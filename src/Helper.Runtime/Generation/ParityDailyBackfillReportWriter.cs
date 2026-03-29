using System.Text;
using System.Text.Json;

namespace Helper.Runtime.Generation;

internal sealed class ParityDailyBackfillReportWriter
{
    private readonly GenerationArtifactDiscoveryOptions _discoveryOptions;
    private readonly string _backfillDir;

    public ParityDailyBackfillReportWriter(GenerationArtifactDiscoveryOptions discoveryOptions, string backfillDir)
    {
        _discoveryOptions = discoveryOptions;
        _backfillDir = backfillDir;
    }

    public string ResolveReportPath(string? reportPath, string stamp)
    {
        if (!string.IsNullOrWhiteSpace(reportPath))
        {
            return Path.GetFullPath(reportPath);
        }

        return Path.Combine(_backfillDir, $"HELPER_PARITY_DAILY_BACKFILL_{stamp}.md");
    }

    public async Task WriteReportAsync(ParityDailyBackfillReport report, CancellationToken ct)
    {
        var reportDir = Path.GetDirectoryName(report.ReportPath);
        if (!string.IsNullOrWhiteSpace(reportDir))
        {
            Directory.CreateDirectory(reportDir);
        }

        var sb = new StringBuilder();
        sb.AppendLine($"# Parity Daily Backfill ({report.GeneratedAtUtc:yyyy-MM-dd HH:mm:ss} UTC)");
        sb.AppendLine();
        sb.AppendLine("## Scope");
        sb.AppendLine($"- WorkloadFilter: {report.WorkloadFilter}");
        sb.AppendLine($"- DiscoveryMode: {_discoveryOptions.Mode}");
        sb.AppendLine($"- DiscoveryDirectRoots: {string.Join(" ; ", _discoveryOptions.DirectRoots)}");
        sb.AppendLine($"- DiscoveryRecursiveRoots: {string.Join(" ; ", _discoveryOptions.RecursiveRoots)}");
        sb.AppendLine($"- StartDateUtc: {report.StartDateUtc ?? "none"}");
        sb.AppendLine($"- EndDateUtc: {report.EndDateUtc ?? "none"}");
        sb.AppendLine($"- OverwriteExisting: {report.OverwriteExisting}");
        sb.AppendLine($"- DryRun: {report.DryRun}");
        sb.AppendLine();
        sb.AppendLine("## Sources");
        sb.AppendLine($"- SourceFileCount: {report.SourceFileCount}");
        sb.AppendLine($"- SourceLineCount: {report.SourceLineCount}");
        sb.AppendLine($"- ParsedLineCount: {report.ParsedLineCount}");
        sb.AppendLine($"- MalformedLineCount: {report.MalformedLineCount}");
        sb.AppendLine($"- DeduplicatedEntryCount: {report.DeduplicatedEntryCount}");
        sb.AppendLine($"- FilteredEntryCount: {report.FilteredEntryCount}");
        sb.AppendLine();
        sb.AppendLine("| Source File | SHA256 | TotalLines | Parsed | Malformed |");
        sb.AppendLine("|---|---|---:|---:|---:|");
        foreach (var file in report.SourceFiles)
        {
            sb.AppendLine($"| {Escape(file.Path)} | `{file.Sha256}` | {file.TotalLines} | {file.ParsedLines} | {file.MalformedLines} |");
        }

        sb.AppendLine();
        sb.AppendLine("## Day Actions");
        sb.AppendLine($"- DailySnapshotDirectory: {report.DailySnapshotDirectory}");
        sb.AppendLine($"- HistorySnapshotDirectory: {report.HistorySnapshotDirectory}");
        sb.AppendLine($"- DaysConsidered: {report.DaysConsidered}");
        sb.AppendLine($"- DaysWritten: {report.DaysWritten}");
        sb.AppendLine($"- DaysSkipped: {report.DaysSkipped}");
        sb.AppendLine();
        sb.AppendLine("| Date (UTC) | Action | TotalRuns | Success | GoldenHit | P95(s) | Unknown | Alerts | DailySnapshot |");
        sb.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|---|");
        foreach (var day in report.Days)
        {
            sb.AppendLine($"| {day.DateUtc} | {day.Action} | {day.TotalRuns} | {day.GenerationSuccessRate:P2} | {day.GoldenHitRate:P2} | {day.P95ReadySeconds:0.00} | {day.UnknownErrorRate:P2} | {day.Alerts.Count} | {Escape(day.DailySnapshotPath)} |");
        }

        sb.AppendLine();
        sb.AppendLine("## Audit Notes");
        sb.AppendLine("- Source of truth: generation_runs.jsonl files discovered via canonical projects root with legacy PROJECTS fallback.");
        sb.AppendLine("- ToolSuccessRatio is derived from run success ratio because run logs do not persist per-tool execution outcomes.");
        sb.AppendLine("- No synthetic runs are generated and no manual JSON edits are required.");

        await File.WriteAllTextAsync(report.ReportPath, sb.ToString(), Encoding.UTF8, ct);
        var jsonPath = Path.ChangeExtension(report.ReportPath, ".json");
        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(jsonPath, json, Encoding.UTF8, ct);
    }

    private static string Escape(string value)
    {
        return value.Replace("|", "\\|", StringComparison.Ordinal);
    }
}

