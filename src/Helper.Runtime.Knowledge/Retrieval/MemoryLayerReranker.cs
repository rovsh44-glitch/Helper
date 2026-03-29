using Helper.Runtime.Core;
using Helper.Runtime.Knowledge;

namespace Helper.Runtime.Knowledge.Retrieval;

internal static class MemoryLayerReranker
{
    public static IReadOnlyList<KnowledgeChunk> SelectTop(
        IReadOnlyList<RerankingPolicy.ScoredCandidate> scored,
        int limit,
        RetrievalRequestOptions? options)
    {
        IReadOnlyList<KnowledgeChunk> selected;
        if (options is null || options.Purpose == RetrievalPurpose.Standard)
        {
            selected = RerankingSelectionPolicy.SelectBalancedTop(scored, limit);
            return RetrievalSourceDiversityPolicy.Apply(scored, selected, limit, options);
        }

        var shortlistLimit = Math.Min(scored.Count, Math.Max(limit * 2, 8));
        var shortlist = RerankingSelectionPolicy.SelectBalancedTop(scored, shortlistLimit);
        var scoreById = scored.ToDictionary(item => item.Chunk.Id, item => item.Score, StringComparer.OrdinalIgnoreCase);

        selected = shortlist
            .Select(chunk => new
            {
                Chunk = chunk,
                BaseScore = scoreById.GetValueOrDefault(chunk.Id),
                Priority = ComputePriority(chunk, options)
            })
            .OrderByDescending(item => item.Priority)
            .ThenByDescending(item => item.BaseScore)
            .ThenBy(item => item.Chunk.Metadata.GetValueOrDefault("title", string.Empty), StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .Select(item => item.Chunk)
            .ToArray();

        return RetrievalSourceDiversityPolicy.Apply(scored, selected, limit, options);
    }

    private static double ComputePriority(KnowledgeChunk chunk, RetrievalRequestOptions options)
    {
        var priority = 0d;
        var domain = chunk.Metadata.GetValueOrDefault("domain");
        var chunkRole = chunk.Metadata.GetValueOrDefault("chunk_role");

        if (options.PreferTraceableChunks && ReasoningRetrievalPolicy.HasTraceability(chunk))
        {
            priority += 2.0;
        }

        if (ReasoningRetrievalPolicy.MatchesDomain(options.PreferredDomains, domain))
        {
            priority += 1.0;
        }

        if (ReasoningRetrievalPolicy.MatchesDomain(options.DisallowedDomains, domain))
        {
            priority -= 1.6;
        }

        if (options.Purpose == RetrievalPurpose.ReasoningSupport &&
            ReasoningRetrievalPolicy.IsGenericReferenceDomain(domain) &&
            !ReasoningRetrievalPolicy.HasStrongRouting(chunk))
        {
            priority -= 1.15;
        }

        priority += chunkRole switch
        {
            "standalone" => 0.35,
            "parent" => 0.2,
            "child" => -0.15,
            _ => 0
        };

        return priority;
    }
}

