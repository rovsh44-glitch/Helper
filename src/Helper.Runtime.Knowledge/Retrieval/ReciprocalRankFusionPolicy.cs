using System.Globalization;
using Helper.Runtime.Core;

namespace Helper.Runtime.Knowledge.Retrieval;

internal static class ReciprocalRankFusionPolicy
{
    private const double FusionConstant = 60d;

    public static IReadOnlyList<KnowledgeChunk> Fuse(
        IReadOnlyList<KnowledgeChunk> vectorCandidates,
        IReadOnlyList<KnowledgeChunk> sparseCandidates,
        int limit)
    {
        if (limit <= 0)
        {
            return Array.Empty<KnowledgeChunk>();
        }

        var fused = new Dictionary<string, FusedCandidate>(StringComparer.OrdinalIgnoreCase);
        Add(vectorCandidates, "vector");
        Add(sparseCandidates, "sparse");

        return fused.Values
            .OrderByDescending(static item => item.Score)
            .ThenBy(item => item.Chunk.Metadata.GetValueOrDefault("title", string.Empty), StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => item.Chunk.Id, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .Select(static item =>
            {
                Annotate(item);
                return item.Chunk;
            })
            .ToList();

        void Add(IReadOnlyList<KnowledgeChunk> candidates, string channel)
        {
            for (var index = 0; index < candidates.Count; index++)
            {
                var candidate = candidates[index];
                var key = candidate.Id;
                if (!fused.TryGetValue(key, out var state))
                {
                    state = new FusedCandidate(candidate);
                    fused[key] = state;
                }
                else
                {
                    MergeMetadata(state.Chunk.Metadata, candidate.Metadata);
                }

                var rank = index + 1;
                state.Score += 1d / (FusionConstant + rank);
                if (string.Equals(channel, "vector", StringComparison.Ordinal))
                {
                    state.VectorRank = rank;
                }
                else
                {
                    state.SparseRank = rank;
                }
            }
        }
    }

    private static void Annotate(FusedCandidate candidate)
    {
        candidate.Chunk.Metadata["hybrid_rrf_score"] = candidate.Score.ToString("0.000000", CultureInfo.InvariantCulture);
        candidate.Chunk.Metadata["hybrid_vector_rank"] = candidate.VectorRank?.ToString(CultureInfo.InvariantCulture) ?? "none";
        candidate.Chunk.Metadata["hybrid_sparse_rank"] = candidate.SparseRank?.ToString(CultureInfo.InvariantCulture) ?? "none";
        candidate.Chunk.Metadata["hybrid_rrf_active"] = "true";
        candidate.Chunk.Metadata["retrieval_channel"] = ResolveChannel(candidate);
    }

    private static string ResolveChannel(FusedCandidate candidate)
    {
        if (candidate.VectorRank is not null && candidate.SparseRank is not null)
        {
            return "hybrid";
        }

        return candidate.VectorRank is not null ? "vector" : "sparse";
    }

    private static void MergeMetadata(Dictionary<string, string> target, Dictionary<string, string> source)
    {
        foreach (var pair in source)
        {
            if (!target.ContainsKey(pair.Key))
            {
                target[pair.Key] = pair.Value;
            }
        }
    }

    private sealed class FusedCandidate
    {
        public FusedCandidate(KnowledgeChunk chunk)
        {
            Chunk = chunk;
        }

        public KnowledgeChunk Chunk { get; }

        public double Score { get; set; }

        public int? VectorRank { get; set; }

        public int? SparseRank { get; set; }
    }
}

