namespace Helper.Runtime.WebResearch.Fetching;

public sealed partial class WebPageFetcher
{
    private async Task<TransportSendResult> SendAsyncWithTransportFallback(
        Uri currentUri,
        List<string> trace,
        CancellationToken ct)
    {
        try
        {
            var response = await SendWithClientAsync(_httpClient, currentUri, ct).ConfigureAwait(false);
            return new TransportSendResult(
                response,
                new WebPageFetchDiagnostics(
                    AttemptCount: 1,
                    RetryCount: 0,
                    AttemptProfiles: new[] { "default" }));
        }
        catch (Exception ex)
        {
            var defaultCategory = TransportExceptionClassifier.Categorize(ex);
            var proxyBrowserAvailable = _proxyAwareHttpClient is not null &&
                (_proxyAwareClientExplicit || HttpFetchSupport.HasUsableSystemProxy(currentUri));
            var proxyTlsCompatibilityAvailable = _proxyTlsCompatibilityHttpClient is not null &&
                (_proxyTlsCompatibilityClientExplicit || HttpFetchSupport.HasUsableSystemProxy(currentUri));
            var retryPlan = TransportExceptionClassifier.BuildRetryPlan(
                currentUri,
                ex,
                _tlsCompatibilityHttpClient is not null,
                proxyBrowserAvailable,
                proxyTlsCompatibilityAvailable);

            if (retryPlan.Count == 0)
            {
                trace.Add($"web_page_fetch.transport_failed target={currentUri} profile=default category={defaultCategory} reason={SanitizeTraceValue(TransportExceptionClassifier.Summarize(ex))}");
                throw new TransportFetchException(
                    new WebPageFetchDiagnostics(
                        AttemptCount: 1,
                        RetryCount: 0,
                        TransportFailureObserved: true,
                        FinalFailureCategory: defaultCategory,
                        FinalFailureProfile: "default",
                        FinalFailureReason: TransportExceptionClassifier.Summarize(ex),
                        AttemptProfiles: new[] { "default" }),
                    ex);
            }

            trace.Add($"web_page_fetch.transport_failed target={currentUri} profile=default category={defaultCategory} reason={SanitizeTraceValue(TransportExceptionClassifier.Summarize(ex))}");
            Exception? lastException = ex;
            var lastCategory = defaultCategory;
            var lastProfile = "default";
            var attemptedProfiles = new List<string> { "default" };
            foreach (var profile in retryPlan)
            {
                var client = ResolveRetryClient(profile);
                if (client is null)
                {
                    continue;
                }

                var profileName = GetTransportProfileName(profile);
                attemptedProfiles.Add(profileName);
                trace.Add($"web_page_fetch.transport_retry target={currentUri} profile={profileName} reason={SanitizeTraceValue(TransportExceptionClassifier.Summarize(lastException))}");
                try
                {
                    var response = await SendWithClientAsync(client, currentUri, ct).ConfigureAwait(false);
                    trace.Add($"web_page_fetch.transport_recovered target={currentUri} profile={profileName}");
                    return new TransportSendResult(
                        response,
                        new WebPageFetchDiagnostics(
                            AttemptCount: attemptedProfiles.Count,
                            RetryCount: attemptedProfiles.Count - 1,
                            TransportFailureObserved: true,
                            TransportRecovered: true,
                            RecoveryProfile: profileName,
                            AttemptProfiles: attemptedProfiles.ToArray()));
                }
                catch (Exception retryEx)
                {
                    lastException = retryEx;
                    lastCategory = TransportExceptionClassifier.Categorize(retryEx);
                    lastProfile = profileName;
                    trace.Add($"web_page_fetch.transport_retry_failed target={currentUri} profile={profileName} category={lastCategory} reason={SanitizeTraceValue(TransportExceptionClassifier.Summarize(retryEx))}");
                }
            }

            throw new TransportFetchException(
                new WebPageFetchDiagnostics(
                    AttemptCount: attemptedProfiles.Count,
                    RetryCount: attemptedProfiles.Count - 1,
                    TransportFailureObserved: true,
                    FinalFailureCategory: lastCategory,
                    FinalFailureProfile: lastProfile,
                    FinalFailureReason: TransportExceptionClassifier.Summarize(lastException ?? ex),
                    AttemptProfiles: attemptedProfiles.ToArray()),
                lastException ?? ex);
        }
    }

    private static Task<HttpResponseMessage> SendWithClientAsync(HttpClient client, Uri currentUri, CancellationToken ct)
    {
        return client.GetAsync(
            currentUri,
            HttpCompletionOption.ResponseHeadersRead,
            ct);
    }

    private HttpClient? ResolveRetryClient(TransportRetryProfile profile)
    {
        return profile switch
        {
            TransportRetryProfile.TlsCompatibility => _tlsCompatibilityHttpClient,
            TransportRetryProfile.ProxyBrowser => _proxyAwareHttpClient,
            TransportRetryProfile.ProxyTlsCompatibility => _proxyTlsCompatibilityHttpClient,
            _ => null
        };
    }

    private static string GetTransportProfileName(TransportRetryProfile profile)
    {
        return profile switch
        {
            TransportRetryProfile.TlsCompatibility => "tls_compatibility",
            TransportRetryProfile.ProxyBrowser => "proxy_browser",
            TransportRetryProfile.ProxyTlsCompatibility => "proxy_tls_compatibility",
            _ => "unknown"
        };
    }

    private enum TransportClientProfile
    {
        Default,
        TlsCompatibility,
        ProxyAware,
        ProxyTlsCompatibility
    }

    private sealed record TransportSendResult(
        HttpResponseMessage Response,
        WebPageFetchDiagnostics Diagnostics);

    private sealed class TransportFetchException : Exception
    {
        public TransportFetchException(WebPageFetchDiagnostics diagnostics, Exception innerException)
            : base(innerException.Message, innerException)
        {
            Diagnostics = diagnostics;
        }

        public WebPageFetchDiagnostics Diagnostics { get; }
    }
}
