using Helper.Runtime.Core;
using Helper.Runtime.Knowledge;

namespace Helper.Runtime.Knowledge.Retrieval;

public sealed class RerankingService : IRerankingService
{
    public IReadOnlyList<KnowledgeChunk> Rerank(string query, IEnumerable<KnowledgeChunk> candidates, int limit = 5, RetrievalRequestOptions? options = null)
    {
        return RerankingPolicy.Rerank(query, candidates, limit, options);
    }
}

