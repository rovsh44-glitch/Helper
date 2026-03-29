using System.Text.Json;
using Helper.Runtime.WebResearch.Fetching;

namespace Helper.Runtime.WebResearch.Providers;

public abstract class JsonSearchProviderBase : IWebSearchProvider
{
    private readonly string _baseUrl;
    private readonly HttpClient _httpClient;
    private readonly IWebFetchSecurityPolicy _securityPolicy;
    private readonly IRedirectGuard _redirectGuard;
    private readonly bool _allowTrustedLoopback;

    protected JsonSearchProviderBase(
        string providerId,
        string baseUrl,
        TimeSpan timeout,
        IWebFetchSecurityPolicy? securityPolicy = null,
        IRedirectGuard? redirectGuard = null,
        bool allowTrustedLoopback = false)
    {
        ProviderId = providerId;
        _baseUrl = NormalizeBaseUrl(baseUrl);
        _securityPolicy = securityPolicy ?? new WebFetchSecurityPolicy();
        _redirectGuard = redirectGuard ?? new RedirectGuard(_securityPolicy);
        _allowTrustedLoopback = allowTrustedLoopback;
        _httpClient = new HttpClient(HttpFetchSupport.CreateDefaultHandler())
        {
            Timeout = timeout
        };
        HttpFetchSupport.ApplyBrowserLikeDefaults(_httpClient);
    }

    public string ProviderId { get; }

    public bool IsEnabled => !string.IsNullOrWhiteSpace(_baseUrl);

    protected string BaseUrl => _baseUrl;

    public async Task<WebSearchProviderClientResponse> SearchAsync(WebSearchPlan plan, CancellationToken ct = default)
    {
        if (!IsEnabled || string.IsNullOrWhiteSpace(plan.Query))
        {
            return Empty("provider_disabled_or_empty_query");
        }

        var requestUri = BuildSearchUri(plan);
        var securityDecision = await _securityPolicy.EvaluateAsync(
            requestUri,
            WebFetchTargetKind.SearchProvider,
            _allowTrustedLoopback,
            ct).ConfigureAwait(false);
        if (!securityDecision.Allowed)
        {
            return new WebSearchProviderClientResponse(Array.Empty<WebSearchDocument>(), Trace("blocked", requestUri, securityDecision.Trace));
        }

        var trace = new List<string>(Trace("attempt", requestUri, securityDecision.Trace));
        var currentUri = requestUri;
        var redirectHop = 0;

        try
        {
            while (true)
            {
                var response = await _httpClient.GetAsync(currentUri, ct).ConfigureAwait(false);
                if (HttpFetchSupport.IsRedirect(response.StatusCode))
                {
                    var location = response.Headers.Location;
                    if (location is null)
                    {
                        trace.Add($"{ProviderId}:redirect_missing_location uri={currentUri}");
                        return new WebSearchProviderClientResponse(Array.Empty<WebSearchDocument>(), trace);
                    }

                    var redirectUri = location.IsAbsoluteUri
                        ? location
                        : new Uri(currentUri, location);
                    redirectHop++;
                    var redirectDecision = await _redirectGuard.EvaluateAsync(
                        currentUri,
                        redirectUri,
                        redirectHop,
                        _allowTrustedLoopback,
                        ct).ConfigureAwait(false);
                    trace.AddRange(redirectDecision.Trace.Select(message => $"{ProviderId}:{message}"));
                    if (!redirectDecision.Allowed)
                    {
                        return new WebSearchProviderClientResponse(Array.Empty<WebSearchDocument>(), trace);
                    }

                    currentUri = redirectUri;
                    continue;
                }

                response.EnsureSuccessStatusCode();
                var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                var documents = ParseDocuments(json, plan.MaxResults);
                trace.Add($"{ProviderId}:{(documents.Count > 0 ? "results" : "empty")} uri={currentUri} count={documents.Count}");
                return new WebSearchProviderClientResponse(documents, trace);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            trace.Add($"{ProviderId}:timeout uri={currentUri}");
            return new WebSearchProviderClientResponse(Array.Empty<WebSearchDocument>(), trace);
        }
        catch
        {
            trace.Add($"{ProviderId}:error uri={currentUri}");
            return new WebSearchProviderClientResponse(Array.Empty<WebSearchDocument>(), trace);
        }
    }

    protected virtual Uri BuildSearchUri(WebSearchPlan plan)
    {
        return new Uri($"{_baseUrl}/search?q={Uri.EscapeDataString(plan.Query)}&format=json");
    }

    private static IReadOnlyList<WebSearchDocument> ParseDocuments(string json, int maxResults)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("results", out var results))
        {
            return Array.Empty<WebSearchDocument>();
        }

        return results
            .EnumerateArray()
            .Take(maxResults)
            .Select(static item => new WebSearchDocument(
                item.TryGetProperty("url", out var url) ? (url.GetString() ?? string.Empty) : string.Empty,
                item.TryGetProperty("title", out var title) ? (title.GetString() ?? string.Empty) : string.Empty,
                item.TryGetProperty("content", out var content) ? (content.GetString() ?? string.Empty) : string.Empty))
            .Where(static item => !string.IsNullOrWhiteSpace(item.Url))
            .ToArray();
    }

    private static string NormalizeBaseUrl(string baseUrl)
    {
        return string.IsNullOrWhiteSpace(baseUrl)
            ? string.Empty
            : baseUrl.Trim().TrimEnd('/');
    }
    private WebSearchProviderClientResponse Empty(string reason)
    {
        return new WebSearchProviderClientResponse(
            Array.Empty<WebSearchDocument>(),
            new[] { $"{ProviderId}:{reason}" });
    }

    private IReadOnlyList<string> Trace(string state, Uri uri, IEnumerable<string>? decisionTrace = null)
    {
        var trace = new List<string>
        {
            $"{ProviderId}:{state} uri={uri}"
        };

        if (decisionTrace is not null)
        {
            trace.AddRange(decisionTrace.Select(message => $"{ProviderId}:{message}"));
        }

        return trace.ToArray();
    }
}

