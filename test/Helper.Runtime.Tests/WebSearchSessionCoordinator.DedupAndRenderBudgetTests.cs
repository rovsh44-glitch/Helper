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
    public async Task ExecuteAsync_CollapsesMirrorSources_AndBuildsEventClusters_AfterPageFetch()
    {
        var coordinator = WebSearchSessionCoordinatorFactory.Create(
            new StubProviderClient(plan =>
            {
                return new[]
                {
                    new WebSearchDocument(
                        "https://mirror.example.org/reuters/climate-pact?utm_source=feed",
                        "Leaders sign climate pact - Mirror",
                        "Leaders sign climate pact after overnight talks in Geneva."),
                    new WebSearchDocument(
                        "https://reuters.com/world/climate-pact",
                        "Leaders sign climate pact - Reuters",
                        "Leaders sign climate pact after overnight talks in Geneva."),
                    new WebSearchDocument(
                        "https://apnews.com/world/climate-pact",
                        "Leaders sign climate pact - AP News",
                        "Leaders sign climate pact after overnight talks in Geneva.")
                };
            }),
            new WebQueryPlanner(),
            new SearchIterationPolicy(),
            new SearchEvidenceSufficiencyPolicy(),
            new MirrorAwarePageFetcher(),
            new EvidenceBoundaryProjector(),
            new RenderedPageBudgetPolicy(),
            new SourceAuthorityScorer(),
            new SpamAndSeoDemotionPolicy(),
            new CanonicalUrlResolver(),
            new DuplicateContentCollapsePolicy(),
            new EventClusterBuilder());

        var session = await coordinator.ExecuteAsync(
            new WebSearchRequest("latest climate pact announcement", MaxResults: 5),
            CancellationToken.None);

        Assert.Equal(2, session.ResultBundle.Documents.Count);
        Assert.Equal(2, session.ResultBundle.SourceUrls.Count);
        Assert.DoesNotContain(session.ResultBundle.Documents, document => document.Url.Contains("mirror.example.org", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            (session.ResultBundle.ProviderTrace ?? Array.Empty<string>())
                .Concat(session.ResultBundle.PageTrace ?? Array.Empty<string>()),
            line => line.Contains("web_normalization.duplicate_", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(session.ResultBundle.PageTrace!, line => line.Contains("web_normalization.event_clusters=1", StringComparison.OrdinalIgnoreCase));
    }


    [Fact]
    public async Task ExecuteAsync_AppliesBrowserRenderBudget_PerSearchSession()
    {
        var previousValue = Environment.GetEnvironmentVariable("HELPER_WEB_RENDER_MAX_PAGES_PER_SEARCH");
        Environment.SetEnvironmentVariable("HELPER_WEB_RENDER_MAX_PAGES_PER_SEARCH", "1");

        try
        {
            var pageFetcher = new RenderBudgetAwarePageFetcher();
            var coordinator = WebSearchSessionCoordinatorFactory.Create(
                new StubProviderClient(
                    new WebSearchDocument("https://example.org/app-1", "App 1", "Shell 1"),
                    new WebSearchDocument("https://example.org/app-2", "App 2", "Shell 2")),
                new WebQueryPlanner(),
                new SearchIterationPolicy(),
                new SearchEvidenceSufficiencyPolicy(),
                pageFetcher,
                new EvidenceBoundaryProjector(),
                new RenderedPageBudgetPolicy(),
                new SourceAuthorityScorer(),
                new SpamAndSeoDemotionPolicy(),
                new CanonicalUrlResolver(),
                new DuplicateContentCollapsePolicy(),
                new EventClusterBuilder());

            var session = await coordinator.ExecuteAsync(
                new WebSearchRequest("latest hard js pages", MaxResults: 5),
                CancellationToken.None);

            Assert.Equal(2, pageFetcher.Contexts.Count);
            Assert.True(pageFetcher.Contexts[0].AllowBrowserRenderFallback);
            Assert.Equal(1, pageFetcher.Contexts[0].RenderBudgetRemaining);
            Assert.False(pageFetcher.Contexts[1].AllowBrowserRenderFallback);
            Assert.Equal(0, pageFetcher.Contexts[1].RenderBudgetRemaining);
            Assert.Contains(session.ResultBundle.PageTrace!, line => line.Contains("browser_render.session_budget=1", StringComparison.Ordinal));
        }
        finally
        {
            Environment.SetEnvironmentVariable("HELPER_WEB_RENDER_MAX_PAGES_PER_SEARCH", previousValue);
        }
    }



}
