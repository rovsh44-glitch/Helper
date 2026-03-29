using System.Globalization;
using System.Text;
using System.Text.Json;
using Helper.Runtime.Core;
using Helper.Runtime.Infrastructure;

namespace Helper.Runtime.Generation;

public sealed class ParityCertificationService : IParityCertificationService
{
    private readonly IGenerationMetricsService _generationMetrics;
    private readonly IToolAuditService _toolAudit;
    private readonly GenerationArtifactDiscoveryOptions _discoveryOptions;

    public ParityCertificationService(
        IGenerationMetricsService generationMetrics,
        IToolAuditService toolAudit,
        string? workspaceRoot = null,
        GenerationArtifactDiscoveryMode? discoveryMode = null)
        : this(
            generationMetrics,
            toolAudit,
            GenerationArtifactDiscoveryOptions.Resolve(workspaceRoot, discoveryMode))
    {
    }

    public ParityCertificationService(
        IGenerationMetricsService generationMetrics,
        IToolAuditService toolAudit,
        GenerationArtifactDiscoveryOptions discoveryOptions)
    {
        _generationMetrics = generationMetrics;
        _toolAudit = toolAudit;
        _discoveryOptions = discoveryOptions;
    }

    public async Task<ParityCertificationReport> GenerateAsync(string? reportPath = null, CancellationToken ct = default)
    {
        var generationSnapshot = _generationMetrics.GetSnapshot();
        var toolSnapshot = _toolAudit.GetSnapshot();
        var lookbackHours = ReadInt("HELPER_PARITY_LOOKBACK_HOURS", 24, 1, 24 * 30);
        var history = await ParityRunHistoryAnalyzer.AnalyzeAsync(_discoveryOptions, lookbackHours, generationSnapshot, ct);

        var alerts = BuildAlerts(history, toolSnapshot.SuccessRatio);

        var targetPath = ResolveReportPath(reportPath);
        var markdown = BuildMarkdownReport(
            generationSnapshot,
            toolSnapshot,
            history,
            alerts);

        var directory = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(targetPath, markdown, Encoding.UTF8, ct);
        await WriteMachineReadableSnapshotAsync(targetPath, toolSnapshot, history, alerts, ct);
        var report = new ParityCertificationReport(
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            ReportPath: targetPath,
            TotalRuns: history.TotalRuns,
            GoldenHitRate: history.GoldenHitRate,
            GenerationSuccessRate: history.SuccessRate,
            P95ReadySeconds: history.P95ReadySeconds,
            UnknownErrorRate: history.UnknownErrorRate,
            ToolSuccessRatio: toolSnapshot.SuccessRatio,
            Alerts: alerts,
            GoldenAttempts: history.GoldenAttempts,
            GoldenHits: history.GoldenHits,
            MinGoldenAttemptsRequired: history.MinGoldenAttemptsRequired,
            GoldenSampleInsufficient: history.GoldenSampleInsufficient);
        await WriteDailySnapshotAsync(report, ct);
        return report;
    }

    private static IReadOnlyList<string> BuildAlerts(
        ParityRunHistorySnapshot history,
        double toolSuccessRatio)
    {
        var alerts = new List<string>();
        if (history.GoldenSampleInsufficient)
        {
            alerts.Add(
                $"insufficient_golden_sample: {history.GoldenAttempts} < {history.MinGoldenAttemptsRequired} (GoldenSource={history.GoldenSource}).");
        }
        else if (history.GoldenHitRate < 0.90)
        {
            alerts.Add($"Golden template hit rate below target: {history.GoldenHitRate:P1} < 90%.");
        }

        if (history.SuccessRate < 0.95)
        {
            alerts.Add($"Generation success rate below target: {history.SuccessRate:P1} < 95%.");
        }

        if (history.P95ReadySeconds > 25)
        {
            alerts.Add($"P95 ready latency above target: {history.P95ReadySeconds:0.0}s > 25s.");
        }

        if (history.UnknownErrorRate > 0.05)
        {
            alerts.Add($"Unknown error rate above target: {history.UnknownErrorRate:P1} > 5%.");
        }

        if (toolSuccessRatio < 0.90)
        {
            alerts.Add($"Tool success ratio below target: {toolSuccessRatio:P1} < 90%.");
        }

        return alerts;
    }

