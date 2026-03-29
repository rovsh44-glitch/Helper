using Helper.Runtime.WebResearch.Fetching;
using Helper.Runtime.WebResearch.Quality;
using Helper.Runtime.WebResearch.Rendering;
using Helper.Runtime.WebResearch.Safety;

namespace Helper.Runtime.WebResearch;

internal interface IWebSearchFetchEnricher
{
    Task<WebSearchFetchEnrichmentResult> EnrichAsync(
        WebSearchRequest request,
        WebSearchPlan plan,
        IReadOnlyList<WebSearchDocument> documents,
        CancellationToken ct);
}

internal sealed record WebSearchFetchEnrichmentResult(
    IReadOnlyList<WebSearchDocument> Documents,
    IReadOnlyList<string> Trace);

internal sealed class WebSearchFetchEnricher : IWebSearchFetchEnricher
{
    private readonly IWebPageFetcher _pageFetcher;
    private readonly IEvidenceBoundaryProjector _evidenceBoundaryProjector;
    private readonly IRenderedPageBudgetPolicy _renderedPageBudgetPolicy;
    private readonly IWebDocumentQualityPolicy _documentQualityPolicy;
    private readonly IFetchStabilityPolicy _fetchStabilityPolicy;
    private readonly IWebSearchFetchDiagnosticsSummarizer _diagnosticsSummarizer;

    public WebSearchFetchEnricher(
        IWebPageFetcher pageFetcher,
        IEvidenceBoundaryProjector evidenceBoundaryProjector,
        IRenderedPageBudgetPolicy renderedPageBudgetPolicy,
        IWebDocumentQualityPolicy documentQualityPolicy,
        IFetchStabilityPolicy fetchStabilityPolicy,
        IWebSearchFetchDiagnosticsSummarizer diagnosticsSummarizer)
    {
        _pageFetcher = pageFetcher;
        _evidenceBoundaryProjector = evidenceBoundaryProjector;
        _renderedPageBudgetPolicy = renderedPageBudgetPolicy;
        _documentQualityPolicy = documentQualityPolicy;
        _fetchStabilityPolicy = fetchStabilityPolicy;
        _diagnosticsSummarizer = diagnosticsSummarizer;
    }

