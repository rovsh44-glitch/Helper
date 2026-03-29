using Helper.Runtime.Core;

namespace Helper.Runtime.Knowledge.Retrieval;

internal sealed class HybridRetrievalCandidateCollector
{
    private readonly IVectorStore _store;
    private readonly IStructuredVectorStore _structuredStore;

    public HybridRetrievalCandidateCollector(IVectorStore store, IStructuredVectorStore structuredStore)
    {
        _store = store;
        _structuredStore = structuredStore;
    }

    public async Task<IReadOnlyList<KnowledgeChunk>> CollectAsync(
        string query,
        float[] queryEmbedding,
        IReadOnlyList<CollectionRoute> routes,
        int limit,
        RetrievalRequestOptions? options,
        CancellationToken ct)
    {
        var vectorCandidates = new List<KnowledgeChunk>();
        var sparseCandidates = new List<KnowledgeChunk>();
        var totalCollections = Math.Max(routes.Count, 1);

        foreach (var route in routes)
        {
            var pointCount = await _structuredStore.GetCollectionPointCountAsync(route.Collection, ct).ConfigureAwait(false);

            var vectorWindow = RetrievalCollectionRoutingPolicy.ResolveCandidateWindow(limit, totalCollections, pointCount);
            vectorWindow = RetrievalCollectionRoutingPolicy.ApplyRoutingWindow(vectorWindow, route);
            var vectorResults = await _store.SearchAsync(queryEmbedding, route.Collection, vectorWindow, ct).ConfigureAwait(false)
                ?? new List<KnowledgeChunk>();
            RetrievalRoutingMetadataSupport.Append(vectorResults, route);
            vectorCandidates.AddRange(vectorResults.DistinctBy(static chunk => chunk.Id, StringComparer.OrdinalIgnoreCase));

            var sparseWindow = LocalSparseRetrievalPolicy.ResolveWindow(limit, route.Rank, pointCount);
            var sparseSeed = await _store.ScrollMetadataAsync(route.Collection, sparseWindow, null, ct).ConfigureAwait(false)
                ?? new List<KnowledgeChunk>();
            RetrievalRoutingMetadataSupport.Append(sparseSeed, route);
            var rankedSparse = LocalSparseRetrievalPolicy.Rank(query, sparseSeed, Math.Max(limit * 3, 8), options);
            sparseCandidates.AddRange(rankedSparse);
        }

        var fusionLimit = Math.Max(limit * 6, 16);
        return ReciprocalRankFusionPolicy.Fuse(
            vectorCandidates,
            sparseCandidates,
            fusionLimit);
    }
}

