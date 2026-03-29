using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Helper.Runtime.Core;
using Helper.Runtime.Infrastructure;

namespace Helper.Runtime.Generation;

public sealed record ParityDailyBackfillRequest(
    string? ReportPath = null,
    string WorkloadClasses = GenerationWorkloadClassifier.Parity,
    string? StartDateUtc = null,
    string? EndDateUtc = null,
    bool OverwriteExisting = false,
    bool DryRun = false,
    bool FailIfNoDays = true);

public sealed record ParityBackfillSourceFileAudit(
    string Path,
    string Sha256,
    long TotalLines,
    long ParsedLines,
    long MalformedLines);

public sealed record ParityDailyBackfillDay(
    string DateUtc,
    DateTimeOffset GeneratedAtUtc,
    long TotalRuns,
    long SuccessfulRuns,
    long FailedRuns,
    double GoldenHitRate,
    double GenerationSuccessRate,
    double P95ReadySeconds,
    double UnknownErrorRate,
    double ToolSuccessRatio,
    long GoldenAttempts,
    long GoldenHits,
    int MinGoldenAttemptsRequired,
    bool GoldenSampleInsufficient,
    IReadOnlyList<string> Alerts,
    string Action,
    string DailySnapshotPath,
    string? DailySnapshotSha256,
    string? HistorySnapshotPath,
    string? HistorySnapshotSha256);

public sealed record ParityDailyBackfillReport(
    DateTimeOffset GeneratedAtUtc,
    string ReportPath,
    string DailySnapshotDirectory,
    string HistorySnapshotDirectory,
    string WorkloadFilter,
    string? StartDateUtc,
    string? EndDateUtc,
    bool OverwriteExisting,
    bool DryRun,
    long SourceLineCount,
    long ParsedLineCount,
    long MalformedLineCount,
    int SourceFileCount,
    long DeduplicatedEntryCount,
    long FilteredEntryCount,
    int DaysConsidered,
    int DaysWritten,
    int DaysSkipped,
    IReadOnlyList<ParityBackfillSourceFileAudit> SourceFiles,
    IReadOnlyList<ParityDailyBackfillDay> Days);

public interface IParityDailyBackfillService
{
    Task<ParityDailyBackfillReport> BackfillAsync(ParityDailyBackfillRequest request, CancellationToken ct = default);
}

public sealed class ParityDailyBackfillService : IParityDailyBackfillService
{
    private readonly GenerationArtifactDiscoveryOptions _discoveryOptions;
    private readonly string _dailyDir;
    private readonly string _historyDir;
    private readonly string _backfillDir;
    private readonly ParityBackfillRunHistoryReader _runHistoryReader;
    private readonly ParityDailyBackfillMetricsCalculator _metricsCalculator;
    private readonly ParityDailyBackfillReportWriter _reportWriter;

    public ParityDailyBackfillService(
        string? workspaceRoot = null,
        GenerationArtifactDiscoveryMode? discoveryMode = null)
        : this(GenerationArtifactDiscoveryOptions.Resolve(workspaceRoot, discoveryMode))
    {
    }

    public ParityDailyBackfillService(GenerationArtifactDiscoveryOptions discoveryOptions)
        : this(discoveryOptions, null, null, null)
    {
    }

    internal ParityDailyBackfillService(
        GenerationArtifactDiscoveryOptions discoveryOptions,
        ParityBackfillRunHistoryReader? runHistoryReader,
        ParityDailyBackfillMetricsCalculator? metricsCalculator,
        ParityDailyBackfillReportWriter? reportWriter)
    {
        _discoveryOptions = discoveryOptions;
        _dailyDir = ParitySnapshotPathResolver.ResolveDailyDirectory(_discoveryOptions.WorkspaceRoot);
        _historyDir = ParitySnapshotPathResolver.ResolveHistoryDirectory(_discoveryOptions.WorkspaceRoot);
        _backfillDir = ParitySnapshotPathResolver.ResolveBackfillDirectory(_discoveryOptions.WorkspaceRoot);
        _runHistoryReader = runHistoryReader ?? new ParityBackfillRunHistoryReader();
        _metricsCalculator = metricsCalculator ?? new ParityDailyBackfillMetricsCalculator();
        _reportWriter = reportWriter ?? new ParityDailyBackfillReportWriter(_discoveryOptions, _backfillDir);
    }

