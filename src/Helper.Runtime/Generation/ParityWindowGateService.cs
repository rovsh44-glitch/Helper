using System.Globalization;
using System.Text;
using System.Text.Json;
using Helper.Runtime.Infrastructure;

namespace Helper.Runtime.Generation;

public sealed record ParityDailySnapshot(
    string DateUtc,
    DateTimeOffset GeneratedAtUtc,
    long TotalRuns,
    double GoldenHitRate,
    double GenerationSuccessRate,
    double P95ReadySeconds,
    double UnknownErrorRate,
    double ToolSuccessRatio,
    IReadOnlyList<string> Alerts,
    long GoldenAttempts = 0,
    long GoldenHits = 0,
    int MinGoldenAttemptsRequired = 0,
    bool GoldenSampleInsufficient = false);

public sealed record ParityWindowDayResult(
    string DateUtc,
    bool Passed,
    IReadOnlyList<string> Violations,
    ParityDailySnapshot Snapshot);

public sealed record ParityWindowGateReport(
    DateTimeOffset GeneratedAtUtc,
    int WindowDays,
    int AvailableDays,
    bool WindowComplete,
    bool Passed,
    string ReportPath,
    IReadOnlyList<string> Violations,
    IReadOnlyList<ParityWindowDayResult> Days);

public interface IParityWindowGateService
{
    Task<ParityWindowGateReport> EvaluateAsync(
        int windowDays = 7,
        string? reportPath = null,
        CancellationToken ct = default);
}

public sealed class ParityWindowGateService : IParityWindowGateService
{
    private readonly string _workspaceRoot;
    private readonly string _snapshotDirectory;
    private readonly IParityGateEvaluator _gateEvaluator;

    public ParityWindowGateService(
        IParityGateEvaluator gateEvaluator,
        string? workspaceRoot = null)
    {
        _gateEvaluator = gateEvaluator;
        _workspaceRoot = string.IsNullOrWhiteSpace(workspaceRoot)
            ? HelperWorkspacePathResolver.ResolveHelperRoot()
            : Path.GetFullPath(workspaceRoot);
        _snapshotDirectory = ParitySnapshotPathResolver.ResolveDailyDirectory(_workspaceRoot);
    }

