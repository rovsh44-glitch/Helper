using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Authentication;
using Helper.Runtime.WebResearch;
using Helper.Runtime.WebResearch.Extraction;
using Helper.Runtime.WebResearch.Fetching;
using Helper.Runtime.WebResearch.Normalization;
using Helper.Runtime.WebResearch.Rendering;

namespace Helper.Runtime.Tests;

public sealed class WebPageFetcherTests
{
    [Fact]
    public void CreateDefaultHandler_DisablesProxyByDefault()
    {
        var handler = HttpFetchSupport.CreateDefaultHandler();

        Assert.False(handler.UseProxy);
    }

    [Fact]
    public void CreateTlsCompatibilityHandler_DisablesProxyAndUsesCompatibilityTlsProfile()
    {
        var handler = HttpFetchSupport.CreateTlsCompatibilityHandler();

        Assert.False(handler.UseProxy);
        Assert.Equal(SslProtocols.Tls12 | SslProtocols.Tls13, handler.SslOptions.EnabledSslProtocols);
    }

    [Fact]
    public void CreateProxyAwareHandler_UsesSystemProxyTransport()
    {
        var handler = HttpFetchSupport.CreateProxyAwareHandler();

        Assert.True(handler.UseProxy);
        Assert.NotNull(handler.Proxy);
    }

