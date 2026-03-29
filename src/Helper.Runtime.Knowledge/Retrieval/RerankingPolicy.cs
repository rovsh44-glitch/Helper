using Helper.Runtime.Core;
using Helper.Runtime.Knowledge;

namespace Helper.Runtime.Knowledge.Retrieval;

internal static class RerankingPolicy
{
    public static IReadOnlyList<KnowledgeChunk> Rerank(string query, IEnumerable<KnowledgeChunk> candidates, int limit = 5, RetrievalRequestOptions? options = null)
    {
        if (limit <= 0)
        {
            return Array.Empty<KnowledgeChunk>();
        }

        var preparedQuery = PreparedQuery.Create(query);
        var scored = candidates
            .Select((candidate, index) => new ScoredCandidate(candidate, Score(preparedQuery, candidate, options), index))
            .OrderByDescending(static item => item.Score)
            .ThenBy(item => item.Chunk.Metadata.GetValueOrDefault("title", string.Empty), StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => item.OriginalOrder)
            .ToList();

        return SelectBalancedTop(scored, limit, options);
    }

    private static double Score(PreparedQuery query, KnowledgeChunk candidate, RetrievalRequestOptions? options)
        => RerankingCandidateScorer.Score(query, candidate, options);

    private static MatchMetrics MatchField(PreparedQuery query, FieldFeatures field)
        => RerankingQueryModel.MatchField(query, field);

    private static IReadOnlyList<KnowledgeChunk> SelectBalancedTop(IReadOnlyList<ScoredCandidate> scored, int limit, RetrievalRequestOptions? options)
        => MemoryLayerReranker.SelectTop(scored, limit, options);

    private static double ComputeDocumentScore(IEnumerable<ScoredCandidate> group)
        => RerankingSelectionPolicy.ComputeDocumentScore(group);

    private static string GetDocumentKey(KnowledgeChunk chunk)
        => RerankingSelectionPolicy.GetDocumentKey(chunk);

    private static string NormalizeText(string? text)
        => RerankingQueryModel.NormalizeText(text);

    private static List<string> Tokenize(string text)
        => RerankingQueryModel.Tokenize(text);

    private static string BuildRoot(string token)
        => RerankingQueryModel.BuildRoot(token);

    private static int CountIntentMatches(PreparedQuery query, HashSet<string> intentRoots)
        => RerankingQueryModel.CountIntentMatches(query, intentRoots);

    private static IReadOnlyList<string> BuildPhrases(IReadOnlyList<QueryTerm> terms)
        => RerankingQueryModel.BuildPhrases(terms);

    internal sealed record PreparedQuery(string NormalizedText, IReadOnlyList<QueryTerm> Terms, IReadOnlyList<string> Phrases)
    {
        public static PreparedQuery Create(string query)
            => RerankingQueryModel.CreatePreparedQuery(query);
    }

    internal sealed record QueryTerm(string Token, double Weight, IReadOnlyList<string> Roots);
    internal sealed record FieldFeatures(string NormalizedText, HashSet<string> Tokens, HashSet<string> Roots)
    {
        public static FieldFeatures Empty { get; } = new(string.Empty, new HashSet<string>(StringComparer.OrdinalIgnoreCase), new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        public bool IsEmpty => string.IsNullOrWhiteSpace(NormalizedText);

        public static FieldFeatures Create(string? text)
            => RerankingQueryModel.CreateFieldFeatures(text);
    }

    internal readonly record struct MatchMetrics(double ExactScore, double FuzzyScore, double Coverage, int PhraseHits)
    {
        public static MatchMetrics Empty => new(0d, 0d, 0d, 0);
    }

    internal sealed record ScoredCandidate(KnowledgeChunk Chunk, double Score, int OriginalOrder);
    internal sealed record DocumentBucket(string DocumentKey, IReadOnlyList<ScoredCandidate> Items, double Score, int OriginalOrder);
}

