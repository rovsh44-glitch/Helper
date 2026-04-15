using Helper.Runtime.WebResearch;
using Helper.Runtime.WebResearch.Extraction;
using Helper.Runtime.WebResearch.Fetching;
using Helper.Runtime.WebResearch.Rendering;

namespace Helper.Runtime.Tests;

public sealed class BrowserRenderTimeoutRegressionTests
{
    [Fact]
    public async Task TryRenderAsync_ConvertsInternalTimeoutIntoNonFatalFailureOutcome()
    {
        var service = new BrowserRenderFallbackService(
            new AllowAllSecurityPolicy(),
            new WebPageContentExtractor(),
            new TimeoutBrowserRenderHost());

        var result = await service.TryRenderAsync(
            new Uri("https://example.org/slow-page"),
            new RenderFallbackBudgetDecision(
                true,
                "allowed",
                TimeSpan.FromSeconds(5),
                200_000,
                new[] { "browser_render.budget allowed=yes" }),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("browser_render_timeout", result.Outcome);
        Assert.Contains(result.Trace, line => line.Contains("browser_render.failure category=browser_render_timeout", StringComparison.Ordinal));
        Assert.Contains(result.Trace, line => line.Contains("browser_render.unavailable category=browser_render_timeout", StringComparison.Ordinal));
    }

    private sealed class TimeoutBrowserRenderHost : IBrowserRenderHost
    {
        public Task<BrowserRenderHostResult> RenderAsync(
            Uri requestedUri,
            BrowserRenderHostOptions options,
            CancellationToken ct = default)
        {
            throw new TaskCanceledException("Simulated browser render timeout.");
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
