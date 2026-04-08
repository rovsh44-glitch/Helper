using Helper.Runtime.WebResearch.Extraction;
using Helper.Runtime.WebResearch.Fetching;

namespace Helper.Runtime.WebResearch.Rendering;

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
            new UnavailableBrowserRenderHost())
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

internal sealed class UnavailableBrowserRenderHost : IBrowserRenderHost
{
    public async Task<BrowserRenderHostResult> RenderAsync(
        Uri requestedUri,
        BrowserRenderHostOptions options,
        CancellationToken ct = default)
    {
        await Task.Yield();

        return new BrowserRenderHostResult(
            false,
            "browser_render_unavailable",
            requestedUri.AbsoluteUri,
            null,
            null,
            new[]
            {
                $"browser_render.unavailable target={requestedUri} reason=browser_runtime_not_configured timeout_ms={(int)options.Timeout.TotalMilliseconds}"
            });
    }
}

