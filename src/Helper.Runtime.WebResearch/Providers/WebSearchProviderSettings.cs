namespace Helper.Runtime.WebResearch.Providers;

public static class WebSearchProviderSettings
{
    private const string DefaultLocalUrl = "http://localhost:8080";
    private const int DefaultTimeoutSeconds = 4;
    private const int DefaultCostBudgetUnits = 3;
    private const int DefaultLatencyBudgetMs = 3500;
    private const int DefaultProviderCooldownSeconds = 45;
    private const int DefaultMaxConsecutiveTimeouts = 2;
    private const int DefaultMaxConsecutiveErrors = 2;
    private const int DefaultSlowProviderLatencyMs = 1200;

    public static string ReadLocalBaseUrl()
    {
        return ReadBaseUrl("HELPER_WEB_SEARCH_LOCAL_URL", DefaultLocalUrl);
    }

    public static string ReadSearxBaseUrl()
    {
        return ReadBaseUrl("HELPER_WEB_SEARCH_SEARX_URL", string.Empty);
    }

    public static TimeSpan ReadProviderTimeout()
    {
        var raw = Environment.GetEnvironmentVariable("HELPER_WEB_SEARCH_PROVIDER_TIMEOUT_SEC");
        if (int.TryParse(raw, out var parsed))
        {
            return TimeSpan.FromSeconds(Math.Clamp(parsed, 1, 30));
        }

        return TimeSpan.FromSeconds(DefaultTimeoutSeconds);
    }

    public static IReadOnlyList<string> ReadProviderOrder()
    {
        var raw = Environment.GetEnvironmentVariable("HELPER_WEB_SEARCH_PROVIDER_ORDER");
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new[] { "local", "searx" };
        }

        return raw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static value => value.Trim().ToLowerInvariant())
            .Where(static value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static int ReadMaxRedirects()
    {
        var raw = Environment.GetEnvironmentVariable("HELPER_WEB_FETCH_MAX_REDIRECTS");
        return int.TryParse(raw, out var parsed)
            ? Math.Clamp(parsed, 0, 5)
            : 3;
    }

    public static int ReadSearchCostBudgetUnits()
    {
        var raw = Environment.GetEnvironmentVariable("HELPER_WEB_SEARCH_COST_BUDGET_UNITS");
        return int.TryParse(raw, out var parsed)
            ? Math.Clamp(parsed, 0, 8)
            : DefaultCostBudgetUnits;
    }

    public static int ReadSearchLatencyBudgetMs()
    {
        var raw = Environment.GetEnvironmentVariable("HELPER_WEB_SEARCH_LATENCY_BUDGET_MS");
        return int.TryParse(raw, out var parsed)
            ? Math.Clamp(parsed, 250, 30000)
            : DefaultLatencyBudgetMs;
    }

    public static TimeSpan ReadProviderCooldownWindow()
    {
        var raw = Environment.GetEnvironmentVariable("HELPER_WEB_SEARCH_PROVIDER_COOLDOWN_SEC");
        return int.TryParse(raw, out var parsed)
            ? TimeSpan.FromSeconds(Math.Clamp(parsed, 5, 600))
            : TimeSpan.FromSeconds(DefaultProviderCooldownSeconds);
    }

    public static int ReadMaxConsecutiveTimeouts()
    {
        var raw = Environment.GetEnvironmentVariable("HELPER_WEB_SEARCH_PROVIDER_MAX_CONSECUTIVE_TIMEOUTS");
        return int.TryParse(raw, out var parsed)
            ? Math.Clamp(parsed, 1, 10)
            : DefaultMaxConsecutiveTimeouts;
    }

    public static int ReadMaxConsecutiveErrors()
    {
        var raw = Environment.GetEnvironmentVariable("HELPER_WEB_SEARCH_PROVIDER_MAX_CONSECUTIVE_ERRORS");
        return int.TryParse(raw, out var parsed)
            ? Math.Clamp(parsed, 1, 10)
            : DefaultMaxConsecutiveErrors;
    }

    public static int ReadSlowProviderLatencyMs()
    {
        var raw = Environment.GetEnvironmentVariable("HELPER_WEB_SEARCH_PROVIDER_SLOW_LATENCY_MS");
        return int.TryParse(raw, out var parsed)
            ? Math.Clamp(parsed, 100, 30000)
            : DefaultSlowProviderLatencyMs;
    }

    private static string ReadBaseUrl(string envName, string fallback)
    {
        var raw = Environment.GetEnvironmentVariable(envName);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return fallback;
        }

        return raw.Trim().TrimEnd('/');
    }
}

