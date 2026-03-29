using System.Text;
using System.Text.Json;

namespace Helper.Api.Conversation;

public interface IWebResearchParityEvalReportWriter
{
    Task<WebResearchParityEvalExportResult> WriteAsync(
        WebResearchParityEvalPackage package,
        string outputDirectory,
        CancellationToken ct);
}

public sealed class WebResearchParityEvalReportWriter : IWebResearchParityEvalReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public async Task<WebResearchParityEvalExportResult> WriteAsync(
        WebResearchParityEvalPackage package,
        string outputDirectory,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new ArgumentException("Output directory must not be empty.", nameof(outputDirectory));
        }

        var resolvedOutputDirectory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(resolvedOutputDirectory);

        var jsonPath = Path.Combine(resolvedOutputDirectory, "web_research_parity_eval_summary.json");
        var markdownPath = Path.Combine(resolvedOutputDirectory, "web_research_parity_eval_summary.md");
        var json = JsonSerializer.Serialize(new
        {
            rubricVersion = package.Rubric.Version,
            generatedAtUtc = DateTimeOffset.UtcNow,
            package.Paths.RootPath,
            summary = package.Summary
        }, JsonOptions);

        var markdown = BuildMarkdown(package);

        await File.WriteAllTextAsync(jsonPath, json, Encoding.UTF8, ct).ConfigureAwait(false);
        await File.WriteAllTextAsync(markdownPath, markdown, Encoding.UTF8, ct).ConfigureAwait(false);

        return new WebResearchParityEvalExportResult(jsonPath, markdownPath, package);
    }

    private static string BuildMarkdown(WebResearchParityEvalPackage package)
    {
        var summary = package.Summary;
        var sb = new StringBuilder();
        sb.AppendLine("# Web-Research Parity Eval Summary");
        sb.AppendLine();
        sb.AppendLine($"Rubric version: `{package.Rubric.Version}`");
        sb.AppendLine($"Gate status: `{summary.GateStatus}`");
        sb.AppendLine($"Seed scenarios: `{summary.SeedScenarioCount}`");
        sb.AppendLine($"Prepared scenarios: `{summary.PreparedScenarioCount}`");
        sb.AppendLine($"End-to-end ratio: `{summary.EndToEndRatio:F2}`");
        sb.AppendLine($"Provider fixtures: `{summary.ProviderFixtureCount}`");
        sb.AppendLine($"Page fixtures: `{summary.PageFixtureCount}`");
        sb.AppendLine();
        sb.AppendLine("Required metrics:");
        foreach (var metric in package.Rubric.RequiredMetrics)
        {
            sb.AppendLine($"- `{metric}`");
        }

        if (summary.Alerts.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Alerts:");
            foreach (var alert in summary.Alerts)
            {
                sb.AppendLine($"- {alert}");
            }
        }

        return sb.ToString();
    }
}

