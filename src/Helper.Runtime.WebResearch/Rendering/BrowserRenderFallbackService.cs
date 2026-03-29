using System.Collections.Concurrent;
using Helper.Runtime.WebResearch.Extraction;
using Helper.Runtime.WebResearch.Fetching;
using Microsoft.Playwright;

namespace Helper.Runtime.WebResearch.Rendering;

public interface IBrowserRenderFallbackService
{
    Task<BrowserRenderFallbackResult> TryRenderAsync(
        Uri requestedUri,
        RenderFallbackBudgetDecision budget,
        CancellationToken ct = default);
}

public sealed record BrowserRenderFallbackResult(
    bool Success,
    string Outcome,
    ExtractedWebPage? Page,
    IReadOnlyList<string> Trace);

internal interface IBrowserRenderHost
{
    Task<BrowserRenderHostResult> RenderAsync(
        Uri requestedUri,
        BrowserRenderHostOptions options,
        CancellationToken ct = default);
}

internal sealed record BrowserRenderHostOptions(
    TimeSpan Timeout,
    int MaxHtmlChars);

internal sealed record BrowserRenderHostResult(
    bool Success,
    string Outcome,
    string? ResolvedUrl,
    string? ContentType,
    string? Html,
    IReadOnlyList<string> Trace);

public sealed class BrowserRenderFallbackService : IBrowserRenderFallbackService
{
    private readonly IWebFetchSecurityPolicy _securityPolicy;
    private readonly IWebPageContentExtractor _contentExtractor;
    private readonly IBrowserRenderHost _browserRenderHost;

    public BrowserRenderFallbackService(
        IWebFetchSecurityPolicy securityPolicy,
        IWebPageContentExtractor contentExtractor)
        : this(
            securityPolicy,
            contentExtractor,
            new PlaywrightBrowserRenderHost(securityPolicy))
    {
    }

    internal BrowserRenderFallbackService(
        IWebFetchSecurityPolicy securityPolicy,
        IWebPageContentExtractor contentExtractor,
        IBrowserRenderHost browserRenderHost)
    {
        _securityPolicy = securityPolicy;
        _contentExtractor = contentExtractor;
        _browserRenderHost = browserRenderHost;
    }

    public async Task<BrowserRenderFallbackResult> TryRenderAsync(
        Uri requestedUri,
        RenderFallbackBudgetDecision budget,
        CancellationToken ct = default)
    {
        var trace = new List<string>();
        trace.AddRange(budget.Trace);
        if (!budget.Allowed)
        {
            return Failure("budget_denied", trace);
        }

        var securityDecision = await _securityPolicy.EvaluateAsync(
            requestedUri,
            WebFetchTargetKind.PageFetch,
            allowTrustedLoopback: false,
            ct).ConfigureAwait(false);
        trace.AddRange(securityDecision.Trace.Select(static line => $"browser_render.{line}"));
        if (!securityDecision.Allowed)
        {
            return Failure("blocked", trace);
        }

        BrowserRenderHostResult hostResult;
        try
        {
            hostResult = await _browserRenderHost.RenderAsync(
                requestedUri,
                new BrowserRenderHostOptions(budget.Timeout, budget.MaxHtmlChars),
                ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var failure = BrowserRenderFailureClassifier.Classify(ex);
            trace.Add($"browser_render.failure category={failure.Category} reason={Sanitize(failure.Reason)}");
            trace.Add($"browser_render.unavailable category={failure.Category} reason={Sanitize(failure.Reason)}");
            return Failure(failure.Outcome, trace);
        }

        trace.AddRange(hostResult.Trace);
        if (!hostResult.Success || string.IsNullOrWhiteSpace(hostResult.Html))
        {
            return Failure(hostResult.Outcome, trace);
        }

        var resolvedUri = Uri.TryCreate(hostResult.ResolvedUrl, UriKind.Absolute, out var parsedResolvedUri)
            ? parsedResolvedUri
            : requestedUri;
        var extractedPage = _contentExtractor.Extract(
            requestedUri,
            resolvedUri,
            hostResult.ContentType ?? "text/html",
            hostResult.Html);
        if (extractedPage is null)
        {
            trace.Add($"browser_render.extraction_failed target={resolvedUri}");
            return Failure("extraction_failed", trace);
        }

        trace.Add($"browser_render.extracted target={resolvedUri} canonical={extractedPage.CanonicalUrl} passages={extractedPage.Passages.Count}");
        return new BrowserRenderFallbackResult(
            true,
            "rendered",
            extractedPage,
            trace);
    }

    private static BrowserRenderFallbackResult Failure(string outcome, IReadOnlyList<string> trace)
    {
        return new BrowserRenderFallbackResult(false, outcome, null, trace);
    }

    private static string Sanitize(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return "unknown";
        }

        return message
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
    }
}

