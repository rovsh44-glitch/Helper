namespace Helper.Runtime.WebResearch.Providers;

public interface IWebSearchProvider
{
    string ProviderId { get; }

    bool IsEnabled { get; }

    Task<WebSearchProviderClientResponse> SearchAsync(WebSearchPlan plan, CancellationToken ct = default);
}