    private string ResolveReportPath(string? reportPath)
    {
        if (!string.IsNullOrWhiteSpace(reportPath))
        {
            return Path.GetFullPath(reportPath);
        }

        var docDir = Path.Combine(_discoveryOptions.WorkspaceRoot, "doc");
        return Path.Combine(docDir, $"HELPER_PARITY_CERTIFICATION_SNAPSHOT_{DateTime.UtcNow:yyyy-MM-dd_HH-mm-ss}.md");
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

    private static string BuildMarkdownReport(
        GenerationMetricsSnapshot generationSnapshot,
        ToolAuditSnapshot toolSnapshot,
        ParityRunHistorySnapshot history,
        IReadOnlyList<string> alerts)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Helper Parity Certification Snapshot ({DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC)");
        sb.AppendLine();
        sb.AppendLine("## Scope");
        sb.AppendLine($"- LookbackHours: {history.LookbackHours}");
        sb.AppendLine($"- LoadedRunEntries: {history.LoadedEntries}");
        sb.AppendLine($"- WindowRunEntriesBeforeWorkloadFilter: {history.WindowEntriesBeforeWorkloadFilter}");
        sb.AppendLine($"- WindowRunEntries: {history.WindowEntries}");
        sb.AppendLine($"- WorkloadFilter: {history.WorkloadFilter}");
        sb.AppendLine($"- GoldenSource: {history.GoldenSource}");
        sb.AppendLine($"- DiscoveryMode: {history.DiscoveryMode}");
        sb.AppendLine($"- DiscoveryDirectRoots: {FormatRoots(history.DiscoveryDirectRoots)}");
        sb.AppendLine($"- DiscoveryRecursiveRoots: {FormatRoots(history.DiscoveryRecursiveRoots)}");
        sb.AppendLine();
        sb.AppendLine("## KPI");
        sb.AppendLine($"- Total Runs: {history.TotalRuns}");
        sb.AppendLine($"- Clean Success Runs: {history.CleanSuccessRuns}");
        sb.AppendLine($"- Degraded Success Runs: {history.DegradedSuccessRuns}");
        sb.AppendLine($"- Hard Failed Runs: {history.HardFailedRuns}");
        sb.AppendLine($"- Golden Hit Rate: {history.GoldenHitRate:P2}");
        sb.AppendLine($"- Generation Success Rate: {history.SuccessRate:P2}");
        sb.AppendLine($"- P95 Ready Seconds: {history.P95ReadySeconds:0.00}");
        sb.AppendLine($"- Unknown Error Rate: {history.UnknownErrorRate:P2}");
        sb.AppendLine($"- Tool Success Ratio: {toolSnapshot.SuccessRatio:P2}");
        sb.AppendLine();
        sb.AppendLine("## Golden Route Evidence");
        sb.AppendLine($"- golden_attempts: {history.GoldenAttempts}");
        sb.AppendLine($"- golden_hits: {history.GoldenHits}");
        sb.AppendLine($"- min_golden_attempts_required: {history.MinGoldenAttemptsRequired}");
        sb.AppendLine($"- golden_sample_insufficient: {history.GoldenSampleInsufficient}");
        sb.AppendLine();
        sb.AppendLine("## Run History Counters (aggregated generation_runs.jsonl)");
        sb.AppendLine($"- run_history_total: {history.TotalRuns}");
        sb.AppendLine($"- run_history_success_total: {history.SuccessfulRuns}");
        sb.AppendLine($"- run_history_failed_total: {history.FailedRuns}");
        sb.AppendLine();
        sb.AppendLine("## Top Error Codes (window)");
        if (history.TopErrorCodeTotals.Count == 0)
        {
            sb.AppendLine("- none");
        }
        else
        {
            foreach (var entry in history.TopErrorCodeTotals)
            {
                sb.AppendLine($"- {entry.Key}: {entry.Value}");
            }
        }
        sb.AppendLine();
        sb.AppendLine("## Failure Taxonomy (window)");
        if (history.FailureCategoryTotals.Count == 0)
        {
            sb.AppendLine("- none");
        }
        else
        {
            foreach (var entry in history.FailureCategoryTotals)
            {
                sb.AppendLine($"- {entry.Key}: {entry.Value}");
            }
        }
        sb.AppendLine();
        sb.AppendLine("## Representative Failed Runs");
        if (history.FailureSamples.Count == 0)
        {
            sb.AppendLine("- none");
        }
        else
        {
            foreach (var sample in history.FailureSamples)
            {
                var templateId = string.IsNullOrWhiteSpace(sample.RoutedTemplateId) ? "n/a" : sample.RoutedTemplateId;
                var evidence = string.IsNullOrWhiteSpace(sample.PrimaryEvidence) ? "n/a" : sample.PrimaryEvidence;
                sb.AppendLine($"- {sample.RunId} | {sample.Category} | disposition={sample.Disposition} | template={templateId} | evidence={evidence}");
            }
        }
        sb.AppendLine();
        sb.AppendLine("## Runtime Session Counters (in-memory)");
        sb.AppendLine($"- generation_runs_total: {generationSnapshot.GenerationRunsTotal}");
        sb.AppendLine($"- generation_golden_template_hit_total: {generationSnapshot.GenerationGoldenTemplateHitTotal}");
        sb.AppendLine($"- generation_golden_template_miss_total: {generationSnapshot.GenerationGoldenTemplateMissTotal}");
        sb.AppendLine($"- generation_timeout_routing_total: {generationSnapshot.GenerationTimeoutRoutingTotal}");
        sb.AppendLine($"- generation_timeout_forge_total: {generationSnapshot.GenerationTimeoutForgeTotal}");
        sb.AppendLine($"- generation_timeout_synthesis_total: {generationSnapshot.GenerationTimeoutSynthesisTotal}");
        sb.AppendLine($"- generation_timeout_autofix_total: {generationSnapshot.GenerationTimeoutAutofixTotal}");
        sb.AppendLine($"- generation_timeout_unknown_total: {generationSnapshot.GenerationTimeoutUnknownTotal}");
        sb.AppendLine($"- generation_autofix_attempts_total: {generationSnapshot.GenerationAutofixAttemptsTotal}");
        sb.AppendLine($"- generation_autofix_success_total: {generationSnapshot.GenerationAutofixSuccessTotal}");
        sb.AppendLine($"- generation_autofix_fail_total: {generationSnapshot.GenerationAutofixFailTotal}");
        sb.AppendLine();
        sb.AppendLine("## Alerts");
        if (alerts.Count == 0)
        {
            sb.AppendLine("- none");
        }
        else
        {
            foreach (var alert in alerts)
            {
                sb.AppendLine($"- {alert}");
            }
        }

        return sb.ToString();
    }

    private static string FormatRoots(IReadOnlyList<string> roots)
    {
        if (roots.Count == 0)
        {
            return "none";
        }

        return string.Join(" ; ", roots);
    }

    private static async Task WriteMachineReadableSnapshotAsync(
        string reportPath,
        ToolAuditSnapshot toolSnapshot,
        ParityRunHistorySnapshot history,
        IReadOnlyList<string> alerts,
        CancellationToken ct)
    {
        var snapshot = new ParityCertificationSnapshotDocument(
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            ReportPath: reportPath,
            LookbackHours: history.LookbackHours,
            WorkloadFilter: history.WorkloadFilter,
            TotalRuns: history.TotalRuns,
            CleanSuccessRuns: history.CleanSuccessRuns,
            DegradedSuccessRuns: history.DegradedSuccessRuns,
            HardFailedRuns: history.HardFailedRuns,
            GoldenHitRate: history.GoldenHitRate,
            GenerationSuccessRate: history.SuccessRate,
            P95ReadySeconds: history.P95ReadySeconds,
            UnknownErrorRate: history.UnknownErrorRate,
            ToolSuccessRatio: toolSnapshot.SuccessRatio,
            Alerts: alerts,
            FailureCategoryTotals: history.FailureCategoryTotals,
            FailureSamples: history.FailureSamples,
            Discovery: new ParityDiscoveryAuditSnapshot(
                Mode: history.DiscoveryMode.ToString(),
                DirectRoots: history.DiscoveryDirectRoots,
                RecursiveRoots: history.DiscoveryRecursiveRoots));
        var jsonPath = Path.ChangeExtension(reportPath, ".json");
        var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(jsonPath, json, Encoding.UTF8, ct);
    }

    private async Task WriteDailySnapshotAsync(ParityCertificationReport report, CancellationToken ct)
    {
        var dailyDir = ParitySnapshotPathResolver.ResolveDailyDirectory(_discoveryOptions.WorkspaceRoot);
        var historyDir = ParitySnapshotPathResolver.ResolveHistoryDirectory(_discoveryOptions.WorkspaceRoot);
        Directory.CreateDirectory(dailyDir);
        Directory.CreateDirectory(historyDir);

        var dateKey = report.GeneratedAtUtc.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var snapshot = new ParityDailySnapshot(
            DateUtc: dateKey,
            GeneratedAtUtc: report.GeneratedAtUtc,
            TotalRuns: report.TotalRuns,
            GoldenHitRate: report.GoldenHitRate,
            GenerationSuccessRate: report.GenerationSuccessRate,
            P95ReadySeconds: report.P95ReadySeconds,
            UnknownErrorRate: report.UnknownErrorRate,
            ToolSuccessRatio: report.ToolSuccessRatio,
            Alerts: report.Alerts,
            GoldenAttempts: report.GoldenAttempts,
            GoldenHits: report.GoldenHits,
            MinGoldenAttemptsRequired: report.MinGoldenAttemptsRequired,
            GoldenSampleInsufficient: report.GoldenSampleInsufficient);
        var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });

