using Helper.Runtime.WebResearch.Extraction;
using Helper.Runtime.WebResearch.Normalization;
using Helper.Runtime.WebResearch.Rendering;

namespace Helper.Runtime.WebResearch.Fetching;

public sealed partial class WebPageFetcher : IWebPageFetcher
{
    private readonly HttpClient _httpClient;
    private readonly IWebFetchSecurityPolicy _securityPolicy;
    private readonly IRedirectGuard _redirectGuard;
    private readonly IContentTypeAdmissionPolicy _contentTypeAdmissionPolicy;
    private readonly IDocumentSourceNormalizationPolicy _documentSourceNormalizationPolicy;
    private readonly IHardPageDetectionPolicy _hardPageDetectionPolicy;
    private readonly IRenderedPageBudgetPolicy _renderedPageBudgetPolicy;
    private readonly IBrowserRenderFallbackService _browserRenderFallbackService;
    private readonly WebSourceTypeExtractorLibrary _sourceTypeExtractorLibrary;
    private readonly int _maxResponseBytes;
    private readonly HttpClient? _tlsCompatibilityHttpClient;
    private readonly HttpClient? _proxyAwareHttpClient;
    private readonly HttpClient? _proxyTlsCompatibilityHttpClient;
    private readonly bool _proxyAwareClientExplicit;
    private readonly bool _proxyTlsCompatibilityClientExplicit;

    public WebPageFetcher(
        IWebFetchSecurityPolicy securityPolicy,
        IRedirectGuard redirectGuard,
        IContentTypeAdmissionPolicy contentTypeAdmissionPolicy,
        IWebPageContentExtractor contentExtractor,
        IHardPageDetectionPolicy hardPageDetectionPolicy,
        IRenderedPageBudgetPolicy renderedPageBudgetPolicy,
        IBrowserRenderFallbackService browserRenderFallbackService,
        IDocumentSourceNormalizationPolicy documentSourceNormalizationPolicy,
        IRemoteDocumentExtractor remoteDocumentExtractor,
        HttpMessageHandler? handler = null,
        HttpMessageHandler? tlsCompatibilityHandler = null,
        HttpMessageHandler? proxyAwareHandler = null,
        HttpMessageHandler? proxyTlsCompatibilityHandler = null,
        int? maxResponseBytes = null)
    {
        ArgumentNullException.ThrowIfNull(securityPolicy);
        ArgumentNullException.ThrowIfNull(redirectGuard);
        ArgumentNullException.ThrowIfNull(contentTypeAdmissionPolicy);
        ArgumentNullException.ThrowIfNull(contentExtractor);
        ArgumentNullException.ThrowIfNull(hardPageDetectionPolicy);
        ArgumentNullException.ThrowIfNull(renderedPageBudgetPolicy);
        ArgumentNullException.ThrowIfNull(browserRenderFallbackService);
        ArgumentNullException.ThrowIfNull(documentSourceNormalizationPolicy);
        ArgumentNullException.ThrowIfNull(remoteDocumentExtractor);

        _securityPolicy = securityPolicy;
        _redirectGuard = redirectGuard;
        _contentTypeAdmissionPolicy = contentTypeAdmissionPolicy;
        _documentSourceNormalizationPolicy = documentSourceNormalizationPolicy;
        _hardPageDetectionPolicy = hardPageDetectionPolicy;
        _renderedPageBudgetPolicy = renderedPageBudgetPolicy;
        _browserRenderFallbackService = browserRenderFallbackService;
        _sourceTypeExtractorLibrary = new WebSourceTypeExtractorLibrary(remoteDocumentExtractor, contentExtractor);
        _maxResponseBytes = maxResponseBytes is int explicitMaxResponseBytes
            ? Math.Clamp(explicitMaxResponseBytes, 16_384, 2_000_000)
            : WebPageFetchSettings.ReadMaxResponseBytes();
        _httpClient = CreateHttpClient(handler, TransportClientProfile.Default);
        _tlsCompatibilityHttpClient = handler is null || tlsCompatibilityHandler is not null
            ? CreateHttpClient(tlsCompatibilityHandler, TransportClientProfile.TlsCompatibility)
            : null;
        _proxyAwareHttpClient = handler is null || proxyAwareHandler is not null
            ? CreateHttpClient(proxyAwareHandler, TransportClientProfile.ProxyAware)
            : null;
        _proxyTlsCompatibilityHttpClient = handler is null || proxyTlsCompatibilityHandler is not null
            ? CreateHttpClient(proxyTlsCompatibilityHandler, TransportClientProfile.ProxyTlsCompatibility)
            : null;
        _proxyAwareClientExplicit = proxyAwareHandler is not null;
        _proxyTlsCompatibilityClientExplicit = proxyTlsCompatibilityHandler is not null;
    }

