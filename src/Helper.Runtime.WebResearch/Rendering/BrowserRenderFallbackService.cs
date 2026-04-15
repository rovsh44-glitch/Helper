using System.Net;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Helper.Runtime.WebResearch.Extraction;
using Helper.Runtime.WebResearch.Fetching;

namespace Helper.Runtime.WebResearch.Rendering;

internal interface IBrowserRenderHost
{
    Task<BrowserRenderHostResult> RenderAsync(
        Uri requestedUri,
        BrowserRenderHostOptions options,
        CancellationToken ct = default);
}

internal sealed record BrowserRenderHostOptions(
    TimeSpan Timeout,
    int MaxHtmlChars);

internal sealed record BrowserRenderHostResult(
    bool Success,
    string Outcome,
    string? ResolvedUrl,
    string? ContentType,
    string? Html,
    IReadOnlyList<string> Trace);

public sealed class BrowserRenderFallbackService : IBrowserRenderFallbackService
{
    private readonly IWebFetchSecurityPolicy _securityPolicy;
    private readonly IWebPageContentExtractor _contentExtractor;
    private readonly IBrowserRenderHost _browserRenderHost;

    public BrowserRenderFallbackService(
        IWebFetchSecurityPolicy securityPolicy,
        IWebPageContentExtractor contentExtractor)
        : this(
            securityPolicy,
            contentExtractor,
            new DirectFetchBrowserRenderHost())
    {
    }

    internal BrowserRenderFallbackService(
        IWebFetchSecurityPolicy securityPolicy,
        IWebPageContentExtractor contentExtractor,
        IBrowserRenderHost browserRenderHost)
    {
        _securityPolicy = securityPolicy;
        _contentExtractor = contentExtractor;
        _browserRenderHost = browserRenderHost;
    }

    public async Task<BrowserRenderFallbackResult> TryRenderAsync(
        Uri requestedUri,
        RenderFallbackBudgetDecision budget,
        CancellationToken ct = default)
    {
        var trace = new List<string>();
        trace.AddRange(budget.Trace);
        if (!budget.Allowed)
        {
            return Failure("budget_denied", trace);
        }

        var securityDecision = await _securityPolicy.EvaluateAsync(
            requestedUri,
            WebFetchTargetKind.PageFetch,
            allowTrustedLoopback: false,
            ct).ConfigureAwait(false);
        trace.AddRange(securityDecision.Trace.Select(static line => $"browser_render.{line}"));
        if (!securityDecision.Allowed)
        {
            return Failure("blocked", trace);
        }

        BrowserRenderHostResult hostResult;
        try
        {
            hostResult = await _browserRenderHost.RenderAsync(
                requestedUri,
                new BrowserRenderHostOptions(budget.Timeout, budget.MaxHtmlChars),
                ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            trace.Add($"browser_render.failure category=browser_render_timeout reason=timeout_after_{(int)budget.Timeout.TotalMilliseconds}ms");
            trace.Add($"browser_render.unavailable category=browser_render_timeout reason=timeout_after_{(int)budget.Timeout.TotalMilliseconds}ms");
            return Failure("browser_render_timeout", trace);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var failure = BrowserRenderFailureClassifier.Classify(ex);
            trace.Add($"browser_render.failure category={failure.Category} reason={Sanitize(failure.Reason)}");
            trace.Add($"browser_render.unavailable category={failure.Category} reason={Sanitize(failure.Reason)}");
            return Failure(failure.Outcome, trace);
        }

        trace.AddRange(hostResult.Trace);
        if (!hostResult.Success || string.IsNullOrWhiteSpace(hostResult.Html))
        {
            return Failure(hostResult.Outcome, trace);
        }

        var resolvedUri = Uri.TryCreate(hostResult.ResolvedUrl, UriKind.Absolute, out var parsedResolvedUri)
            ? parsedResolvedUri
            : requestedUri;
        var extractedPage = _contentExtractor.Extract(
            requestedUri,
            resolvedUri,
            hostResult.ContentType ?? "text/html",
            hostResult.Html);
        if (extractedPage is null)
        {
            trace.Add($"browser_render.extraction_failed target={resolvedUri}");
            return Failure("extraction_failed", trace);
        }

        trace.Add($"browser_render.extracted target={resolvedUri} canonical={extractedPage.CanonicalUrl} passages={extractedPage.Passages.Count}");
        return new BrowserRenderFallbackResult(
            true,
            "rendered",
            extractedPage,
            trace);
    }

    private static BrowserRenderFallbackResult Failure(string outcome, IReadOnlyList<string> trace)
    {
        return new BrowserRenderFallbackResult(false, outcome, null, trace);
    }

    private static string Sanitize(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return "unknown";
        }

        return message
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
    }
}