    public async Task<ParityWindowGateReport> EvaluateAsync(
        int windowDays = 7,
        string? reportPath = null,
        CancellationToken ct = default)
    {
        windowDays = Math.Clamp(windowDays, 1, 30);
        var thresholds = ParityGateThresholds.FromEnvironment();
        var allowIncompleteWindow = ReadFlag("HELPER_PARITY_WINDOW_ALLOW_INCOMPLETE", false);
        var enforceAlerts = ReadFlag("HELPER_PARITY_GATE_ENFORCE_REPORT_ALERTS", false);

        var snapshots = await LoadSnapshotsAsync(ct);
        var latestByDate = snapshots
            .GroupBy(x => x.DateUtc, StringComparer.Ordinal)
            .Select(g => g.OrderByDescending(x => x.GeneratedAtUtc).First())
            .OrderByDescending(x => x.DateUtc, StringComparer.Ordinal)
            .Take(windowDays)
            .OrderBy(x => x.DateUtc, StringComparer.Ordinal)
            .ToList();

        var dayResults = new List<ParityWindowDayResult>(latestByDate.Count);
        foreach (var snapshot in latestByDate)
        {
            var dayReport = new Helper.Runtime.Core.ParityCertificationReport(
                GeneratedAtUtc: snapshot.GeneratedAtUtc,
                ReportPath: $"{snapshot.DateUtc}.json",
                TotalRuns: snapshot.TotalRuns,
                GoldenHitRate: snapshot.GoldenHitRate,
                GenerationSuccessRate: snapshot.GenerationSuccessRate,
                P95ReadySeconds: snapshot.P95ReadySeconds,
                UnknownErrorRate: snapshot.UnknownErrorRate,
                ToolSuccessRatio: snapshot.ToolSuccessRatio,
                Alerts: snapshot.Alerts,
                GoldenAttempts: snapshot.GoldenAttempts,
                GoldenHits: snapshot.GoldenHits,
                MinGoldenAttemptsRequired: snapshot.MinGoldenAttemptsRequired,
                GoldenSampleInsufficient: snapshot.GoldenSampleInsufficient);
            var decision = _gateEvaluator.Evaluate(dayReport, thresholds);
            var violations = decision.Violations.ToList();
            if (enforceAlerts && snapshot.Alerts.Count > 0)
            {
                violations.Add($"Report alerts present for {snapshot.DateUtc}: {string.Join("; ", snapshot.Alerts)}");
            }

            dayResults.Add(new ParityWindowDayResult(
                DateUtc: snapshot.DateUtc,
                Passed: violations.Count == 0,
                Violations: violations,
                Snapshot: snapshot));
        }

        var windowComplete = dayResults.Count >= windowDays;
        var violationsSummary = new List<string>();
        if (!windowComplete && !allowIncompleteWindow)
        {
            violationsSummary.Add($"Window incomplete: required {windowDays} day(s), found {dayResults.Count}.");
        }

        foreach (var day in dayResults.Where(x => !x.Passed))
        {
            violationsSummary.Add($"{day.DateUtc}: {string.Join("; ", day.Violations)}");
        }

        var passed = violationsSummary.Count == 0 && (windowComplete || allowIncompleteWindow);
        var resolvedReportPath = ResolveReportPath(reportPath);
        await WriteReportAsync(resolvedReportPath, windowDays, windowComplete, passed, violationsSummary, dayResults, ct);

        return new ParityWindowGateReport(
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            WindowDays: windowDays,
            AvailableDays: dayResults.Count,
            WindowComplete: windowComplete,
            Passed: passed,
            ReportPath: resolvedReportPath,
            Violations: violationsSummary,
            Days: dayResults);
    }

