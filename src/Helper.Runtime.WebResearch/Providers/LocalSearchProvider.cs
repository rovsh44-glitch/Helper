using Helper.Runtime.WebResearch.Fetching;

namespace Helper.Runtime.WebResearch.Providers;

public sealed class LocalSearchProvider : JsonSearchProviderBase
{
    public const string Id = "local";

    public LocalSearchProvider()
        : this(WebSearchProviderSettings.ReadLocalBaseUrl())
    {
    }

    public LocalSearchProvider(string baseUrl)
        : this(baseUrl, null, null)
    {
    }

    public LocalSearchProvider(
        string baseUrl,
        IWebFetchSecurityPolicy? securityPolicy,
        IRedirectGuard? redirectGuard)
        : base(Id, baseUrl, WebSearchProviderSettings.ReadProviderTimeout(), securityPolicy, redirectGuard, allowTrustedLoopback: true)
    {
    }

    protected override Uri BuildSearchUri(WebSearchPlan plan)
    {
        var language = LooksRussian(plan.Query) ? "ru-RU" : "en-US";
        return new Uri($"{BaseUrl}/search?q={Uri.EscapeDataString(plan.Query)}&format=json&language={Uri.EscapeDataString(language)}");
    }

    private static bool LooksRussian(string? query)
    {
        return !string.IsNullOrWhiteSpace(query) &&
               query.Any(static ch => ch is >= '\u0400' and <= '\u04FF');
    }
}

