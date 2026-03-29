namespace Helper.Runtime.WebResearch.Fetching;

public sealed partial class WebPageFetcher
{
    private static HttpClient CreateHttpClient(HttpMessageHandler? handler, TransportClientProfile profile)
    {
        HttpMessageHandler effectiveHandler = handler ??
            profile switch
            {
                TransportClientProfile.Default => HttpFetchSupport.CreateDefaultHandler(),
                TransportClientProfile.TlsCompatibility => HttpFetchSupport.CreateTlsCompatibilityHandler(),
                TransportClientProfile.ProxyAware => HttpFetchSupport.CreateProxyAwareHandler(),
                TransportClientProfile.ProxyTlsCompatibility => HttpFetchSupport.CreateProxyTlsCompatibilityHandler(),
                _ => HttpFetchSupport.CreateDefaultHandler()
            };

        var client = new HttpClient(effectiveHandler)
        {
            Timeout = WebPageFetchSettings.ReadTimeout()
        };
        switch (profile)
        {
            case TransportClientProfile.TlsCompatibility:
            case TransportClientProfile.ProxyTlsCompatibility:
                HttpFetchSupport.ApplyTlsCompatibilityDefaults(client, prefersDocuments: true);
                break;
            case TransportClientProfile.ProxyAware:
                HttpFetchSupport.ApplyProxyAwareDefaults(client, prefersDocuments: true);
                break;
            default:
                HttpFetchSupport.ApplyBrowserLikeDefaults(client, prefersDocuments: true);
                break;
        }

        return client;
    }

    private static WebPageFetchResult Failure(
        string requestedUrl,
        string? resolvedUrl,
        string outcome,
        IReadOnlyList<string> trace,
        WebPageFetchDiagnostics? diagnostics = null)
    {
        return new WebPageFetchResult(
            requestedUrl,
            resolvedUrl,
            Success: false,
            Outcome: outcome,
            ExtractedPage: null,
            Trace: trace,
            UsedBrowserRenderFallback: false,
            Diagnostics: diagnostics);
    }

    private static string SanitizeTraceValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "unknown";
        }

        return value
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Replace('"', '\'')
            .Trim();
    }
}