    private async Task<IReadOnlyList<ParityDailySnapshot>> LoadSnapshotsAsync(CancellationToken ct)
    {
        if (!Directory.Exists(_snapshotDirectory))
        {
            return Array.Empty<ParityDailySnapshot>();
        }

        var snapshots = new List<ParityDailySnapshot>();
        foreach (var file in Directory.EnumerateFiles(_snapshotDirectory, "*.json", SearchOption.TopDirectoryOnly))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var json = await File.ReadAllTextAsync(file, ct);
                var dto = JsonSerializer.Deserialize<ParityDailySnapshotDto>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                if (dto is null)
                {
                    continue;
                }

                var date = NormalizeDate(dto.DateUtc, dto.GeneratedAtUtc);
                snapshots.Add(new ParityDailySnapshot(
                    DateUtc: date,
                    GeneratedAtUtc: dto.GeneratedAtUtc,
                    TotalRuns: Math.Max(0, dto.TotalRuns),
                    GoldenHitRate: ClampRate(dto.GoldenHitRate),
                    GenerationSuccessRate: ClampRate(dto.GenerationSuccessRate),
                    P95ReadySeconds: Math.Max(0, dto.P95ReadySeconds),
                    UnknownErrorRate: ClampRate(dto.UnknownErrorRate),
                    ToolSuccessRatio: ClampRate(dto.ToolSuccessRatio),
                    Alerts: dto.Alerts?.Where(x => !string.IsNullOrWhiteSpace(x)).ToList() ?? new List<string>(),
                    GoldenAttempts: Math.Max(0, dto.GoldenAttempts),
                    GoldenHits: Math.Max(0, dto.GoldenHits),
                    MinGoldenAttemptsRequired: Math.Max(0, dto.MinGoldenAttemptsRequired),
                    GoldenSampleInsufficient: dto.GoldenSampleInsufficient));
            }
            catch
            {
                // skip malformed snapshots
            }
        }

        return snapshots;
    }

    private async Task WriteReportAsync(
        string reportPath,
        int windowDays,
        bool windowComplete,
        bool passed,
        IReadOnlyList<string> violations,
        IReadOnlyList<ParityWindowDayResult> days,
        CancellationToken ct)
    {
        var directory = Path.GetDirectoryName(reportPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var sb = new StringBuilder();
        sb.AppendLine($"# Helper Parity Window Gate ({DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC)");
        sb.AppendLine();
        sb.AppendLine($"- Window Days: {windowDays}");
        sb.AppendLine($"- Available Days: {days.Count}");
        sb.AppendLine($"- Window Complete: {windowComplete}");
        sb.AppendLine($"- Passed: {passed}");
        sb.AppendLine();
        sb.AppendLine("## Day Results");
        sb.AppendLine("| Date (UTC) | Passed | Golden Hit | Success | P95 Ready (s) | Unknown Error | Tool Success | Alerts |");
        sb.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|");
        foreach (var day in days)
        {
            sb.AppendLine($"| {day.DateUtc} | {(day.Passed ? "pass" : "fail")} | {day.Snapshot.GoldenHitRate:P2} | {day.Snapshot.GenerationSuccessRate:P2} | {day.Snapshot.P95ReadySeconds:0.00} | {day.Snapshot.UnknownErrorRate:P2} | {day.Snapshot.ToolSuccessRatio:P2} | {day.Snapshot.Alerts.Count} |");
        }

        sb.AppendLine();
        sb.AppendLine("## Violations");
        if (violations.Count == 0)
        {
            sb.AppendLine("- none");
        }
        else
        {
            foreach (var violation in violations)
            {
                sb.AppendLine($"- {violation}");
            }
        }

        await File.WriteAllTextAsync(reportPath, sb.ToString(), Encoding.UTF8, ct);

        var jsonSidecar = Path.ChangeExtension(reportPath, ".json");
        var payload = new
        {
            generatedAtUtc = DateTimeOffset.UtcNow,
            windowDays,
            availableDays = days.Count,
            windowComplete,
            passed,
            violations,
            days
        };
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(jsonSidecar, json, Encoding.UTF8, ct);
    }

    private string ResolveReportPath(string? reportPath)
    {
        if (!string.IsNullOrWhiteSpace(reportPath))
        {
            return Path.GetFullPath(reportPath);
        }

        return Path.Combine(_workspaceRoot, "doc", $"HELPER_PARITY_WINDOW_GATE_{DateTime.UtcNow:yyyy-MM-dd_HH-mm-ss}.md");
    }

    private static string ResolveWorkspaceRoot(string startDirectory)
    {
        var current = new DirectoryInfo(startDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Helper.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return startDirectory;
    }

    private static bool ReadFlag(string envName, bool fallback)
    {
        var raw = Environment.GetEnvironmentVariable(envName);
        return bool.TryParse(raw, out var parsed) ? parsed : fallback;
    }

    private static string NormalizeDate(string? rawDate, DateTimeOffset generatedAtUtc)
    {
        if (!string.IsNullOrWhiteSpace(rawDate) &&
            DateOnly.TryParseExact(rawDate.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            return parsed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        return DateOnly.FromDateTime(generatedAtUtc.UtcDateTime).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private static double ClampRate(double value)
    {
        return Math.Clamp(value, 0, 1);
    }

    private sealed class ParityDailySnapshotDto
    {
        public string? DateUtc { get; init; }
        public DateTimeOffset GeneratedAtUtc { get; init; }
        public long TotalRuns { get; init; }
        public double GoldenHitRate { get; init; }
        public double GenerationSuccessRate { get; init; }
        public double P95ReadySeconds { get; init; }
        public double UnknownErrorRate { get; init; }
        public double ToolSuccessRatio { get; init; }
        public List<string>? Alerts { get; init; }
        public long GoldenAttempts { get; init; }
        public long GoldenHits { get; init; }
        public int MinGoldenAttemptsRequired { get; init; }
        public bool GoldenSampleInsufficient { get; init; }
    }
}