    public async Task<ParityDailyBackfillReport> BackfillAsync(ParityDailyBackfillRequest request, CancellationToken ct = default)
    {
        var sourceSnapshot = await _runHistoryReader.ReadAsync(_discoveryOptions, ct);
        var filterSet = ParseWorkloadFilter(request.WorkloadClasses);
        var filterLabel = filterSet.Count == 0 ? "all" : string.Join(",", filterSet.OrderBy(x => x, StringComparer.Ordinal));
        var startDate = ParseDate(request.StartDateUtc, nameof(request.StartDateUtc));
        var endDate = ParseDate(request.EndDateUtc, nameof(request.EndDateUtc));
        if (startDate.HasValue && endDate.HasValue && startDate.Value > endDate.Value)
        {
            throw new InvalidOperationException($"Invalid date range: {startDate:yyyy-MM-dd} > {endDate:yyyy-MM-dd}.");
        }

        var filtered = sourceSnapshot.Entries
            .Where(x => MatchesWorkload(x.WorkloadClass, filterSet))
            .Where(x =>
            {
                var date = DateOnly.FromDateTime(x.TimelineAnchorUtc.UtcDateTime);
                if (startDate.HasValue && date < startDate.Value)
                {
                    return false;
                }

                if (endDate.HasValue && date > endDate.Value)
                {
                    return false;
                }

                return true;
            })
            .OrderBy(x => x.TimelineAnchorUtc)
            .ToList();

        var byDate = filtered
            .GroupBy(x => DateOnly.FromDateTime(x.TimelineAnchorUtc.UtcDateTime).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), StringComparer.Ordinal)
            .OrderBy(x => x.Key, StringComparer.Ordinal)
            .ToList();

        if (byDate.Count == 0 && request.FailIfNoDays)
        {
            throw new InvalidOperationException("Backfill produced no daily slices after workload/date filters.");
        }

        Directory.CreateDirectory(_dailyDir);
        Directory.CreateDirectory(_historyDir);
        Directory.CreateDirectory(_backfillDir);