    public async Task<WebSearchFetchEnrichmentResult> EnrichAsync(
        WebSearchRequest request,
        WebSearchPlan plan,
        IReadOnlyList<WebSearchDocument> documents,
        CancellationToken ct)
    {
        if (documents.Count == 0)
        {
            return new WebSearchFetchEnrichmentResult(Array.Empty<WebSearchDocument>(), Array.Empty<string>());
        }

        if (ReferenceEquals(_pageFetcher, NoopWebPageFetcher.Instance))
        {
            return new WebSearchFetchEnrichmentResult(
                documents,
                new[] { "web_page_fetch.disabled coordinator_mode=noop" });
        }

        var stability = _fetchStabilityPolicy.Resolve(request.Query, plan, documents.Count, request.MaxResults);
        var fetchBudget = Math.Min(stability.AttemptBudget, documents.Count);
        var renderBudgetRemaining = _renderedPageBudgetPolicy.ResolvePerSearchBudget(fetchBudget);
        var trace = new List<string>
        {
            $"web_page_fetch.budget={fetchBudget}",
            $"web_page_fetch.success_target={stability.SuccessTarget}",
            $"browser_render.session_budget={renderBudgetRemaining}"
        };
        trace.AddRange(stability.Trace);
        var enrichedDocuments = new List<WebSearchDocument>(documents.Count);
        var successfulFetchCount = 0;
        var attemptedFetchCount = 0;
        var fetchDiagnostics = new List<WebSearchFetchDiagnosticEntry>();
        var baseFetchBudget = Math.Min(WebPageFetchSettings.ReadMaxFetchesPerSearch(), documents.Count);
        if (stability.BackfillEnabled && (fetchBudget > baseFetchBudget || documents.Count > request.MaxResults))
        {
            var reason = fetchBudget > baseFetchBudget
                ? "expanded_attempt_budget"
                : "expanded_candidate_pool";
            trace.Add($"web_page_fetch.backfill_triggered reason={reason} base_budget={baseFetchBudget} attempt_budget={fetchBudget} candidate_count={documents.Count} success_target={stability.SuccessTarget}");
        }

        for (var index = 0; index < documents.Count; index++)
        {
            var document = documents[index];
            if (index >= fetchBudget || !WebSearchDocumentPipelineSupport.IsHttpUrl(document.Url))
            {
                enrichedDocuments.Add(document);
                continue;
            }

            var fetchResult = await _pageFetcher.FetchAsync(
                document.Url,
                new WebPageFetchContext(
                    FetchOrdinal: index + 1,
                    FetchBudget: fetchBudget,
                    AllowBrowserRenderFallback: renderBudgetRemaining > 0,
                    RenderBudgetRemaining: renderBudgetRemaining),
                ct).ConfigureAwait(false);
            attemptedFetchCount++;
            trace.AddRange(fetchResult.Trace.Select(message => $"{message} document={index + 1}"));
            if (fetchResult.UsedBrowserRenderFallback && renderBudgetRemaining > 0)
            {
                renderBudgetRemaining--;
            }

            fetchDiagnostics.Add(_diagnosticsSummarizer.CreateEntry(document, fetchResult));

            if (!fetchResult.Success || fetchResult.ExtractedPage is null)
            {
                enrichedDocuments.Add(document);
                continue;
            }

            var projection = _evidenceBoundaryProjector.Project(fetchResult.ExtractedPage);
            trace.AddRange(projection.Trace.Select(message => $"{message} document={index + 1}"));

            var extractedPage = projection.Page;
            var normalizedUrl = WebSearchDocumentPipelineSupport.IsHttpUrl(extractedPage.CanonicalUrl)
                ? extractedPage.CanonicalUrl
                : fetchResult.ResolvedUrl ?? document.Url;
            var snippet = WebSearchDocumentPipelineSupport.BuildSnippetFromPage(extractedPage, document.Snippet);
            var enrichedDocument = document with
            {
                Url = normalizedUrl,
                Title = string.IsNullOrWhiteSpace(extractedPage.Title) ? document.Title : extractedPage.Title,
                Snippet = snippet,
                IsFallback = false,
                ExtractedPage = extractedPage
            };
            var quality = _documentQualityPolicy.Evaluate(enrichedDocument, "page", request.Query);
            trace.AddRange(quality.Trace.Select(message => $"{message} document={index + 1}"));
            if (!quality.Allowed)
            {
                enrichedDocuments.Add(document);
                continue;
            }

            enrichedDocuments.Add(enrichedDocument);
            successfulFetchCount++;
            if (stability.BackfillEnabled && successfulFetchCount >= stability.SuccessTarget)
            {
                if (index + 1 < documents.Count)
                {
                    trace.Add($"web_page_fetch.success_target_reached count={successfulFetchCount} attempts={index + 1}");
                }

                for (var remaining = index + 1; remaining < documents.Count; remaining++)
                {
                    enrichedDocuments.Add(documents[remaining]);
                }

                break;
            }
        }

        if (documents.Count > fetchBudget)
        {
            if (successfulFetchCount < stability.SuccessTarget && stability.BackfillEnabled)
            {
                trace.Add($"web_page_fetch.backfill_exhausted success_count={successfulFetchCount} success_target={stability.SuccessTarget} skipped={documents.Count - fetchBudget}");
            }
            else
            {
                trace.Add($"web_page_fetch.skipped reason=budget_exhausted count={documents.Count - fetchBudget}");
            }
        }

        trace.Add($"web_page_fetch.attempted_count={attemptedFetchCount}");
        trace.Add($"web_page_fetch.extracted_count={successfulFetchCount}");
        trace.AddRange(_diagnosticsSummarizer.Summarize(fetchDiagnostics));

        return new WebSearchFetchEnrichmentResult(
            enrichedDocuments.ToArray(),
            trace);
    }
}

