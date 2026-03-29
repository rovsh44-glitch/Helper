namespace Helper.Runtime.WebResearch.Fetching;

internal static class WebPageFetchSettings
{
    private const int DefaultTimeoutSeconds = 6;
    private const int DefaultMaxBytes = 400_000;
    private const int DefaultMaxFetchesPerSearch = 3;
    private const int DefaultMaxFetchAttemptsPerSearch = 5;

    public static TimeSpan ReadTimeout()
    {
        var raw = Environment.GetEnvironmentVariable("HELPER_WEB_PAGE_FETCH_TIMEOUT_SEC");
        return int.TryParse(raw, out var parsed)
            ? TimeSpan.FromSeconds(Math.Clamp(parsed, 1, 20))
            : TimeSpan.FromSeconds(DefaultTimeoutSeconds);
    }

    public static int ReadMaxResponseBytes()
    {
        var raw = Environment.GetEnvironmentVariable("HELPER_WEB_PAGE_MAX_BYTES");
        return int.TryParse(raw, out var parsed)
            ? Math.Clamp(parsed, 16_384, 2_000_000)
            : DefaultMaxBytes;
    }

    public static int ReadMaxFetchesPerSearch()
    {
        var raw = Environment.GetEnvironmentVariable("HELPER_WEB_PAGE_MAX_FETCHES_PER_SEARCH");
        return int.TryParse(raw, out var parsed)
            ? Math.Clamp(parsed, 1, 6)
            : DefaultMaxFetchesPerSearch;
    }

    public static int ReadMaxFetchAttemptsPerSearch()
    {
        var raw = Environment.GetEnvironmentVariable("HELPER_WEB_PAGE_MAX_FETCH_ATTEMPTS_PER_SEARCH");
        return int.TryParse(raw, out var parsed)
            ? Math.Clamp(parsed, 1, 8)
            : DefaultMaxFetchAttemptsPerSearch;
    }
}