internal sealed class PlaywrightBrowserRenderHost : IBrowserRenderHost
{
    private readonly IWebFetchSecurityPolicy _securityPolicy;

    public PlaywrightBrowserRenderHost(IWebFetchSecurityPolicy securityPolicy)
    {
        _securityPolicy = securityPolicy;
    }

    public async Task<BrowserRenderHostResult> RenderAsync(
        Uri requestedUri,
        BrowserRenderHostOptions options,
        CancellationToken ct = default)
    {
        var trace = new List<string> { "browser_render.host=playwright" };
        var blockedRequests = 0;
        var blockedHeavyResources = 0;
        var allowedRequests = 0;
        var blockedSamples = new ConcurrentQueue<string>();

        try
        {
            using var playwright = await Playwright.CreateAsync().ConfigureAwait(false);
            await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true
            }).ConfigureAwait(false);

            var context = await browser.NewContextAsync(new BrowserNewContextOptions
            {
                UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36"
            }).ConfigureAwait(false);

            await context.RouteAsync("**/*", async route =>
            {
                var request = route.Request;
                if (request.ResourceType is "image" or "media" or "font")
                {
                    Interlocked.Increment(ref blockedHeavyResources);
                    await route.AbortAsync().ConfigureAwait(false);
                    return;
                }

                if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var resourceUri) ||
                    (!resourceUri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
                     !resourceUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
                {
                    Interlocked.Increment(ref blockedRequests);
                    blockedSamples.Enqueue("invalid_resource_uri");
                    await route.AbortAsync().ConfigureAwait(false);
                    return;
                }

                var securityDecision = await _securityPolicy.EvaluateAsync(
                    resourceUri,
                    WebFetchTargetKind.PageFetch,
                    allowTrustedLoopback: false,
                    ct).ConfigureAwait(false);
                if (!securityDecision.Allowed)
                {
                    Interlocked.Increment(ref blockedRequests);
                    if (blockedSamples.Count < 3)
                    {
                        blockedSamples.Enqueue(resourceUri.Host);
                    }

                    await route.AbortAsync().ConfigureAwait(false);
                    return;
                }

                Interlocked.Increment(ref allowedRequests);
                await route.ContinueAsync().ConfigureAwait(false);
            }).ConfigureAwait(false);

            var page = await context.NewPageAsync().ConfigureAwait(false);
            await page.GotoAsync(
                requestedUri.AbsoluteUri,
                new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.NetworkIdle,
                    Timeout = (float)options.Timeout.TotalMilliseconds
                }).ConfigureAwait(false);

            var resolvedUrl = page.Url;
            var resolvedUri = Uri.TryCreate(resolvedUrl, UriKind.Absolute, out var parsedResolvedUri)
                ? parsedResolvedUri
                : requestedUri;
            var finalDecision = await _securityPolicy.EvaluateAsync(
                resolvedUri,
                WebFetchTargetKind.PageFetch,
                allowTrustedLoopback: false,
                ct).ConfigureAwait(false);
            trace.AddRange(finalDecision.Trace.Select(static line => $"browser_render.final.{line}"));
            if (!finalDecision.Allowed)
            {
                await context.CloseAsync().ConfigureAwait(false);
                return new BrowserRenderHostResult(
                    false,
                    "blocked",
                    resolvedUri.AbsoluteUri,
                    null,
                    null,
                    trace);
            }

            var html = await page.ContentAsync().ConfigureAwait(false);
            if (html.Length > options.MaxHtmlChars)
            {
                html = html[..options.MaxHtmlChars];
                trace.Add($"browser_render.html_truncated max_chars={options.MaxHtmlChars}");
            }

            trace.Add(
                $"browser_render.completed target={requestedUri} resolved={resolvedUri} allowed_requests={allowedRequests} blocked_requests={blockedRequests} heavy_resources_blocked={blockedHeavyResources}");
            if (!blockedSamples.IsEmpty)
            {
                trace.Add($"browser_render.blocked_samples={string.Join(",", blockedSamples.Take(3))}");
            }

            await context.CloseAsync().ConfigureAwait(false);
            return new BrowserRenderHostResult(
                true,
                "rendered",
                resolvedUri.AbsoluteUri,
                "text/html",
                html,
                trace);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            trace.Add("browser_render.timeout");
            return new BrowserRenderHostResult(false, "timeout", requestedUri.AbsoluteUri, null, null, trace);
        }
        catch (Exception ex)
        {
            var failure = BrowserRenderFailureClassifier.Classify(ex);
            trace.Add($"browser_render.failure category={failure.Category} reason={Sanitize(failure.Reason)}");
            trace.Add($"browser_render.unavailable category={failure.Category} reason={Sanitize(failure.Reason)}");
            return new BrowserRenderHostResult(false, failure.Outcome, requestedUri.AbsoluteUri, null, null, trace);
        }
    }

    private static string Sanitize(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return "unknown";
        }

        return message
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
    }
}

