using System.Net;
using System.Net.Http.Headers;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace Helper.Runtime.WebResearch.Fetching;

internal static class HttpFetchSupport
{
    private const string DefaultUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/136.0.0.0 Safari/537.36 HELPER/1.0";
    private static readonly string[] RenderablePathMarkers =
    {
        "/article/",
        "/articles/",
        "/paper",
        "/papers/",
        "/journal/",
        "/abstract/",
        "/recommendation",
        "/recommendations/",
        "/guideline",
        "/guidelines/",
        "/healthy-aging",
        "/news/",
        "/post/",
        "/study/",
        "/studies/"
    };

    public static bool IsRedirect(System.Net.HttpStatusCode statusCode)
    {
        var code = (int)statusCode;
        return code is 301 or 302 or 303 or 307 or 308;
    }

    public static HttpClientHandler CreateDefaultHandler()
    {
        var useProxy = ReadBooleanEnvironmentVariable("HELPER_WEB_FETCH_USE_PROXY", defaultValue: false);
        return new HttpClientHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
            UseProxy = useProxy,
            Proxy = useProxy ? WebRequest.GetSystemWebProxy() : null,
            CheckCertificateRevocationList = true,
            SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
        };
    }

    public static HttpClientHandler CreateProxyAwareHandler()
    {
        return new HttpClientHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
            UseProxy = true,
            Proxy = WebRequest.GetSystemWebProxy(),
            CheckCertificateRevocationList = true,
            SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
        };
    }

    public static SocketsHttpHandler CreateTlsCompatibilityHandler()
    {
        var useProxy = ReadBooleanEnvironmentVariable("HELPER_WEB_FETCH_USE_PROXY", defaultValue: false);
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
            UseProxy = useProxy,
            Proxy = useProxy ? WebRequest.GetSystemWebProxy() : null,
            UseCookies = false,
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1),
            ConnectTimeout = TimeSpan.FromSeconds(10)
        };
        handler.SslOptions.EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13;
        handler.SslOptions.CertificateRevocationCheckMode = X509RevocationMode.NoCheck;
        return handler;
    }

    public static SocketsHttpHandler CreateProxyTlsCompatibilityHandler()
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
            UseProxy = true,
            Proxy = WebRequest.GetSystemWebProxy(),
            UseCookies = false,
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1),
            ConnectTimeout = TimeSpan.FromSeconds(10)
        };
        handler.SslOptions.EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13;
        handler.SslOptions.CertificateRevocationCheckMode = X509RevocationMode.NoCheck;
        return handler;
    }

    public static void ApplyBrowserLikeDefaults(HttpClient client, string? acceptLanguage = null, bool prefersDocuments = false)
    {
        ArgumentNullException.ThrowIfNull(client);

        if (client.DefaultRequestHeaders.UserAgent.Count == 0)
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd(DefaultUserAgent);
        }

        if (client.DefaultRequestHeaders.Accept.Count == 0)
        {
            if (prefersDocuments)
            {
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/xhtml+xml"));
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/xml", 0.9));
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/pdf", 0.85));
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*", 0.7));
            }
            else
            {
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain", 0.8));
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*", 0.5));
            }
        }

        if (client.DefaultRequestHeaders.AcceptLanguage.Count == 0)
        {
            foreach (var segment in (acceptLanguage ?? "ru-RU,ru;q=0.95,en-US;q=0.85,en;q=0.75")
                         .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (StringWithQualityHeaderValue.TryParse(segment, out var parsed))
                {
                    client.DefaultRequestHeaders.AcceptLanguage.Add(parsed);
                }
            }
        }
    }

    public static void ApplyTlsCompatibilityDefaults(HttpClient client, string? acceptLanguage = null, bool prefersDocuments = false)
    {
        ArgumentNullException.ThrowIfNull(client);
        client.DefaultRequestVersion = HttpVersion.Version11;
        client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
        client.DefaultRequestHeaders.ConnectionClose = true;
        ApplyBrowserLikeDefaults(client, acceptLanguage, prefersDocuments);
    }

    public static void ApplyProxyAwareDefaults(HttpClient client, string? acceptLanguage = null, bool prefersDocuments = false)
    {
        ArgumentNullException.ThrowIfNull(client);
        client.DefaultRequestVersion = HttpVersion.Version11;
        client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
        client.DefaultRequestHeaders.ConnectionClose = true;
        ApplyBrowserLikeDefaults(client, acceptLanguage, prefersDocuments);
    }

    public static bool HasUsableSystemProxy(Uri targetUri)
    {
        ArgumentNullException.ThrowIfNull(targetUri);

        try
        {
            var proxy = WebRequest.GetSystemWebProxy();
            var proxyUri = proxy?.GetProxy(targetUri);
            return proxyUri is not null &&
                proxyUri.IsAbsoluteUri &&
                Uri.Compare(proxyUri, targetUri, UriComponents.AbsoluteUri, UriFormat.SafeUnescaped, StringComparison.OrdinalIgnoreCase) != 0;
        }
        catch
        {
            return false;
        }
    }

    public static bool LooksLikeRenderableDocumentPath(Uri targetUri)
    {
        ArgumentNullException.ThrowIfNull(targetUri);

        if (!targetUri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            !targetUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var path = targetUri.AbsolutePath;
        if (string.IsNullOrWhiteSpace(path) || path == "/")
        {
            return false;
        }

        if (path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".html", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".htm", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (RenderablePathMarkers.Any(marker => path.Contains(marker, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var segments = path
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0)
        {
            return false;
        }

        var lastSegment = segments[^1];
        if (lastSegment.Contains('.', StringComparison.Ordinal))
        {
            return false;
        }

        return lastSegment.Length >= 10;
    }

    public static async Task<BudgetedContentReadResult> ReadBytesWithinBudgetAsync(HttpContent content, int maxBytes, CancellationToken ct)
    {
        await using var stream = await content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        var remaining = maxBytes;

        while (remaining > 0)
        {
            var read = await stream.ReadAsync(chunk.AsMemory(0, Math.Min(chunk.Length, remaining)), ct).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            buffer.Write(chunk, 0, read);
            remaining -= read;
        }

        if (remaining == 0)
        {
            var sentinel = new byte[1];
            var overflowRead = await stream.ReadAsync(sentinel.AsMemory(), ct).ConfigureAwait(false);
            if (overflowRead > 0)
            {
                return new BudgetedContentReadResult(buffer.ToArray(), Truncated: true);
            }
        }

        return new BudgetedContentReadResult(buffer.ToArray(), Truncated: false);
    }

    public readonly record struct BudgetedContentReadResult(byte[] Bytes, bool Truncated);

    private static bool ReadBooleanEnvironmentVariable(string name, bool defaultValue)
    {
        var raw = Environment.GetEnvironmentVariable(name);
        return bool.TryParse(raw, out var parsed) ? parsed : defaultValue;
    }
}

