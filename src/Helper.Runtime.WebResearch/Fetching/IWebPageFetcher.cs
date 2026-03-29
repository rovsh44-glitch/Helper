namespace Helper.Runtime.WebResearch.Fetching;

public sealed record WebPageFetchContext(
    int FetchOrdinal,
    int FetchBudget,
    bool AllowBrowserRenderFallback,
    int RenderBudgetRemaining)
{
    public static WebPageFetchContext Default { get; } = new(
        FetchOrdinal: 1,
        FetchBudget: 1,
        AllowBrowserRenderFallback: true,
        RenderBudgetRemaining: 1);
}

public interface IWebPageFetcher
{
    Task<WebPageFetchResult> FetchAsync(string url, CancellationToken ct = default);
    Task<WebPageFetchResult> FetchAsync(string url, WebPageFetchContext context, CancellationToken ct = default);
}

internal sealed class NoopWebPageFetcher : IWebPageFetcher
{
    public static NoopWebPageFetcher Instance { get; } = new();

    private NoopWebPageFetcher()
    {
    }

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
            Outcome: "disabled",
            ExtractedPage: null,
            Trace: new[] { $"web_page_fetch.disabled url={url}" },
            UsedBrowserRenderFallback: false));
    }
}

