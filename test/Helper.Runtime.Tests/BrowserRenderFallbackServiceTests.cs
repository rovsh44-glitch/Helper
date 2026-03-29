using Helper.Runtime.WebResearch;
using Helper.Runtime.WebResearch.Extraction;
using Helper.Runtime.WebResearch.Fetching;
using Helper.Runtime.WebResearch.Rendering;

namespace Helper.Runtime.Tests;

public sealed class BrowserRenderFallbackServiceTests
{
    [Fact]
    public async Task TryRenderAsync_ExtractsRenderedHtml_WhenHostReturnsContent()
    {
        var service = new BrowserRenderFallbackService(
            new AllowAllSecurityPolicy(),
            new WebPageContentExtractor(),
            new StubBrowserRenderHost(
                new BrowserRenderHostResult(
                    true,
                    "rendered",
                    "https://example.org/rendered",
                    "text/html",
                    "<html><head><title>Rendered</title></head><body><p>This rendered content contains enough evidence for extraction and grounding after browser fallback.</p></body></html>",
                    new[] { "browser_render.completed target=https://example.org/rendered" })));

        var result = await service.TryRenderAsync(
            new Uri("https://example.org/rendered"),
            new RenderFallbackBudgetDecision(
                true,
                "allowed",
                TimeSpan.FromSeconds(5),
                200_000,
                new[] { "browser_render.budget allowed=yes" }),
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(result.Page);
        Assert.Equal("https://example.org/rendered", result.Page!.CanonicalUrl);
        Assert.Contains("enough evidence for extraction", result.Page.Body, StringComparison.Ordinal);
        Assert.Contains(result.Trace, line => line.Contains("browser_render.extracted", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TryRenderAsync_ClassifiesSpawnFailures_WhenBrowserCannotLaunch()
    {
        var service = new BrowserRenderFallbackService(
            new AllowAllSecurityPolicy(),
            new WebPageContentExtractor(),
            new ThrowingBrowserRenderHost(new InvalidOperationException("spawn EPERM")));

        var result = await service.TryRenderAsync(
            new Uri("https://pmc.ncbi.nlm.nih.gov/articles/PMC5167494"),
            new RenderFallbackBudgetDecision(
                true,
                "allowed",
                TimeSpan.FromSeconds(5),
                200_000,
                new[] { "browser_render.budget allowed=yes" }),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("browser_spawn_blocked", result.Outcome);
        Assert.Contains(result.Trace, line => line.Contains("browser_render.failure category=browser_spawn_blocked", StringComparison.Ordinal));
        Assert.Contains(result.Trace, line => line.Contains("browser_render.unavailable category=browser_spawn_blocked", StringComparison.Ordinal));
    }

    private sealed class StubBrowserRenderHost : IBrowserRenderHost
    {
        private readonly BrowserRenderHostResult _result;

        public StubBrowserRenderHost(BrowserRenderHostResult result)
        {
            _result = result;
        }

        public Task<BrowserRenderHostResult> RenderAsync(
            Uri requestedUri,
            BrowserRenderHostOptions options,
            CancellationToken ct = default)
        {
            return Task.FromResult(_result);
        }
    }

    private sealed class ThrowingBrowserRenderHost : IBrowserRenderHost
    {
        private readonly Exception _exception;

        public ThrowingBrowserRenderHost(Exception exception)
        {
            _exception = exception;
        }

        public Task<BrowserRenderHostResult> RenderAsync(
            Uri requestedUri,
            BrowserRenderHostOptions options,
            CancellationToken ct = default)
        {
            throw _exception;
        }
    }

    private sealed class AllowAllSecurityPolicy : IWebFetchSecurityPolicy
    {
        public Task<WebFetchSecurityDecision> EvaluateAsync(
            Uri targetUri,
            WebFetchTargetKind targetKind,
            bool allowTrustedLoopback = false,
            CancellationToken ct = default)
        {
            return Task.FromResult(new WebFetchSecurityDecision(
                true,
                "allowed",
                new[] { $"web_fetch.allowed target={targetUri}" }));
        }
    }
}

