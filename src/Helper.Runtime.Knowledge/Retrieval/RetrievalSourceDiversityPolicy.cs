using System.Globalization;
using Helper.Runtime.Core;

namespace Helper.Runtime.Knowledge.Retrieval;

internal readonly record struct RetrievalSourceDiversityAssessment(
    double Dominance,
    int DistinctSources,
    int DistinctCollections,
    string? DominantSourceKey,
    IReadOnlyList<string> AlternativeCollections)
{
    public bool IsMeaningfullyBetterThan(RetrievalSourceDiversityAssessment other)
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

internal static class RetrievalSourceDiversityPolicy
{
    public static IReadOnlyList<KnowledgeChunk> Apply(
        IReadOnlyList<RerankingPolicy.ScoredCandidate> scored,
        IReadOnlyList<KnowledgeChunk> baseline,
        int limit,
        RetrievalRequestOptions? options)
    {
        if (limit <= 1 || baseline.Count <= 1)
        {
            return Annotate(baseline, Assess(baseline), guardApplied: false);
        }

        var dominanceThreshold = ResolveDominanceThreshold(options);
        var baselineAssessment = Assess(baseline);
        if (!ShouldDiversify(scored, baseline, baselineAssessment, dominanceThreshold))
        {
            return Annotate(baseline, baselineAssessment, guardApplied: false);
        }

        var diversified = BuildDiversifiedSelection(scored, baseline, limit, options);
        var diversifiedAssessment = Assess(diversified);
        if (!diversifiedAssessment.IsMeaningfullyBetterThan(baselineAssessment))
        {
            return Annotate(baseline, baselineAssessment, guardApplied: false);
        }

        return Annotate(diversified, diversifiedAssessment, guardApplied: true);
    }

    private static bool ShouldDiversify(
        IReadOnlyList<RerankingPolicy.ScoredCandidate> scored,
        IReadOnlyList<KnowledgeChunk> baseline,
        RetrievalSourceDiversityAssessment baselineAssessment,
        double dominanceThreshold)
    {
        if (baselineAssessment.Dominance <= dominanceThreshold || string.IsNullOrWhiteSpace(baselineAssessment.DominantSourceKey))
        {
            return false;
        }

        var scoreById = scored.ToDictionary(item => item.Chunk.Id, item => item.Score, StringComparer.OrdinalIgnoreCase);
        var baselineMinScore = baseline
            .Select(chunk => scoreById.GetValueOrDefault(chunk.Id))
            .DefaultIfEmpty(0d)
            .Min();
        var viableFloor = baselineMinScore - 0.75d;
        var baselineCollections = baseline
            .Select(GetCollectionKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var window = scored.Take(Math.Min(scored.Count, Math.Max(baseline.Count * 4, 12)));

        return window.Any(item =>
        {
            if (item.Score < viableFloor)
            {
                return false;
            }

            var sourceKey = GetSourceKey(item.Chunk);
            if (string.Equals(sourceKey, baselineAssessment.DominantSourceKey, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return !baselineCollections.Contains(GetCollectionKey(item.Chunk)) || baselineAssessment.DistinctCollections <= 1;
        });
    }

    private static IReadOnlyList<KnowledgeChunk> BuildDiversifiedSelection(
        IReadOnlyList<RerankingPolicy.ScoredCandidate> scored,
        IReadOnlyList<KnowledgeChunk> baseline,
        int limit,
        RetrievalRequestOptions? options)
    {
        var scoreById = scored.ToDictionary(item => item.Chunk.Id, item => item.Score, StringComparer.OrdinalIgnoreCase);
        var baselineMinScore = baseline
            .Select(chunk => scoreById.GetValueOrDefault(chunk.Id))
            .DefaultIfEmpty(0d)
            .Min();
        var viableFloor = baselineMinScore - 0.75d;
        var selected = new List<KnowledgeChunk>(Math.Min(limit, scored.Count));
        var selectedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sourceCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var seenCollections = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var targetDistinctSources = Math.Min(limit, Math.Max(2, (limit + 1) / 2));
        var maxPerSource = limit <= 3 ? 1 : 2;

        Add(scored[0].Chunk);

        foreach (var item in scored)
        {
            if (selected.Count >= limit)
            {
                break;
            }

            var sourceKey = GetSourceKey(item.Chunk);
            var collectionKey = GetCollectionKey(item.Chunk);
            if (selectedIds.Contains(item.Chunk.Id) ||
                item.Score < viableFloor ||
                sourceCounts.ContainsKey(sourceKey) ||
                seenCollections.Contains(collectionKey))
            {
                continue;
            }

            Add(item.Chunk);
        }

        foreach (var item in scored)
        {
            if (selected.Count >= limit)
            {
                break;
            }

            var sourceKey = GetSourceKey(item.Chunk);
            if (selectedIds.Contains(item.Chunk.Id) ||
                item.Score < viableFloor ||
                sourceCounts.ContainsKey(sourceKey) ||
                sourceCounts.Count >= targetDistinctSources)
            {
                continue;
            }

            Add(item.Chunk);
        }

        foreach (var item in scored)
        {
            if (selected.Count >= limit)
            {
                break;
            }

            var sourceKey = GetSourceKey(item.Chunk);
            if (selectedIds.Contains(item.Chunk.Id) || sourceCounts.GetValueOrDefault(sourceKey) >= maxPerSource)
            {
                continue;
            }

            Add(item.Chunk);
        }

        foreach (var item in scored)
        {
            if (selected.Count >= limit)
            {
                break;
            }

            if (selectedIds.Contains(item.Chunk.Id))
            {
                continue;
            }

            Add(item.Chunk);
        }

        return selected;

        void Add(KnowledgeChunk chunk)
        {
            var sourceKey = GetSourceKey(chunk);
            selected.Add(chunk);
            selectedIds.Add(chunk.Id);
            sourceCounts[sourceKey] = sourceCounts.TryGetValue(sourceKey, out var current) ? current + 1 : 1;
            seenCollections.Add(GetCollectionKey(chunk));
        }
    }

    private static IReadOnlyList<KnowledgeChunk> Annotate(
        IReadOnlyList<KnowledgeChunk> chunks,
        RetrievalSourceDiversityAssessment assessment,
        bool guardApplied)
    {
        var dominance = assessment.Dominance.ToString("0.000", CultureInfo.InvariantCulture);
        var alternativeCollections = assessment.AlternativeCollections.Count == 0
            ? "none"
            : string.Join(",", assessment.AlternativeCollections);

        foreach (var chunk in chunks)
        {
            chunk.Metadata["source_diversity_source_key"] = GetSourceKey(chunk);
            chunk.Metadata["source_diversity_dominance"] = dominance;
            chunk.Metadata["source_diversity_distinct_sources"] = assessment.DistinctSources.ToString(CultureInfo.InvariantCulture);
            chunk.Metadata["source_diversity_distinct_collections"] = assessment.DistinctCollections.ToString(CultureInfo.InvariantCulture);
            chunk.Metadata["source_diversity_guard_applied"] = guardApplied ? "true" : "false";
            chunk.Metadata["source_diversity_alternative_collections"] = alternativeCollections;
        }

        return chunks;
    }

    private static RetrievalSourceDiversityAssessment Assess(IReadOnlyList<KnowledgeChunk> chunks)
    {
        if (chunks.Count == 0)
        {
            return new RetrievalSourceDiversityAssessment(0d, 0, 0, null, Array.Empty<string>());
        }

        var sourceCounts = chunks
            .GroupBy(GetSourceKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => new { SourceKey = group.Key, Count = group.Count() })
            .OrderByDescending(static group => group.Count)
            .ThenBy(static group => group.SourceKey, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var collections = chunks
            .Select(GetCollectionKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var dominantSource = sourceCounts[0];

        return new RetrievalSourceDiversityAssessment(
            Dominance: dominantSource.Count / (double)chunks.Count,
            DistinctSources: sourceCounts.Count,
            DistinctCollections: collections.Length,
            DominantSourceKey: dominantSource.SourceKey,
            AlternativeCollections: collections);
    }

    private static double ResolveDominanceThreshold(RetrievalRequestOptions? options)
    {
        return options?.Purpose switch
        {
            RetrievalPurpose.ReasoningSupport => 0.55d,
            RetrievalPurpose.FactualLookup => 0.60d,
            _ => 0.72d
        };
    }

    private static string GetCollectionKey(KnowledgeChunk chunk)
        => chunk.Metadata.GetValueOrDefault("collection", chunk.Collection);

    private static string GetSourceKey(KnowledgeChunk chunk)
    {
        var raw = chunk.Metadata.GetValueOrDefault("source_url")
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