        var dailyPath = Path.Combine(dailyDir, $"parity_{dateKey}.json");
        await File.WriteAllTextAsync(dailyPath, json, Encoding.UTF8, ct);

        var historyPath = Path.Combine(historyDir, $"parity_{report.GeneratedAtUtc:yyyy-MM-dd_HH-mm-ss}.json");
        await File.WriteAllTextAsync(historyPath, json, Encoding.UTF8, ct);
    }
}

internal sealed record ParityCertificationSnapshotDocument(
    DateTimeOffset GeneratedAtUtc,
    string ReportPath,
    int LookbackHours,
    string WorkloadFilter,
    long TotalRuns,
    long CleanSuccessRuns,
    long DegradedSuccessRuns,
    long HardFailedRuns,
    double GoldenHitRate,
    double GenerationSuccessRate,
    double P95ReadySeconds,
    double UnknownErrorRate,
    double ToolSuccessRatio,
    IReadOnlyList<string> Alerts,
    IReadOnlyDictionary<string, long> FailureCategoryTotals,
    IReadOnlyList<ParityFailureSample> FailureSamples,
    ParityDiscoveryAuditSnapshot Discovery);

internal sealed record ParityDiscoveryAuditSnapshot(
    string Mode,
    IReadOnlyList<string> DirectRoots,
    IReadOnlyList<string> RecursiveRoots);