internal sealed class UnavailableBrowserRenderHost : IBrowserRenderHost
{
    public async Task<BrowserRenderHostResult> RenderAsync(
        Uri requestedUri,
        BrowserRenderHostOptions options,
        CancellationToken ct = default)
    {
        await Task.Yield();

        return new BrowserRenderHostResult(
            false,
            "browser_render_unavailable",
            requestedUri.AbsoluteUri,
            null,
            null,
            new[]
            {
                $"browser_render.unavailable target={requestedUri} reason=browser_runtime_not_configured timeout_ms={(int)options.Timeout.TotalMilliseconds}"
            });
    }
}

internal sealed class DirectFetchBrowserRenderHost : IBrowserRenderHost, IDisposable
{
    private readonly HttpClient _client;

    public DirectFetchBrowserRenderHost()
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
            UseProxy = false,
            UseCookies = false,
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1),
            ConnectTimeout = TimeSpan.FromSeconds(10)
        };
        handler.SslOptions.EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13;
        handler.SslOptions.CertificateRevocationCheckMode = X509RevocationMode.NoCheck;

        _client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
        _client.DefaultRequestVersion = HttpVersion.Version11;
        _client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
        _client.DefaultRequestHeaders.ConnectionClose = true;
        HttpFetchSupport.ApplyBrowserLikeDefaults(_client, prefersDocuments: true);
    }

    public async Task<BrowserRenderHostResult> RenderAsync(
        Uri requestedUri,
        BrowserRenderHostOptions options,
        CancellationToken ct = default)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linkedCts.CancelAfter(options.Timeout);

        using var request = new HttpRequestMessage(HttpMethod.Get, requestedUri);
        using var response = await _client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            linkedCts.Token).ConfigureAwait(false);

        var contentType = response.Content.Headers.ContentType?.ToString() ?? "text/html";
        var resolvedUrl = response.RequestMessage?.RequestUri?.AbsoluteUri ?? requestedUri.AbsoluteUri;
        if (!response.IsSuccessStatusCode)
        {
            return new BrowserRenderHostResult(
                false,
                $"http_{(int)response.StatusCode}",
                resolvedUrl,
                contentType,
                null,
                new[]
                {
                    $"browser_render.direct_fetch_failed target={requestedUri} status={(int)response.StatusCode}",
                    $"browser_render.unavailable target={requestedUri} reason=http_status_{(int)response.StatusCode}"
                });
        }

        var readResult = await HttpFetchSupport.ReadBytesWithinBudgetAsync(
            response.Content,
            options.MaxHtmlChars,
            linkedCts.Token).ConfigureAwait(false);
        var html = WebSourceTypeExtractionSupport.DecodeIfTextLike(
            readResult.Bytes,
            contentType,
            response.Content.Headers.ContentType?.CharSet);
        if (string.IsNullOrWhiteSpace(html))
        {
            return new BrowserRenderHostResult(
                false,
                "empty_html",
                resolvedUrl,
                contentType,
                null,
                new[]
                {
                    $"browser_render.direct_fetch_failed target={requestedUri} reason=empty_html"
                });
        }

        var trace = new List<string>
        {
            $"browser_render.direct_fetch_succeeded target={requestedUri} resolved={resolvedUrl} content_type={contentType}"
        };
        if (readResult.Truncated)
        {
            trace.Add($"browser_render.direct_fetch_truncated target={requestedUri} max_html_chars={options.MaxHtmlChars}");
        }

        trace.Add($"browser_render.completed target={resolvedUrl} mode=direct_html_fetch");
        return new BrowserRenderHostResult(
            true,
            "direct_html_fetch",
            resolvedUrl,
            contentType,
            html,
            trace);
    }

    public void Dispose()
    {
        _client.Dispose();
    }
}