    [Fact]
    public async Task FetchAsync_FollowsRedirectAndExtractsHtml()
    {
        var fetcher = CreateFetcher(
            handler: new StubHttpMessageHandler(request =>
            {
                if (request.RequestUri!.AbsoluteUri.EndsWith("/start", StringComparison.Ordinal))
                {
                    var redirect = new HttpResponseMessage(HttpStatusCode.Redirect);
                    redirect.Headers.Location = new Uri("https://example.org/article");
                    return redirect;
                }

                var html = """
                    <html>
                    <head>
                      <title>Example article</title>
                      <link rel="canonical" href="https://example.org/article" />
                    </head>
                    <body>
                      <p>This fetched page contains enough substantive text for extraction and passage generation.</p>
                      <p>The second paragraph confirms that redirect handling preserved the final article body.</p>
                    </body>
                    </html>
                    """;
                return BuildResponse(HttpStatusCode.OK, "text/html; charset=utf-8", html);
            }));

        var result = await fetcher.FetchAsync("https://example.org/start", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("extracted", result.Outcome);
        Assert.Equal("https://example.org/article", result.ResolvedUrl);
        Assert.NotNull(result.ExtractedPage);
        Assert.Equal("https://example.org/article", result.ExtractedPage!.CanonicalUrl);
        Assert.Contains("second paragraph confirms", result.ExtractedPage.Body, StringComparison.Ordinal);
        Assert.Contains(result.Trace, line => line.Contains("web_fetch.redirect_allowed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FetchAsync_UsesRemoteDocumentExtractor_ForDirectPdfResponse()
    {
        var fetcher = CreateFetcher(
            handler: new StubHttpMessageHandler(_ => BuildResponse(HttpStatusCode.OK, "application/pdf", "%PDF-1.7")),
            securityPolicy: new AllowAllSecurityPolicy(),
            redirectGuard: new RedirectGuard(new AllowAllSecurityPolicy()),
            contentTypeAdmissionPolicy: new ContentTypeAdmissionPolicy(128_000),
            contentExtractor: new WebPageContentExtractor(),
            hardPageDetectionPolicy: null,
            renderedPageBudgetPolicy: null,
            browserRenderFallbackService: null,
            remoteDocumentExtractor: new StubRemoteDocumentExtractor());

        var result = await fetcher.FetchAsync("https://example.org/report.pdf", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("document_extracted_pdf", result.Outcome);
        Assert.NotNull(result.ExtractedPage);
        Assert.Equal("application/pdf", result.ExtractedPage!.ContentType);
        Assert.Contains(result.Trace, line => line.Contains("web_document_extract.stub", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FetchAsync_RoutesClinicalGuidanceHtml_ThroughDocumentLikeExtractor()
    {
        var html = """
            <html>
            <head><title>Клинические рекомендации по мигрени</title></head>
            <body>
              <article>
                <p>Критерии начала профилактической терапии включают частые приступы головной боли и выраженное снижение качества жизни.</p>
                <p>Цели профилактики состоят в уменьшении числа дней с мигренью, снижении интенсивности приступов и улучшении переносимости лечения.</p>
              </article>
            </body>
            </html>
            """;
        var fetcher = CreateFetcher(
            handler: new StubHttpMessageHandler(_ => BuildResponse(HttpStatusCode.OK, "text/html; charset=utf-8", html)));

        var result = await fetcher.FetchAsync("https://cr.minzdrav.gov.ru/recommendations/migraine", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("extracted", result.Outcome);
        Assert.NotNull(result.ExtractedPage);
        Assert.Contains(result.Trace, line => line.Contains("web_extract.route kind=clinical_guidance extractor=document_like_html", StringComparison.Ordinal));
        Assert.Contains("Критерии начала профилактической терапии", result.ExtractedPage!.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FetchAsync_RetriesWithTlsCompatibilityTransport_WhenPrimarySslHandshakeFails()
    {
        var primaryHandler = new StubHttpMessageHandler(_ =>
            throw new HttpRequestException(
                "The SSL connection could not be established, see inner exception.",
                new AuthenticationException("Handshake failed.")));
        var compatibilityHandler = new StubHttpMessageHandler(_ => BuildResponse(
            HttpStatusCode.OK,
            "text/html; charset=utf-8",
            "<html><head><title>Recovered article</title></head><body><p>Fallback TLS transport succeeded and returned substantive content.</p></body></html>"));
        var fetcher = CreateFetcher(
            handler: primaryHandler,
            hardPageDetectionPolicy: null,
            renderedPageBudgetPolicy: null,
            browserRenderFallbackService: null,
            documentSourceNormalizationPolicy: null,
            remoteDocumentExtractor: null,
            tlsCompatibilityHandler: compatibilityHandler);

        var result = await fetcher.FetchAsync("https://example.org/recovered", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("extracted", result.Outcome);
        Assert.NotNull(result.ExtractedPage);
        Assert.Contains("Fallback TLS transport succeeded", result.ExtractedPage!.Body, StringComparison.Ordinal);
        Assert.NotNull(result.Diagnostics);
        Assert.True(result.Diagnostics!.TransportFailureObserved);
        Assert.True(result.Diagnostics.TransportRecovered);
        Assert.Equal("tls_compatibility", result.Diagnostics.RecoveryProfile);
        Assert.Contains(result.Trace, line => line.Contains("web_page_fetch.transport_retry", StringComparison.Ordinal));
        Assert.Contains(result.Trace, line => line.Contains("web_page_fetch.transport_recovered", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FetchAsync_RetriesWithProxyTransport_WhenPrimaryConnectionIsRefused()
    {
        var primaryHandler = new StubHttpMessageHandler(_ =>
            throw new HttpRequestException(
                "No connection could be made because the target machine actively refused it.",
                new SocketException((int)SocketError.ConnectionRefused)));
        var proxyHandler = new StubHttpMessageHandler(_ => BuildResponse(
            HttpStatusCode.OK,
            "text/html; charset=utf-8",
            "<html><head><title>Proxy recovered article</title></head><body><p>Proxy fallback succeeded and returned stable article content.</p></body></html>"));
        var fetcher = CreateFetcher(
            handler: primaryHandler,
            hardPageDetectionPolicy: null,
            renderedPageBudgetPolicy: null,
            browserRenderFallbackService: null,
            documentSourceNormalizationPolicy: null,
            remoteDocumentExtractor: null,
            tlsCompatibilityHandler: null,
            proxyAwareHandler: proxyHandler);

        var result = await fetcher.FetchAsync("https://example.org/proxy-recovered", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("extracted", result.Outcome);
        Assert.NotNull(result.ExtractedPage);
        Assert.Contains("Proxy fallback succeeded", result.ExtractedPage!.Body, StringComparison.Ordinal);
        Assert.NotNull(result.Diagnostics);
        Assert.True(result.Diagnostics!.TransportFailureObserved);
        Assert.True(result.Diagnostics.TransportRecovered);
        Assert.Equal("proxy_browser", result.Diagnostics.RecoveryProfile);
        Assert.Contains(result.Trace, line => line.Contains("web_page_fetch.transport_retry target=https://example.org/proxy-recovered profile=proxy_browser", StringComparison.Ordinal));
        Assert.Contains(result.Trace, line => line.Contains("web_page_fetch.transport_recovered target=https://example.org/proxy-recovered profile=proxy_browser", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FetchAsync_RejectsInteractiveShellHtml_WhenExtractorLibraryFindsOnlyChrome()
    {
        const string html = """
            <html>
            <head><title>MoonshotAI Attention Residuals discussion · GitHub</title></head>
            <body>
              <nav>GitHub Advanced Security</nav>
              <div>Enterprise platform</div>
              <div>Saved searches</div>
              <div>Pull requests</div>
              <div>Marketplace</div>
            </body>
            </html>
            """;
        var fetcher = CreateFetcher(
            handler: new StubHttpMessageHandler(_ => BuildResponse(HttpStatusCode.OK, "text/html; charset=utf-8", html)));

        var result = await fetcher.FetchAsync("https://github.com/MoonshotAI/Attention-Residuals/discussions/1", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains(result.Outcome, new[] { "interactive_shell_contamination", "extraction_failed" });
        Assert.Contains(result.Trace, line => line.Contains("web_extract.route kind=interactive_shell extractor=interactive_shell_html", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FetchAsync_NormalizesGitHubBlobPdf_BeforeDocumentExtraction()
    {
        Uri? actualRequestUri = null;
        var fetcher = CreateFetcher(
            handler: new StubHttpMessageHandler(request =>
            {
                actualRequestUri = request.RequestUri;
                return BuildResponse(HttpStatusCode.OK, "application/pdf", "%PDF-1.7");
            }),
            securityPolicy: new AllowAllSecurityPolicy(),
            redirectGuard: new RedirectGuard(new AllowAllSecurityPolicy()),
            contentTypeAdmissionPolicy: new ContentTypeAdmissionPolicy(128_000),
            contentExtractor: new WebPageContentExtractor(),
            hardPageDetectionPolicy: null,
            renderedPageBudgetPolicy: null,
            browserRenderFallbackService: null,
            documentSourceNormalizationPolicy: new DocumentSourceNormalizationPolicy(),
            remoteDocumentExtractor: new StubRemoteDocumentExtractor());

        var result = await fetcher.FetchAsync("https://github.com/MoonshotAI/Attention-Residuals/blob/master/Attention_Residuals.pdf", CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(actualRequestUri);
        Assert.Equal("https://raw.githubusercontent.com/MoonshotAI/Attention-Residuals/master/Attention_Residuals.pdf", actualRequestUri!.AbsoluteUri);
        Assert.Contains(result.Trace, line => line.Contains("web_source.normalized=yes", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FetchAsync_ReturnsFailure_WhenExtractionCannotProduceBody()
    {
        var fetcher = CreateFetcher(
            handler: new StubHttpMessageHandler(_ => BuildResponse(
                HttpStatusCode.OK,
                "text/html",
                "<html><head><title>Empty</title></head><body><script>noop()</script></body></html>")));

        var result = await fetcher.FetchAsync("https://example.org/empty", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("extraction_failed", result.Outcome);
        Assert.Contains(result.Trace, line =>
            line.Contains("web_page_fetch.extraction_failed", StringComparison.Ordinal) ||
            line.Contains("web_page_fetch.extraction_rejected", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FetchAsync_UsesBrowserRenderFallback_WhenHardJsPageIsDetected()
    {
        var fetcher = CreateFetcher(
            handler: new StubHttpMessageHandler(_ => BuildResponse(
                HttpStatusCode.OK,
                "text/html; charset=utf-8",
                "<html><head><script src=\"/_next/static/chunk.js\"></script></head><body><div id=\"__next\">Loading...</div></body></html>")),
            hardPageDetectionPolicy: new HardPageDetectionPolicy(),
            renderedPageBudgetPolicy: new RenderedPageBudgetPolicy(),
            browserRenderFallbackService: new StubBrowserRenderFallbackService(BuildRenderedPage("https://example.org/app")));

        var result = await fetcher.FetchAsync(
            "https://example.org/app",
            new WebPageFetchContext(1, 1, AllowBrowserRenderFallback: true, RenderBudgetRemaining: 1),
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("rendered", result.Outcome);
        Assert.True(result.UsedBrowserRenderFallback);
        Assert.NotNull(result.ExtractedPage);
        Assert.Contains("rendered article body", result.ExtractedPage!.Body, StringComparison.Ordinal);
        Assert.Contains(result.Trace, line => line.Contains("browser_render.extracted", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FetchAsync_UsesPartialRead_ForOversizedHtmlWithinAdmissionPolicy()
    {
        const int partialReadBudget = 16_384;
        const string htmlPrefix = "<html><head><title>Large article</title></head><body><p>This large page still contains useful guidance in the leading portion of the document.</p><p>The early section should be enough for extraction.</p>";
        var html = htmlPrefix + new string('x', partialReadBudget + 2_048) + "</body></html>";
        var fetcher = CreateFetcher(
            handler: new StubHttpMessageHandler(_ => BuildResponse(HttpStatusCode.OK, "text/html; charset=utf-8", html)),
            contentTypeAdmissionPolicy: new ContentTypeAdmissionPolicy(partialReadBudget),
            maxResponseBytes: partialReadBudget);

        var result = await fetcher.FetchAsync("https://example.org/large-article", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("extracted", result.Outcome);
        Assert.NotNull(result.ExtractedPage);
        Assert.Contains("large page still contains useful guidance", result.ExtractedPage!.Body, StringComparison.Ordinal);
        Assert.True(
            result.Trace.Any(line => line.Contains("mode=partial_read", StringComparison.Ordinal)),
            string.Join(Environment.NewLine, result.Trace));
    }

    [Fact]
    public async Task FetchAsync_UsesPartialRead_ForOversizedHtml_WhenContentTypeIsMissing()
    {
        const int partialReadBudget = 16_384;
        var html = "<html><head><title>Fallback article</title></head><body><p>Leading section remains extractable even when the server omits content type.</p></body></html>" + new string('x', partialReadBudget + 2_048);
        var fetcher = CreateFetcher(
            handler: new StubHttpMessageHandler(_ =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(html)
                };
                response.Content.Headers.ContentType = null;
                return response;
            }),
            contentTypeAdmissionPolicy: new ContentTypeAdmissionPolicy(partialReadBudget),
            maxResponseBytes: partialReadBudget);

        var result = await fetcher.FetchAsync("https://example.org/missing-content-type", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("extracted", result.Outcome);
        Assert.NotNull(result.ExtractedPage);
        Assert.Contains("server omits content type", result.ExtractedPage!.Body, StringComparison.Ordinal);
        Assert.True(
            result.Trace.Any(line => line.Contains("content_length_over_budget_partial_read", StringComparison.Ordinal)),
            string.Join(Environment.NewLine, result.Trace));
    }

    [Fact]
    public async Task FetchAsync_UsesBrowserRenderRecovery_WhenTransportFailsForDocumentLikeUrl()
    {
        var fetcher = CreateFetcher(
            handler: new StubHttpMessageHandler(_ =>
                throw new HttpRequestException(
                    "No connection could be made because the target machine actively refused it.",
                    new SocketException((int)SocketError.ConnectionRefused))),
            hardPageDetectionPolicy: new HardPageDetectionPolicy(),
            renderedPageBudgetPolicy: new RenderedPageBudgetPolicy(),
            browserRenderFallbackService: new StubBrowserRenderFallbackService(BuildRenderedPage("https://pmc.ncbi.nlm.nih.gov/articles/PMC5167494")));

        var result = await fetcher.FetchAsync(
            "https://pmc.ncbi.nlm.nih.gov/articles/PMC5167494",
            new WebPageFetchContext(1, 2, AllowBrowserRenderFallback: true, RenderBudgetRemaining: 1),
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("rendered", result.Outcome);
        Assert.True(result.UsedBrowserRenderFallback);
        Assert.NotNull(result.ExtractedPage);
        Assert.NotNull(result.Diagnostics);
        Assert.True(result.Diagnostics!.TransportFailureObserved);
        Assert.Contains(result.Trace, line => line.Contains("web_page_fetch.render_recovery_attempt target=https://pmc.ncbi.nlm.nih.gov/articles/PMC5167494", StringComparison.Ordinal));
        Assert.Contains(result.Trace, line => line.Contains("web_page_fetch.render_recovery_succeeded target=https://pmc.ncbi.nlm.nih.gov/articles/PMC5167494", StringComparison.Ordinal));
        Assert.Contains(result.Trace, line => line.Contains("browser_render.extracted", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FetchAsync_UsesBrowserRenderRecovery_ForUnknownTransportFailure_OnDocumentLikeUrl()
    {
        var fetcher = CreateFetcher(
            handler: new StubHttpMessageHandler(_ => throw new HttpRequestException("Transport failed without classified inner exception.")),
            hardPageDetectionPolicy: new HardPageDetectionPolicy(),
            renderedPageBudgetPolicy: new RenderedPageBudgetPolicy(),
            browserRenderFallbackService: new StubBrowserRenderFallbackService(BuildRenderedPage("https://legalacts.ru/doc/sample-guideline")));

        var result = await fetcher.FetchAsync(
            "https://legalacts.ru/doc/sample-guideline",
            new WebPageFetchContext(1, 2, AllowBrowserRenderFallback: true, RenderBudgetRemaining: 1),
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("rendered", result.Outcome);
        Assert.True(result.UsedBrowserRenderFallback);
        Assert.NotNull(result.Diagnostics);
        Assert.Equal("unknown_transport", result.Diagnostics!.FinalFailureCategory);
        Assert.Contains(result.Trace, line => line.Contains("web_page_fetch.render_recovery_attempt target=https://legalacts.ru/doc/sample-guideline", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FetchAsync_SkipsBrowserRenderFallback_WhenBudgetIsExhausted()
    {
        var fetcher = CreateFetcher(
            handler: new StubHttpMessageHandler(_ => BuildResponse(
                HttpStatusCode.OK,
                "text/html; charset=utf-8",
                "<html><head><script src=\"/_next/static/chunk.js\"></script></head><body><div id=\"__next\">Loading...</div></body></html>")),
            hardPageDetectionPolicy: new HardPageDetectionPolicy(),
            renderedPageBudgetPolicy: new RenderedPageBudgetPolicy(),
            browserRenderFallbackService: new ThrowingBrowserRenderFallbackService());

        var result = await fetcher.FetchAsync(
            "https://example.org/app",
            new WebPageFetchContext(1, 1, AllowBrowserRenderFallback: true, RenderBudgetRemaining: 0),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("extraction_failed", result.Outcome);
        Assert.False(result.UsedBrowserRenderFallback);
        Assert.Contains(result.Trace, line => line.Contains("browser_render.budget allowed=no reason=budget_exhausted", StringComparison.Ordinal));
    }

    private static WebPageFetcher CreateFetcher(
        HttpMessageHandler handler,
        IWebFetchSecurityPolicy? securityPolicy = null,
        IRedirectGuard? redirectGuard = null,
        IContentTypeAdmissionPolicy? contentTypeAdmissionPolicy = null,
        IWebPageContentExtractor? contentExtractor = null,
        IHardPageDetectionPolicy? hardPageDetectionPolicy = null,
        IRenderedPageBudgetPolicy? renderedPageBudgetPolicy = null,
        IBrowserRenderFallbackService? browserRenderFallbackService = null,
        IDocumentSourceNormalizationPolicy? documentSourceNormalizationPolicy = null,
        IRemoteDocumentExtractor? remoteDocumentExtractor = null,
        HttpMessageHandler? tlsCompatibilityHandler = null,
        HttpMessageHandler? proxyAwareHandler = null,
        HttpMessageHandler? proxyTlsCompatibilityHandler = null,
        int? maxResponseBytes = null)
    {
        var effectiveSecurityPolicy = securityPolicy ?? new AllowAllSecurityPolicy();
        return new WebPageFetcher(
            effectiveSecurityPolicy,
            redirectGuard ?? new RedirectGuard(effectiveSecurityPolicy),
            contentTypeAdmissionPolicy ?? new ContentTypeAdmissionPolicy(128_000),
            contentExtractor ?? new WebPageContentExtractor(),
            hardPageDetectionPolicy ?? new HardPageDetectionPolicy(),
            renderedPageBudgetPolicy ?? new RenderedPageBudgetPolicy(),
            browserRenderFallbackService ?? new NoopBrowserRenderFallbackService(),
            documentSourceNormalizationPolicy ?? new DocumentSourceNormalizationPolicy(),
            remoteDocumentExtractor ?? new PassiveRemoteDocumentExtractor(),
            handler,
            tlsCompatibilityHandler,
            proxyAwareHandler,
            proxyTlsCompatibilityHandler,
            maxResponseBytes);
    }

    private static HttpResponseMessage BuildResponse(HttpStatusCode statusCode, string contentType, string content)
    {
        var response = new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(content)
        };
        response.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        return response;
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _resolver;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> resolver)
        {
            _resolver = resolver;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_resolver(request));
        }
    }

    private sealed class StubRemoteDocumentExtractor : IRemoteDocumentExtractor
    {
        public Task<RemoteDocumentExtractionResult> ExtractAsync(
            Uri requestedUri,
            Uri resolvedUri,
            string? contentType,
            byte[] contentBytes,
            CancellationToken ct = default)
        {
            return Task.FromResult(new RemoteDocumentExtractionResult(
                Handled: true,
                Success: true,
                Outcome: "document_extracted_pdf",
                ExtractedPage: new ExtractedWebPage(
                    requestedUri.AbsoluteUri,
                    resolvedUri.AbsoluteUri,
                    resolvedUri.AbsoluteUri,
                    "Attention Residuals",
                    "2026",
                    "This remote document proposes attention residuals as a replacement for fixed residual accumulation.",
                    new[]
                    {
                        new ExtractedWebPassage(1, "This remote document proposes attention residuals as a replacement for fixed residual accumulation.")
                    },
                    "application/pdf"),
                Trace: new[] { $"web_document_extract.stub requested={requestedUri} resolved={resolvedUri}" }));
        }
    }

    private sealed class PassiveRemoteDocumentExtractor : IRemoteDocumentExtractor
    {
        public Task<RemoteDocumentExtractionResult> ExtractAsync(
            Uri requestedUri,
            Uri resolvedUri,
            string? contentType,
            byte[] contentBytes,
            CancellationToken ct = default)
        {
            return Task.FromResult(new RemoteDocumentExtractionResult(false, false, "not_handled", null, Array.Empty<string>()));
        }
    }

    private sealed class AllowAllSecurityPolicy : IWebFetchSecurityPolicy
    {
        public Task<WebFetchSecurityDecision> EvaluateAsync(
            Uri targetUri,
            WebFetchTargetKind targetKind,
            bool allowTrustedLoopback = false,
            CancellationToken ct = default)
        {
            return Task.FromResult(new WebFetchSecurityDecision(
                true,
                "allowed",
                new[] { $"web_fetch.allowed target={targetUri}", $"web_fetch.kind={targetKind.ToString().ToLowerInvariant()}" }));
        }
    }

    private static ExtractedWebPage BuildRenderedPage(string url)
    {
        return new ExtractedWebPage(
            RequestedUrl: url,
            ResolvedUrl: url,
            CanonicalUrl: url,
            Title: "Rendered app article",
            PublishedAt: "2026-03-21",
            Body: "This rendered article body contains enough substantive text for extraction and downstream grounding after browser fallback.",
            Passages: new[]
            {
                new ExtractedWebPassage(1, "This rendered article body contains enough substantive text for extraction and downstream grounding after browser fallback.")
            },
            ContentType: "text/html");
    }

    private sealed class StubBrowserRenderFallbackService : IBrowserRenderFallbackService
    {
        private readonly ExtractedWebPage _page;

        public StubBrowserRenderFallbackService(ExtractedWebPage page)
        {
            _page = page;
        }

        public Task<BrowserRenderFallbackResult> TryRenderAsync(Uri requestedUri, RenderFallbackBudgetDecision budget, CancellationToken ct = default)
        {
            return Task.FromResult(new BrowserRenderFallbackResult(
                true,
                "rendered",
                _page,
                new[] { $"browser_render.extracted target={requestedUri} canonical={_page.CanonicalUrl} passages={_page.Passages.Count}" }));
        }
    }

    private sealed class ThrowingBrowserRenderFallbackService : IBrowserRenderFallbackService
    {
        public Task<BrowserRenderFallbackResult> TryRenderAsync(Uri requestedUri, RenderFallbackBudgetDecision budget, CancellationToken ct = default)
        {
            throw new InvalidOperationException("Render fallback should not be invoked when budget is exhausted.");
        }
    }

    private sealed class NoopBrowserRenderFallbackService : IBrowserRenderFallbackService
    {
        public Task<BrowserRenderFallbackResult> TryRenderAsync(Uri requestedUri, RenderFallbackBudgetDecision budget, CancellationToken ct = default)
        {
            return Task.FromResult(new BrowserRenderFallbackResult(false, "not_rendered", null, Array.Empty<string>()));
        }
    }
}

