using Helper.Runtime.WebResearch.Fetching;
using Helper.Runtime.WebResearch.Normalization;
using Helper.Runtime.WebResearch.Quality;
using Helper.Runtime.WebResearch.Ranking;
using Helper.Runtime.WebResearch.Rendering;
using Helper.Runtime.WebResearch.Safety;

namespace Helper.Runtime.WebResearch;

internal interface IWebSearchDocumentPipeline
{
    bool CanExpandFetchSelection { get; }

    IReadOnlyList<WebSearchDocument> Normalize(
        WebSearchRequest request,
        WebSearchPlan plan,
        IReadOnlyList<WebSearchDocument> documents,
        int maxResults,
        bool allowFetchExpansion,
        List<string> trace);

    Task<WebSearchPostFetchResult> EnrichAndFinalizeAsync(
        WebSearchRequest request,
        WebSearchPlan plan,
        IReadOnlyList<WebSearchDocument> documents,
        bool usedDeterministicFallback,
        CancellationToken ct);
}

internal sealed record WebSearchPostFetchResult(
    IReadOnlyList<WebSearchDocument> Documents,
    IReadOnlyList<string> Trace,
    bool UpgradedToDirectPageFetch);

internal sealed class WebSearchDocumentPipeline : IWebSearchDocumentPipeline
{
    private readonly IWebPageFetcher _pageFetcher;
    private readonly IWebSearchCandidateNormalizer _candidateNormalizer;
    private readonly IWebSearchFetchEnricher _fetchEnricher;
    private readonly IDuplicateContentCollapsePolicy _duplicateCollapsePolicy;
    private readonly IEventClusterBuilder _eventClusterBuilder;
    private readonly IPostFetchSelectionPolicy _postFetchSelectionPolicy;

    internal WebSearchDocumentPipeline(
        IWebPageFetcher pageFetcher,
        IWebSearchCandidateNormalizer candidateNormalizer,
        IWebSearchFetchEnricher fetchEnricher,
        IDuplicateContentCollapsePolicy duplicateCollapsePolicy,
        IEventClusterBuilder eventClusterBuilder,
        IPostFetchSelectionPolicy postFetchSelectionPolicy)
    {
        _pageFetcher = pageFetcher;
        _candidateNormalizer = candidateNormalizer;
        _fetchEnricher = fetchEnricher;
        _duplicateCollapsePolicy = duplicateCollapsePolicy;
        _eventClusterBuilder = eventClusterBuilder;
        _postFetchSelectionPolicy = postFetchSelectionPolicy;
    }

    public bool CanExpandFetchSelection => !ReferenceEquals(_pageFetcher, NoopWebPageFetcher.Instance);

    public IReadOnlyList<WebSearchDocument> Normalize(
        WebSearchRequest request,
        WebSearchPlan plan,
        IReadOnlyList<WebSearchDocument> documents,
        int maxResults,
        bool allowFetchExpansion,
        List<string> trace)
    {
        return _candidateNormalizer.Normalize(
            request,
            plan,
            documents,
            maxResults,
            allowFetchExpansion,
            trace);
    }

    public async Task<WebSearchPostFetchResult> EnrichAndFinalizeAsync(
        WebSearchRequest request,
        WebSearchPlan plan,
        IReadOnlyList<WebSearchDocument> documents,
        bool usedDeterministicFallback,
        CancellationToken ct)
    {
        var enrichment = await _fetchEnricher.EnrichAsync(request, plan, documents, ct).ConfigureAwait(false);
        var aggregateDocuments = enrichment.Documents.ToList();
        var pageTrace = enrichment.Trace.ToList();
        var postFetchNormalizationTrace = new List<string>();
        aggregateDocuments = _candidateNormalizer.Canonicalize(aggregateDocuments, "post_fetch", postFetchNormalizationTrace).ToList();
        var collapsed = _duplicateCollapsePolicy.Collapse(aggregateDocuments, "post_fetch");
        aggregateDocuments = collapsed.Documents.ToList();
        pageTrace.AddRange(postFetchNormalizationTrace);
        pageTrace.AddRange(collapsed.Trace);
        var clusters = _eventClusterBuilder.Build(aggregateDocuments);
        pageTrace.AddRange(clusters.Trace);
        aggregateDocuments = _postFetchSelectionPolicy.SelectFinalDocuments(request, plan, aggregateDocuments, pageTrace).ToList();

        return new WebSearchPostFetchResult(
            aggregateDocuments,
            pageTrace,
            usedDeterministicFallback && aggregateDocuments.Any(static document => document.ExtractedPage is not null));
    }



}

