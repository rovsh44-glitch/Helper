using System.Globalization;
using Helper.Runtime.Core;

namespace Helper.Runtime.Knowledge.Retrieval;

internal static class LocalSparseRetrievalPolicy
{
    private const int MinimumSparseWindow = 24;
    private const int MaximumSparseWindow = 72;

    public static int ResolveWindow(int limit, int routeRank, int? pointCount)
    {
        var normalizedLimit = Math.Max(limit, 1);
        var baseWindow = pointCount.GetValueOrDefault() switch
        {
            >= 40000 => 56,
            >= 12000 => 44,
            >= 2000 => 32,
            > 0 => 24,
            _ => 24
        };

        baseWindow = Math.Max(baseWindow, normalizedLimit * 4);
        if (routeRank <= 1)
        {
            baseWindow = (int)Math.Ceiling(baseWindow * 1.35d);
        }
        else if (routeRank <= 3)
        {
            baseWindow = (int)Math.Ceiling(baseWindow * 1.15d);
        }

        return Math.Clamp(baseWindow, MinimumSparseWindow, MaximumSparseWindow);
    }

    public static IReadOnlyList<KnowledgeChunk> Rank(
        string query,
        IEnumerable<KnowledgeChunk> candidates,
        int limit,
        RetrievalRequestOptions? options)
    {
        if (limit <= 0)
        {
            return Array.Empty<KnowledgeChunk>();
        }

        var preparedQuery = RerankingPolicy.PreparedQuery.Create(query);
        var ranked = candidates
            .Select(candidate => (Chunk: candidate, Score: RerankingCandidateScorer.Score(preparedQuery, candidate, options)))
            .Where(static item => item.Score > 0.45d)
            .OrderByDescending(static item => item.Score)
            .ThenBy(item => item.Chunk.Metadata.GetValueOrDefault("title", string.Empty), StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => item.Chunk.Id, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .ToList();

        for (var index = 0; index < ranked.Count; index++)
        {
            ranked[index].Chunk.Metadata["sparse_retrieval_rank"] = (index + 1).ToString(CultureInfo.InvariantCulture);
            ranked[index].Chunk.Metadata["sparse_retrieval_score"] = ranked[index].Score.ToString("0.000", CultureInfo.InvariantCulture);
        }

        return ranked.Select(static item => item.Chunk).ToList();
    }
}