        var generatedAtUtc = DateTimeOffset.UtcNow;
        var reportStamp = generatedAtUtc.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture);
        var minGoldenAttempts = ReadInt("HELPER_PARITY_MIN_GOLDEN_ATTEMPTS", 20, 1, 10_000);
        var thresholds = ParityGateThresholds.FromEnvironment();

        var days = new List<ParityDailyBackfillDay>(byDate.Count);
        foreach (var group in byDate)
        {
            ct.ThrowIfCancellationRequested();
            var metrics = _metricsCalculator.Compute(group.ToList(), minGoldenAttempts, thresholds);
            var dailyPath = Path.Combine(_dailyDir, $"parity_{group.Key}.json");
            var dailyPathFull = Path.GetFullPath(dailyPath);
            var historyPath = Path.Combine(_historyDir, $"parity_{group.Key}_{reportStamp}_backfill.json");
            var historyPathFull = Path.GetFullPath(historyPath);

            var existed = File.Exists(dailyPath);
            string action;
            string? dailySha = null;
            string? historySha = null;
            string? writtenHistoryPath = null;

            if (request.DryRun)
            {
                action = existed ? "dry_run_would_overwrite" : "dry_run_would_write";
                if (existed)
                {
                    dailySha = ComputeSha256(dailyPath);
                }
            }
            else if (existed && !request.OverwriteExisting)
            {
                action = "skipped_existing";
                dailySha = ComputeSha256(dailyPath);
            }
            else
            {
                action = existed ? "overwritten" : "written_new";
                var json = JsonSerializer.Serialize(metrics.Snapshot, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(dailyPath, json, Encoding.UTF8, ct);
                await File.WriteAllTextAsync(historyPath, json, Encoding.UTF8, ct);
                writtenHistoryPath = historyPathFull;
                dailySha = ComputeSha256(dailyPath);
                historySha = ComputeSha256(historyPath);
            }

            days.Add(new ParityDailyBackfillDay(
                DateUtc: group.Key,
                GeneratedAtUtc: metrics.Snapshot.GeneratedAtUtc,
                TotalRuns: metrics.TotalRuns,
                SuccessfulRuns: metrics.SuccessfulRuns,
                FailedRuns: metrics.FailedRuns,
                GoldenHitRate: metrics.Snapshot.GoldenHitRate,
                GenerationSuccessRate: metrics.Snapshot.GenerationSuccessRate,
                P95ReadySeconds: metrics.Snapshot.P95ReadySeconds,
                UnknownErrorRate: metrics.Snapshot.UnknownErrorRate,
                ToolSuccessRatio: metrics.Snapshot.ToolSuccessRatio,
                GoldenAttempts: metrics.Snapshot.GoldenAttempts,
                GoldenHits: metrics.Snapshot.GoldenHits,
                MinGoldenAttemptsRequired: metrics.Snapshot.MinGoldenAttemptsRequired,
                GoldenSampleInsufficient: metrics.Snapshot.GoldenSampleInsufficient,
                Alerts: metrics.Snapshot.Alerts,
                Action: action,
                DailySnapshotPath: dailyPathFull,
                DailySnapshotSha256: dailySha,
                HistorySnapshotPath: writtenHistoryPath,
                HistorySnapshotSha256: historySha));
        }

        var daysWritten = days.Count(x => string.Equals(x.Action, "written_new", StringComparison.Ordinal) ||
                                          string.Equals(x.Action, "overwritten", StringComparison.Ordinal));
        var daysSkipped = days.Count(x => string.Equals(x.Action, "skipped_existing", StringComparison.Ordinal));
        var report = new ParityDailyBackfillReport(
            GeneratedAtUtc: generatedAtUtc,
            ReportPath: _reportWriter.ResolveReportPath(request.ReportPath, reportStamp),
            DailySnapshotDirectory: _dailyDir,
            HistorySnapshotDirectory: _historyDir,
            WorkloadFilter: filterLabel,
            StartDateUtc: startDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            EndDateUtc: endDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            OverwriteExisting: request.OverwriteExisting,
            DryRun: request.DryRun,
            SourceLineCount: sourceSnapshot.SourceLineCount,
            ParsedLineCount: sourceSnapshot.ParsedLineCount,
            MalformedLineCount: sourceSnapshot.MalformedLineCount,
            SourceFileCount: sourceSnapshot.SourceFiles.Count,
            DeduplicatedEntryCount: sourceSnapshot.Entries.Count,
            FilteredEntryCount: filtered.Count,
            DaysConsidered: days.Count,
            DaysWritten: daysWritten,
            DaysSkipped: daysSkipped,
            SourceFiles: sourceSnapshot.SourceFiles,
            Days: days);

        await _reportWriter.WriteReportAsync(report, ct);
        return report;
    }

    private static DateOnly? ParseDate(string? raw, string name)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (!DateOnly.TryParseExact(raw.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            throw new InvalidOperationException($"Invalid {name}: '{raw}'. Expected yyyy-MM-dd.");
        }

        return parsed;
    }

    private static IReadOnlySet<string> ParseWorkloadFilter(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        return raw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static bool MatchesWorkload(string workloadClass, IReadOnlySet<string> filter)
    {
        if (filter.Count == 0)
        {
            return true;
        }

        return filter.Contains(workloadClass);
    }

    private static int ReadInt(string envName, int fallback, int min, int max)
    {
        var raw = Environment.GetEnvironmentVariable(envName);
        if (!int.TryParse(raw, out var parsed))
        {
            parsed = fallback;
        }

        return Math.Clamp(parsed, min, max);
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash);
    }
}

