using System.Text;
using System.Text.Json;

namespace Helper.Api.Conversation;

public interface IHumanLikeCommunicationEvalReportWriter
{
    Task<HumanLikeCommunicationEvalExportResult> WriteAsync(
        HumanLikeCommunicationEvalPackage package,
        string outputDirectory,
        CancellationToken ct);
}

public sealed class HumanLikeCommunicationEvalReportWriter : IHumanLikeCommunicationEvalReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public async Task<HumanLikeCommunicationEvalExportResult> WriteAsync(
        HumanLikeCommunicationEvalPackage package,
        string outputDirectory,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new ArgumentException("Output directory must not be empty.", nameof(outputDirectory));
        }

        Directory.CreateDirectory(outputDirectory);
        var jsonPath = Path.Combine(outputDirectory, "human_like_communication_eval_summary.json");
        var markdownPath = Path.Combine(outputDirectory, "human_like_communication_eval_summary.md");
        var generatedAtUtc = DateTimeOffset.UtcNow;

        var payload = new
        {
            generatedAtUtc = generatedAtUtc.ToString("O"),
            packageRoot = package.Paths.RootPath,
            corpusPath = package.Paths.CorpusPath,
            rubricPath = package.Paths.RubricPath,
            rubricVersion = package.Rubric.Version,
            summary = new
            {
                package.Summary.SeedScenarioCount,
                package.Summary.PreparedScenarioCount,
                package.Summary.EndToEndScenarioCount,
                package.Summary.EndToEndRatio,
                package.Summary.GateStatus,
                package.Summary.LanguageDistribution,
                package.Summary.KindDistribution,
                package.Summary.LabelDistribution,
                package.Summary.MissingKinds,
                package.Summary.MissingLabels,
                package.Summary.Alerts
            },
            dimensions = package.Rubric.Dimensions
        };

        await File.WriteAllTextAsync(jsonPath, JsonSerializer.Serialize(payload, JsonOptions), ct).ConfigureAwait(false);
        await File.WriteAllTextAsync(markdownPath, BuildMarkdown(package, generatedAtUtc), ct).ConfigureAwait(false);

        return new HumanLikeCommunicationEvalExportResult(jsonPath, markdownPath, package);
    }

    private static string BuildMarkdown(HumanLikeCommunicationEvalPackage package, DateTimeOffset generatedAtUtc)
    {
        var lines = new List<string>
        {
            "# Human-Like Communication Eval Summary",
            string.Empty,
            $"Generated: `{generatedAtUtc:O}`",
            $"Gate status: `{package.Summary.GateStatus}`",
            $"Rubric version: `{package.Rubric.Version}`",
            $"Corpus path: `{package.Paths.CorpusPath}`",
            $"Rubric path: `{package.Paths.RubricPath}`",
            string.Empty,
            "## Coverage",
            string.Empty,
            $"- Seed scenarios: `{package.Summary.SeedScenarioCount}`",
            $"- Prepared runs: `{package.Summary.PreparedScenarioCount}`",
            $"- End-to-end scenarios: `{package.Summary.EndToEndScenarioCount}`",
            $"- End-to-end ratio: `{package.Summary.EndToEndRatio:P2}`",
            string.Empty,
            "## Language Distribution",
            string.Empty
        };

        AppendDistribution(lines, package.Summary.LanguageDistribution);
        lines.Add(string.Empty);
        lines.Add("## Scenario Classes");
        lines.Add(string.Empty);
        AppendDistribution(lines, package.Summary.KindDistribution);
        lines.Add(string.Empty);
        lines.Add("## Labels");
        lines.Add(string.Empty);
        AppendDistribution(lines, package.Summary.LabelDistribution);
        lines.Add(string.Empty);
        lines.Add("## Rubric");
        lines.Add(string.Empty);

        foreach (var dimension in package.Rubric.Dimensions)
        {
            lines.Add($"- `{dimension.Key}`: {dimension.Description} Guidance: {dimension.ScoringGuidance}");
        }

        lines.Add(string.Empty);
        lines.Add("## Alerts");
        lines.Add(string.Empty);
        if (package.Summary.Alerts.Count == 0)
        {
            lines.Add("- none");
        }
        else
        {
            foreach (var alert in package.Summary.Alerts)
            {
                lines.Add($"- {alert}");
            }
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static void AppendDistribution(ICollection<string> lines, IReadOnlyDictionary<string, int> distribution)
    {
        if (distribution.Count == 0)
        {
            lines.Add("- none");
            return;
        }

        foreach (var item in distribution.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase))
        {
            lines.Add($"- `{item.Key}`: `{item.Value}`");
        }
    }
}

