using Helper.Runtime.Core;
using Helper.Runtime.WebResearch;

namespace Helper.Runtime.Infrastructure;

public sealed class WebSearchProviderClient : IWebSearchProviderClient
{
    private readonly IWebSearcher _searcher;

    public WebSearchProviderClient(IWebSearcher searcher)
    {
        _searcher = searcher;
    }

    public async Task<WebSearchProviderClientResponse> SearchAsync(WebSearchPlan plan, CancellationToken ct = default)
    {
        var results = await _searcher.SearchAsync(plan.Query, ct).ConfigureAwait(false);

        return new WebSearchProviderClientResponse(
            results
            .Take(plan.MaxResults)
            .Select(static result => new WebSearchDocument(
                result.Url,
                result.Title,
                result.Content,
                result.IsDeepScan))
            .ToArray(),
            new[] { "legacy_adapter:search_completed" });
    }
}

