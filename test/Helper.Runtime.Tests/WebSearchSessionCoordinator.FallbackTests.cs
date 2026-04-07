using Helper.Runtime.WebResearch;
using Helper.Runtime.WebResearch.Fetching;
using Helper.Runtime.WebResearch.Normalization;
using Helper.Runtime.WebResearch.Quality;
using Helper.Runtime.WebResearch.Ranking;
using Helper.Runtime.WebResearch.Rendering;
using Helper.Runtime.WebResearch.Safety;
using Helper.Testing.WebResearch;

namespace Helper.Runtime.Tests;

[Trait("Lane", "Integration")]
public sealed partial class WebSearchSessionCoordinatorTests
{
    [Fact]
    public async Task ExecuteAsync_UsesDeterministicFallback_FromUserProvidedUrl_WhenProviderReturnsNothing()
    {
        var coordinator = WebSearchSessionCoordinatorFactory.Create(new StubProviderClient());

        var session = await coordinator.ExecuteAsync(
            new WebSearchRequest("проанализируй https://example.org/report и сравни выводы", MaxResults: 5),
            CancellationToken.None);

        Assert.Equal("deterministic_fallback", session.ResultBundle.Outcome);
        Assert.True(session.ResultBundle.UsedDeterministicFallback);
        Assert.Single(session.ResultBundle.Documents);
        Assert.Equal("https://example.org/report", session.ResultBundle.SourceUrls[0]);
        Assert.Contains("could not fetch live page content", session.ResultBundle.Documents[0].Snippet, StringComparison.OrdinalIgnoreCase);
    }


    [Fact]
    public async Task ExecuteAsync_DropsMachineDiagnosticSearchResult_AndFallsBackToUserUrl()
    {
        var coordinator = WebSearchSessionCoordinatorFactory.Create(
            new StubProviderClient(
                new WebSearchDocument(
                    "https://www.kaggle.com/competitions/kaggle-measuring-agi",
                    "Kaggle competition",
                    "Unexpected token , \"!doctype \"... is not valid JSON. SyntaxError: Unexpected token , \"!doctype \"... is not valid JSON.")),
            new WebQueryPlanner(),
            new SearchIterationPolicy(),
            new SearchEvidenceSufficiencyPolicy(),
            NoopWebPageFetcher.Instance,
            new EvidenceBoundaryProjector(),
            new RenderedPageBudgetPolicy(),
            new SourceAuthorityScorer(),
            new SpamAndSeoDemotionPolicy(),
            new CanonicalUrlResolver(),
            new DuplicateContentCollapsePolicy(),
            new EventClusterBuilder(),
            new WebDocumentQualityPolicy());

        var session = await coordinator.ExecuteAsync(
            new WebSearchRequest("Проанализируй статью и предоставь своё мнение: https://www.kaggle.com/competitions/kaggle-measuring-agi", MaxResults: 5),
            CancellationToken.None);

        Assert.Equal("deterministic_fallback", session.ResultBundle.Outcome);
        Assert.True(session.ResultBundle.UsedDeterministicFallback);
        Assert.Single(session.ResultBundle.Documents);
        Assert.Contains("could not fetch live page content", session.ResultBundle.Documents[0].Snippet, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(session.ResultBundle.ProviderTrace!, line => line.Contains("web_document_quality.allowed=no", StringComparison.OrdinalIgnoreCase));
    }


    [Fact]
    public async Task ExecuteAsync_PreservesProviderBlockedTrace_WhenFallbackIsUsed()
    {
        var coordinator = WebSearchSessionCoordinatorFactory.Create(new StubProviderClient(_ =>
            new WebSearchProviderClientResponse(
                Array.Empty<WebSearchDocument>(),
                new[] { "local:web_fetch.blocked reason=private_or_loopback_address target=http://169.254.169.254" })));

        var session = await coordinator.ExecuteAsync(
            new WebSearchRequest("analyze https://example.org/report", MaxResults: 5),
            CancellationToken.None);

        Assert.Equal("deterministic_fallback", session.ResultBundle.Outcome);
        Assert.Contains(session.ResultBundle.ProviderTrace!, line => line.Contains("private_or_loopback_address", StringComparison.OrdinalIgnoreCase));
    }


    [Fact]
    public async Task ExecuteAsync_SummarizesTransportFailures_WhenFetchCannotRecoverEvidence()
    {
        var coordinator = WebSearchSessionCoordinatorFactory.Create(
            new StubProviderClient(_ =>
                new[]
                {
                    new WebSearchDocument(
                        "https://pmc.ncbi.nlm.nih.gov/articles/PMC9159724",
                        "The Influence of Phototherapy on Recovery From Exercise-Induced Muscle Damage",
                        "Systematic review of muscle recovery and exercise-induced damage."),
                    new WebSearchDocument(
                        "https://link.springer.com/article/10.1007/s10103-025-04318-w",
                        "A systematic review on whole-body photobiomodulation for exercise performance",
                        "Systematic review of whole-body photobiomodulation and exercise performance.")
                }),
            new WebQueryPlanner(),
            new SearchIterationPolicy(),
            new SearchEvidenceSufficiencyPolicy(),
            new TransportFailurePageFetcher(),
            new EvidenceBoundaryProjector(),
            new RenderedPageBudgetPolicy(),
            new SourceAuthorityScorer(),
            new SpamAndSeoDemotionPolicy(),
            new CanonicalUrlResolver(),
            new DuplicateContentCollapsePolicy(),
            new EventClusterBuilder());

        var session = await coordinator.ExecuteAsync(
            new WebSearchRequest(
                "Объясни, насколько убедительны данные о пользе терапии красным светом для восстановления после тренировок.",
                MaxResults: 5),
            CancellationToken.None);

        Assert.Contains(session.ResultBundle.PageTrace!, line => line.Contains("web_page_fetch.failure_count=2", StringComparison.Ordinal));
        Assert.Contains(session.ResultBundle.PageTrace!, line => line.Contains("web_page_fetch.transport_failure_count=2", StringComparison.Ordinal));
        Assert.Contains(session.ResultBundle.PageTrace!, line => line.Contains("web_page_fetch.transport_categories=connection_refused:2", StringComparison.Ordinal));
        Assert.Contains(session.ResultBundle.PageTrace!, line => line.Contains("web_page_fetch.failure host=pmc.ncbi.nlm.nih.gov", StringComparison.Ordinal));
        Assert.Contains(session.ResultBundle.PageTrace!, line => line.Contains("web_page_fetch.failure host=link.springer.com", StringComparison.Ordinal));
    }


}
