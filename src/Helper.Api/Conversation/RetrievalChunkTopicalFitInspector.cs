using System.Globalization;
using Helper.Runtime.Core;

namespace Helper.Api.Conversation;

internal sealed record RetrievalChunkTopicalFitSummary(
    double AverageScore,
    double GenericShare,
    string AggregateLabel,
    bool HasAssessments,
    bool NeedsDeeperRetrieval,
    IReadOnlyList<string> Trace)
{
    public bool IsMeaningfullyBetterThan(RetrievalChunkTopicalFitSummary other)
    {
        if (AverageScore >= other.AverageScore + 0.08d)
        {
            return true;
        }

        if (GenericShare + 0.25d <= other.GenericShare && AverageScore >= other.AverageScore - 0.02d)
        {
            return true;
        }

        return LabelRank(AggregateLabel) > LabelRank(other.AggregateLabel) && AverageScore >= other.AverageScore - 0.02d;
    }

    private static int LabelRank(string label)
    {
        return label switch
        {
            "high" => 3,
            "medium" => 2,
            "low" => 1,
            _ => 0
        };
    }
}

internal static class RetrievalChunkTopicalFitInspector
{
    public static RetrievalChunkTopicalFitSummary Summarize(IReadOnlyList<KnowledgeChunk> chunks, double topicalFitFloor)
    {
        if (chunks.Count == 0)
        {
            return new RetrievalChunkTopicalFitSummary(
                0d,
                1d,
                "missing",
                HasAssessments: false,
                NeedsDeeperRetrieval: true,
                Trace: new[]
                {
                    "topical_fit_avg:0.000",
                    "topical_fit_label:missing",
                    "topical_fit_generic_share:1.000",
                    "topical_fit_domains:none"
                });
        }

        var entries = chunks.Select(ParseEntry).ToList();
        var hasAssessments = entries.Any(static entry => entry.HasAssessment);
        var averageScore = entries.Average(static entry => entry.Score);
        var genericShare = entries.Count(static entry => entry.GenericDomain) / (double)entries.Count;
        var highCount = entries.Count(static entry => string.Equals(entry.Label, "high", StringComparison.OrdinalIgnoreCase));
        var lowCount = entries.Count(static entry => string.Equals(entry.Label, "low", StringComparison.OrdinalIgnoreCase));
        var aggregateLabel = averageScore >= 0.72d
            ? "high"
            : averageScore >= topicalFitFloor
                ? "medium"
                : "low";
        var needsDeeperRetrieval = hasAssessments &&
                                   (averageScore < topicalFitFloor ||
                                    (genericShare >= 0.50d && highCount == 0) ||
                                    lowCount == entries.Count ||
                                    entries.Any(static entry => entry.SuggestDeeperRetrieval));
        var domainTrace = string.Join(
            ",",
            entries
                .Select(static entry => $"{entry.Domain}:{entry.Label}")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(4));

        return new RetrievalChunkTopicalFitSummary(
            averageScore,
            genericShare,
            aggregateLabel,
            hasAssessments,
            needsDeeperRetrieval,
            new[]
            {
                $"topical_fit_avg:{averageScore.ToString("0.000", CultureInfo.InvariantCulture)}",
                $"topical_fit_label:{aggregateLabel}",
                $"topical_fit_generic_share:{genericShare.ToString("0.000", CultureInfo.InvariantCulture)}",
                $"topical_fit_domains:{domainTrace}"
            });
    }

    private static RetrievalChunkFitEntry ParseEntry(KnowledgeChunk chunk)
    {
        var metadata = chunk.Metadata;
        var hasAssessment = metadata.ContainsKey("topical_fit_score") || metadata.ContainsKey("topical_fit_label");
        var score = ParseDouble(metadata.GetValueOrDefault("topical_fit_score"));
        var label = metadata.GetValueOrDefault("topical_fit_label");
        if (string.IsNullOrWhiteSpace(label))
        {
            score = score > 0d
                ? score
                : HasTraceability(metadata)
                    ? 0.60d
                    : 0.48d;
            label = score >= 0.72d
                ? "high"
                : score >= 0.48d
                    ? "medium"
                    : "low";
        }

        var domain = metadata.GetValueOrDefault("domain", metadata.GetValueOrDefault("collection", chunk.Collection));
        var genericDomain = string.Equals(metadata.GetValueOrDefault("topical_fit_generic_domain"), "true", StringComparison.OrdinalIgnoreCase);
        var suggestDeeperRetrieval = string.Equals(metadata.GetValueOrDefault("topical_fit_suggest_deeper_retrieval"), "true", StringComparison.OrdinalIgnoreCase);
        return new RetrievalChunkFitEntry(score, label, domain, genericDomain, suggestDeeperRetrieval, hasAssessment);
    }

    private static double ParseDouble(string? value)
        => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0d;

    private static bool HasTraceability(IReadOnlyDictionary<string, string> metadata)
    {
        return !string.IsNullOrWhiteSpace(metadata.GetValueOrDefault("document_id")) &&
               (!string.IsNullOrWhiteSpace(metadata.GetValueOrDefault("section_path")) ||
                !string.IsNullOrWhiteSpace(metadata.GetValueOrDefault("page_start")));
    }

    private sealed record RetrievalChunkFitEntry(
        double Score,
        string Label,
        string Domain,
        bool GenericDomain,
        bool SuggestDeeperRetrieval,
        bool HasAssessment);
}

