using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Helper.Runtime.Core;

public enum RetrievalPurpose
{
    Standard,
    FactualLookup,
    ReasoningSupport
}

public sealed record RetrievalRequestOptions(
    RetrievalPurpose Purpose = RetrievalPurpose.Standard,
    IReadOnlyList<string>? PreferredDomains = null,
    IReadOnlyList<string>? DisallowedDomains = null,
    bool PreferTraceableChunks = false);

public interface IRetrievalContextAssembler
{
    Task<IReadOnlyList<KnowledgeChunk>> AssembleAsync(
        string query,
        string? domain = null,
        int limit = 5,
        string pipelineVersion = "v2",
        bool expandContext = true,
        CancellationToken ct = default,
        RetrievalRequestOptions? options = null);
}

public interface IRerankingService
{
    IReadOnlyList<KnowledgeChunk> Rerank(
        string query,
        IEnumerable<KnowledgeChunk> candidates,
        int limit = 5,
        RetrievalRequestOptions? options = null);
}

