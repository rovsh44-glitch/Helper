using Helper.Runtime.WebResearch.Fetching;

namespace Helper.Runtime.WebResearch.Providers;

public sealed class LocalSearchProvider : JsonSearchProviderBase
{
    public const string Id = "local";
    private readonly ILocalSearchTimeoutCompactionPolicy _timeoutCompactionPolicy;

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
        : this(baseUrl, securityPolicy, redirectGuard, null, null)
    {
    }

    internal LocalSearchProvider(
        string baseUrl,
        IWebFetchSecurityPolicy? securityPolicy,
        IRedirectGuard? redirectGuard,
        ILocalSearchTimeoutCompactionPolicy? timeoutCompactionPolicy,
        HttpMessageHandler? handler)
        : base(
            Id,
            baseUrl,
            WebSearchProviderSettings.ReadProviderTimeout(),
            securityPolicy,
            redirectGuard,
            allowTrustedLoopback: true,
            handler)
    {
        _timeoutCompactionPolicy = timeoutCompactionPolicy ?? new LocalSearchTimeoutCompactionPolicy();
    }

    protected override Uri BuildSearchUri(WebSearchPlan plan)
    {
        var language = LooksRussian(plan.Query) ? "ru-RU" : "en-US";
        return new Uri($"{BaseUrl}/search?q={Uri.EscapeDataString(plan.Query)}&format=json&language={Uri.EscapeDataString(language)}");
    }

    protected internal override SearchProviderTimeoutRecoveryDecision? BuildTimeoutRecoveryDecision(WebSearchPlan plan)
    {
        var compaction = _timeoutCompactionPolicy.Compact(plan);
        if (!compaction.Applied)
        {
            return null;
        }

        return new SearchProviderTimeoutRecoveryDecision(
            plan with { Query = compaction.Query },
            compaction.Trace);
    }

    private static bool LooksRussian(string? query)
    {
        return !string.IsNullOrWhiteSpace(query) &&
               query.Any(static ch => ch is >= '\u0400' and <= '\u04FF');
    }
}

