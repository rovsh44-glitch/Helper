using System.Globalization;
using Helper.Runtime.Core;
using Helper.Runtime.Infrastructure;

namespace Helper.Runtime.Knowledge.Retrieval;

public sealed class ContextAssemblyService : IRetrievalContextAssembler
{
    private readonly AILink _ai;
    private readonly IRerankingService _reranker;
    private readonly RetrievalCollectionRouter _router;
    private readonly RetrievalContextExpansionService _expander;
    private readonly HybridRetrievalCandidateCollector _candidateCollector;

    public ContextAssemblyService(IVectorStore store, IStructuredVectorStore structuredStore, AILink ai, IRerankingService reranker)
    {
        _ai = ai;
        _reranker = reranker;

        var profileStore = new RetrievalCollectionProfileStore(store, structuredStore);
        _router = new RetrievalCollectionRouter(structuredStore, profileStore);
        _expander = new RetrievalContextExpansionService(structuredStore);
        _candidateCollector = new HybridRetrievalCandidateCollector(store, structuredStore);
    }

    internal static void ResetCollectionProfilesForTesting() => RetrievalCollectionProfileStore.ResetForTesting();

    public async Task<IReadOnlyList<KnowledgeChunk>> AssembleAsync(
        string query,
        string? domain = null,
        int limit = 5,
        string pipelineVersion = "v2",
        bool expandContext = true,
        CancellationToken ct = default,
        RetrievalRequestOptions? options = null)
    {
        var normalizedPipelineVersion = KnowledgeCollectionNaming.NormalizePipelineVersion(pipelineVersion);
        var collectionRoutes = await _router.ResolveAsync(query, domain, normalizedPipelineVersion, ct).ConfigureAwait(false);
        if (collectionRoutes.Count == 0)
        {
            return Array.Empty<KnowledgeChunk>();
        }

        var queryEmbedding = await _ai.EmbedAsync(query, ct).ConfigureAwait(false);
        var candidates = await _candidateCollector
            .CollectAsync(query, queryEmbedding, collectionRoutes, limit, options, ct)
            .ConfigureAwait(false);
        if (candidates.Count == 0)
        {
            return Array.Empty<KnowledgeChunk>();
        }

        var effectiveExpandContext = options?.Purpose != RetrievalPurpose.ReasoningSupport && expandContext;
        if (!effectiveExpandContext)
        {
            return _reranker.Rerank(query, candidates, limit, options);
        }

        var initial = _reranker.Rerank(query, candidates, Math.Max(limit * 2, 8), options);
        var expanded = new Dictionary<string, KnowledgeChunk>(StringComparer.OrdinalIgnoreCase);
        foreach (var chunk in initial)
        {
            expanded[chunk.Id] = chunk;
            await _expander.ExpandAsync(expanded, chunk, ct).ConfigureAwait(false);
        }

        return _reranker.Rerank(query, expanded.Values, limit, options);
    }
}