    public async Task<WebPageFetchResult> FetchAsync(string url, CancellationToken ct = default)
    {
        return await FetchAsync(url, WebPageFetchContext.Default, ct).ConfigureAwait(false);
    }

    public async Task<WebPageFetchResult> FetchAsync(string url, WebPageFetchContext context, CancellationToken ct = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var requestUri))
        {
            return Failure(
                url,
                resolvedUrl: null,
                outcome: "invalid_url",
                new[] { $"web_page_fetch.blocked reason=invalid_url target={url}" });
        }

        var normalization = _documentSourceNormalizationPolicy.Normalize(requestUri);
        var effectiveRequestUri = normalization.EffectiveUri;
        var securityDecision = await _securityPolicy.EvaluateAsync(
            effectiveRequestUri,
            WebFetchTargetKind.PageFetch,
            allowTrustedLoopback: false,
            ct).ConfigureAwait(false);
        if (!securityDecision.Allowed)
        {
            return Failure(url, effectiveRequestUri.AbsoluteUri, "blocked", normalization.Trace.Concat(securityDecision.Trace).ToArray());
        }

        var trace = new List<string>
        {
            $"web_page_fetch.attempt target={effectiveRequestUri}"
        };
        trace.AddRange(normalization.Trace);
        trace.AddRange(securityDecision.Trace);

        var currentUri = effectiveRequestUri;
        var redirectHop = 0;

        try
        {
            while (true)
            {
                var transportResult = await SendAsyncWithTransportFallback(currentUri, trace, ct).ConfigureAwait(false);
                var fetchDiagnostics = transportResult.Diagnostics;
                using var response = transportResult.Response;

                if (HttpFetchSupport.IsRedirect(response.StatusCode))
                {
                    var location = response.Headers.Location;
                    if (location is null)
                    {
                        trace.Add($"web_page_fetch.redirect_missing_location from={currentUri}");
                        return Failure(url, currentUri.AbsoluteUri, "redirect_missing_location", trace);
                    }

                    var redirectUri = location.IsAbsoluteUri
                        ? location
                        : new Uri(currentUri, location);
                    redirectHop++;
                    var redirectDecision = await _redirectGuard.EvaluateAsync(
                        currentUri,
                        redirectUri,
                        redirectHop,
                        allowTrustedLoopback: false,
                        ct).ConfigureAwait(false);
                    trace.AddRange(redirectDecision.Trace);
                    if (!redirectDecision.Allowed)
                    {
                        return Failure(url, currentUri.AbsoluteUri, "redirect_blocked", trace);
                    }

                    currentUri = redirectUri;
                    continue;
                }

                response.EnsureSuccessStatusCode();
                var contentType = response.Content.Headers.ContentType?.ToString();
                var admissionDecision = _contentTypeAdmissionPolicy.Evaluate(
                    currentUri,
                    contentType,
                    response.Content.Headers.ContentLength);
                trace.AddRange(admissionDecision.Trace);
                if (!admissionDecision.Allowed)
                {
                    return Failure(url, currentUri.AbsoluteUri, admissionDecision.ReasonCode, trace);
                }

                var normalizedContentType = WebSourceTypeExtractionSupport.NormalizeMediaType(contentType);
                var sourceType = WebSourceTypeClassifier.Classify(requestUri, currentUri, normalizedContentType);
                trace.Add($"web_page_fetch.source_type={sourceType.Kind} target={currentUri}");

                var readResult = await HttpFetchSupport.ReadBytesWithinBudgetAsync(
                    response.Content,
                    _maxResponseBytes,
                    ct).ConfigureAwait(false);

                if (readResult.Truncated)
                {
                    trace.Add($"web_page_fetch.content_length_exceeded target={currentUri} max_bytes={_maxResponseBytes} mode=partial_read");
                }

                var bytes = readResult.Bytes;
                var decodedContent = WebSourceTypeExtractionSupport.DecodeIfTextLike(
                    bytes,
                    normalizedContentType,
                    response.Content.Headers.ContentType?.CharSet);
                var extractionResult = await _sourceTypeExtractorLibrary.ExtractAsync(
                    new WebSourceTypeExtractionRequest(
                        requestUri,
                        currentUri,
                        contentType,
                        normalizedContentType,
                        bytes,
                        decodedContent,
                        sourceType),
                    ct).ConfigureAwait(false);
                trace.AddRange(extractionResult.Trace);

                var extractedPage = extractionResult.ExtractedPage;
                var hardPageDecision = _hardPageDetectionPolicy.Evaluate(
                    requestUri,
                    currentUri,
                    contentType,
                    decodedContent ?? string.Empty,
                    extractedPage);
                trace.AddRange(hardPageDecision.Trace);
                if (hardPageDecision.IsHardPage)
                {
                    var renderBudgetDecision = _renderedPageBudgetPolicy.Evaluate(context, hardPageDecision);
                    trace.AddRange(renderBudgetDecision.Trace);
                    if (renderBudgetDecision.Allowed)
                    {
                        var renderResult = await _browserRenderFallbackService.TryRenderAsync(
                            currentUri,
                            renderBudgetDecision,
                            ct).ConfigureAwait(false);
                        trace.AddRange(renderResult.Trace);
                        if (renderResult.Success && renderResult.Page is not null)
                        {
                            return new WebPageFetchResult(
                                url,
                                renderResult.Page.ResolvedUrl,
                                Success: true,
                                Outcome: "rendered",
                                ExtractedPage: renderResult.Page,
                                Trace: trace,
                                UsedBrowserRenderFallback: true,
                                Diagnostics: fetchDiagnostics);
                        }
                    }
                }

                if (extractionResult.Handled)
                {
                    if (!extractionResult.Success || extractedPage is null)
                    {
                        trace.Add($"web_page_fetch.extraction_rejected target={currentUri} outcome={extractionResult.Outcome}");
                        return Failure(url, currentUri.AbsoluteUri, extractionResult.Outcome, trace);
                    }

                    trace.Add($"web_page_fetch.extracted target={currentUri} canonical={extractedPage.CanonicalUrl} passages={extractedPage.Passages.Count} extractor={extractionResult.ExtractorId}");
                    if (!string.IsNullOrWhiteSpace(extractedPage.PublishedAt))
                    {
                        trace.Add($"web_page_fetch.published_at={extractedPage.PublishedAt}");
                    }

                    return new WebPageFetchResult(
                        url,
                        currentUri.AbsoluteUri,
                        Success: true,
                        Outcome: extractionResult.Outcome,
                        ExtractedPage: extractedPage,
                        Trace: trace,
                        UsedBrowserRenderFallback: false,
                        Diagnostics: fetchDiagnostics);
                }

                if (extractedPage is null)
                {
                    trace.Add($"web_page_fetch.extraction_failed target={currentUri}");
                    return Failure(url, currentUri.AbsoluteUri, "extraction_failed", trace);
                }

                trace.Add($"web_page_fetch.extracted target={currentUri} canonical={extractedPage.CanonicalUrl} passages={extractedPage.Passages.Count}");
                if (!string.IsNullOrWhiteSpace(extractedPage.PublishedAt))
                {
                    trace.Add($"web_page_fetch.published_at={extractedPage.PublishedAt}");
                }

                return new WebPageFetchResult(
                    url,
                    currentUri.AbsoluteUri,
                    Success: true,
                    Outcome: "extracted",
                    ExtractedPage: extractedPage,
                    Trace: trace,
                    UsedBrowserRenderFallback: false,
                    Diagnostics: fetchDiagnostics);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            trace.Add($"web_page_fetch.timeout target={currentUri}");
            return Failure(url, currentUri.AbsoluteUri, "timeout", trace);
        }
        catch (TransportFetchException ex)
        {
            var renderRecovery = await TryRecoverFromTransportFailureWithBrowserRenderAsync(
                requestUri,
                currentUri,
                context,
                ex.Diagnostics,
                trace,
                ct).ConfigureAwait(false);
            if (renderRecovery is not null)
            {
                return renderRecovery;
            }

            trace.Add($"web_page_fetch.error target={currentUri} type={ex.InnerException?.GetType().Name ?? ex.GetType().Name} message={SanitizeTraceValue(ex.Diagnostics.FinalFailureReason ?? ex.Message)}");
            return Failure(url, currentUri.AbsoluteUri, "error", trace, ex.Diagnostics);
        }
        catch (Exception ex)
        {
            trace.Add($"web_page_fetch.error target={currentUri} type={ex.GetType().Name} message={SanitizeTraceValue(ex.Message)}");
            return Failure(
                url,
                currentUri.AbsoluteUri,
                "error",
                trace,
                new WebPageFetchDiagnostics(
                    AttemptCount: 1,
                    RetryCount: 0,
                    TransportFailureObserved: ex is HttpRequestException,
                    FinalFailureCategory: ex is HttpRequestException ? TransportExceptionClassifier.Categorize(ex) : null,
                    FinalFailureProfile: "default",
                    FinalFailureReason: TransportExceptionClassifier.Summarize(ex),
                    AttemptProfiles: new[] { "default" }));
        }
    }

}

