using System.Globalization;
using Helper.Runtime.Core;

namespace Helper.Api.Conversation;

internal sealed record RetrievalSourceDiversitySummary(
    double Dominance,
    int DistinctSources,
    int DistinctCollections,
    bool GuardApplied,
    bool NeedsBroaderRetrieval,
    IReadOnlyList<string> Trace)
{
    public bool IsMeaningfullyBetterThan(RetrievalSourceDiversitySummary other)
    {
        if (DistinctSources > other.DistinctSources && Dominance <= other.Dominance + 0.05d)
        {
            return true;
        }

        if (DistinctCollections > other.DistinctCollections && Dominance <= other.Dominance + 0.05d)
        {
            return true;
        }

        return Dominance <= other.Dominance - 0.18d;
    }
}

internal static class RetrievalSourceDiversityInspector
{
    public static RetrievalSourceDiversitySummary Summarize(IReadOnlyList<KnowledgeChunk> chunks, double dominanceThreshold)
    {
        if (chunks.Count == 0)
        {
            return new RetrievalSourceDiversitySummary(
                0d,
                0,
                0,
                GuardApplied: false,
                NeedsBroaderRetrieval: false,
                Trace: new[]
                {
                    "source_diversity_dominance:0.000",
                    "source_diversity_distinct_sources:0",
                    "source_diversity_distinct_collections:0",
                    "source_diversity_guard:none",
                    "source_diversity_alternative_collections:none"
                });
        }

        var sourceGroups = chunks
            .GroupBy(ResolveSourceKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => new { SourceKey = group.Key, Count = group.Count() })
            .OrderByDescending(static group => group.Count)
            .ThenBy(static group => group.SourceKey, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var distinctCollections = chunks
            .Select(chunk => chunk.Metadata.GetValueOrDefault("collection", chunk.Collection))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static item => item, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var dominance = sourceGroups[0].Count / (double)chunks.Count;
        var guardApplied = chunks.Any(chunk => string.Equals(chunk.Metadata.GetValueOrDefault("source_diversity_guard_applied"), "true", StringComparison.OrdinalIgnoreCase));
        var needsBroaderRetrieval = chunks.Count >= 3 && dominance > dominanceThreshold && sourceGroups.Count < chunks.Count;
        var guardState = guardApplied
            ? "applied"
            : needsBroaderRetrieval
                ? "needed"
                : "not_needed";

        return new RetrievalSourceDiversitySummary(
            dominance,
            sourceGroups.Count,
            distinctCollections.Length,
            guardApplied,
            needsBroaderRetrieval,
            new[]
            {
                $"source_diversity_dominance:{dominance.ToString("0.000", CultureInfo.InvariantCulture)}",
                $"source_diversity_distinct_sources:{sourceGroups.Count}",
                $"source_diversity_distinct_collections:{distinctCollections.Length}",
                $"source_diversity_guard:{guardState}",
                $"source_diversity_alternative_collections:{(distinctCollections.Length == 0 ? "none" : string.Join(",", distinctCollections))}"
            });
    }

    private static string ResolveSourceKey(KnowledgeChunk chunk)
    {
        var raw = chunk.Metadata.GetValueOrDefault("source_diversity_source_key")
                  ?? chunk.Metadata.GetValueOrDefault("source_url")
                  ?? chunk.Metadata.GetValueOrDefault("source_path")
                  ?? chunk.Metadata.GetValueOrDefault("document_id")
                  ?? chunk.Metadata.GetValueOrDefault("title")
                  ?? chunk.Id;

        if (Uri.TryCreate(raw, UriKind.Absolute, out var uri))
        {
            return $"{uri.Host.ToLowerInvariant()}{uri.AbsolutePath.TrimEnd('/').ToLowerInvariant()}";
        }

        return raw.Trim().ToLowerInvariant();
    }
}

