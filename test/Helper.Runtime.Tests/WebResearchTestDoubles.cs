using Helper.Runtime.WebResearch;
using Helper.Runtime.WebResearch.Fetching;

namespace Helper.Testing.WebResearch;

public sealed class StubProviderClient : IWebSearchProviderClient
{
    private readonly Func<WebSearchPlan, WebSearchProviderClientResponse> _handler;

    public StubProviderClient()
        : this(Array.Empty<WebSearchDocument>())
    {
    }

    public StubProviderClient(params WebSearchDocument[] documents)
        : this(_ => documents)
    {
    }

    public StubProviderClient(Func<WebSearchPlan, IEnumerable<WebSearchDocument>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _handler = plan => new WebSearchProviderClientResponse(handler(plan).ToArray(), Array.Empty<string>());
    }

    public StubProviderClient(Func<WebSearchPlan, WebSearchProviderClientResponse> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _handler = handler;
    }

    public Task<WebSearchProviderClientResponse> SearchAsync(WebSearchPlan plan, CancellationToken ct = default)
    {
        return Task.FromResult(_handler(plan));
    }
}

public sealed class MirrorAwarePageFetcher : IWebPageFetcher
{
    public Task<WebPageFetchResult> FetchAsync(string url, CancellationToken ct = default)
    {
        return FetchAsync(url, WebPageFetchContext.Default, ct);
    }

    public Task<WebPageFetchResult> FetchAsync(string url, WebPageFetchContext context, CancellationToken ct = default)
    {
        var page = BuildMirrorAwarePage(url);
        return Task.FromResult(new WebPageFetchResult(
            url,
            page.ResolvedUrl,
            Success: true,
            Outcome: "fetched",
            ExtractedPage: page,
            Trace: new[] { $"web_page_fetch.success url={url}" },
            UsedBrowserRenderFallback: false));
    }

    private static ExtractedWebPage BuildMirrorAwarePage(string url)
    {
        if (url.Contains("mirror.example.org", StringComparison.OrdinalIgnoreCase))
        {
            return BuildPage(
                url,
                "https://reuters.com/world/climate-pact",
                "Leaders sign climate pact - Reuters",
                "Leaders sign climate pact after overnight talks in Geneva. Delegations approved the agreement after a final round of edits.");
        }

        if (url.Contains("reuters.com", StringComparison.OrdinalIgnoreCase))
        {
            return BuildPage(
                url,
                "https://reuters.com/world/climate-pact",
                "Leaders sign climate pact - Reuters",
                "Leaders sign climate pact after overnight talks in Geneva. Delegations approved the agreement after a final round of edits.");
        }

        return BuildPage(
            url,
            url,
            "Leaders sign climate pact - AP News",
            "Leaders sign climate pact after overnight talks in Geneva. The agreement clears the way for implementation steps and public guidance.");
    }

    private static ExtractedWebPage BuildPage(string requestedUrl, string canonicalUrl, string title, string body)
    {
        return new ExtractedWebPage(
            RequestedUrl: requestedUrl,
            ResolvedUrl: canonicalUrl,
            CanonicalUrl: canonicalUrl,
            Title: title,
            PublishedAt: "2026-04-07",
            Body: body,
            Passages:
            [
                new ExtractedWebPassage(1, body)
            ],
            ContentType: "text/html");
    }
}

public sealed class RenderBudgetAwarePageFetcher : IWebPageFetcher
{
    public List<WebPageFetchContext> Contexts { get; } = new();

    public Task<WebPageFetchResult> FetchAsync(string url, CancellationToken ct = default)
    {
        return FetchAsync(url, WebPageFetchContext.Default, ct);
    }

    public Task<WebPageFetchResult> FetchAsync(string url, WebPageFetchContext context, CancellationToken ct = default)
    {
        Contexts.Add(context);

        return Task.FromResult(new WebPageFetchResult(
            url,
            url,
            Success: false,
            Outcome: "render_budget_probe",
            ExtractedPage: null,
            Trace: new[] { $"web_page_fetch.render_budget_probe url={url}" },
            UsedBrowserRenderFallback: context.AllowBrowserRenderFallback));
    }
}

public sealed class TransportFailurePageFetcher : IWebPageFetcher
{
    public Task<WebPageFetchResult> FetchAsync(string url, CancellationToken ct = default)
    {
        return FetchAsync(url, WebPageFetchContext.Default, ct);
    }

    public Task<WebPageFetchResult> FetchAsync(string url, WebPageFetchContext context, CancellationToken ct = default)
    {
        return Task.FromResult(new WebPageFetchResult(
            url,
            null,
            Success: false,
            Outcome: "fetch_failed",
            ExtractedPage: null,
            Trace: new[] { $"web_page_fetch.transport_failure url={url}" },
            UsedBrowserRenderFallback: false,
            Diagnostics: new WebPageFetchDiagnostics(
                AttemptCount: 1,
                RetryCount: 0,
                TransportFailureObserved: true,
                TransportRecovered: false,
                RecoveryProfile: null,
                FinalFailureCategory: "connection_refused",
                FinalFailureProfile: "connection_refused",
                FinalFailureReason: "connection refused",
                AttemptProfiles: new[] { "connection_refused" })));
    }
}
