using Helper.Runtime.WebResearch.Fetching;

namespace Helper.Runtime.WebResearch.Providers;

public sealed class SearxSearchProvider : JsonSearchProviderBase
{
    public const string Id = "searx";

    public SearxSearchProvider()
        : this(WebSearchProviderSettings.ReadSearxBaseUrl())
    {
    }

    public SearxSearchProvider(string baseUrl)
        : this(baseUrl, null, null)
    {
    }

    public SearxSearchProvider(
        string baseUrl,
        IWebFetchSecurityPolicy? securityPolicy,
        IRedirectGuard? redirectGuard)
        : base(Id, baseUrl, WebSearchProviderSettings.ReadProviderTimeout(), securityPolicy, redirectGuard)
    {
    }
}

