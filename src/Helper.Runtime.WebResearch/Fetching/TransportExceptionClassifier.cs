using System.Net.Http;
using System.Net.Sockets;
using System.Security.Authentication;
using Helper.Runtime.WebResearch.Extraction;

namespace Helper.Runtime.WebResearch.Fetching;

internal enum TransportRetryProfile
{
    TlsCompatibility,
    ProxyBrowser,
    ProxyTlsCompatibility
}

internal static class TransportExceptionClassifier
{
    private static readonly string[] TlsRetryableMarkers =
    {
        "ssl connection could not be established",
        "authentication failed",
        "secure channel",
        "handshake",
        "tls",
        "certificate",
        "unexpected eof",
        "received an unexpected eof",
        "cannot determine the frame size"
    };

    private static readonly string[] ProxyReachabilityMarkers =
    {
        "actively refused",
        "connection refused",
        "target machine actively refused",
        "connection reset",
        "forcibly closed",
        "name or service not known",
        "no such host is known",
        "host is unknown",
        "could not be resolved",
        "temporary failure in name resolution",
        "network is unreachable",
        "connection timed out"
    };

    public static bool ShouldRetryWithTlsCompatibility(Uri currentUri, Exception exception, bool compatibilityClientAvailable)
    {
        if (!compatibilityClientAvailable ||
            !currentUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        foreach (var current in Enumerate(exception))
        {
            if (current is AuthenticationException)
            {
                return true;
            }

            var message = current.Message ?? string.Empty;
            if (TlsRetryableMarkers.Any(marker => message.Contains(marker, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    public static IReadOnlyList<TransportRetryProfile> BuildRetryPlan(
        Uri currentUri,
        Exception exception,
        bool tlsCompatibilityAvailable,
        bool proxyBrowserAvailable,
        bool proxyTlsCompatibilityAvailable)
    {
        var retries = new List<TransportRetryProfile>(3);
        var tlsLikeFailure = ShouldRetryWithTlsCompatibility(currentUri, exception, tlsCompatibilityAvailable);
        var proxyLikeFailure = ShouldRetryWithProxyFallback(exception);

        if (tlsLikeFailure && tlsCompatibilityAvailable)
        {
            retries.Add(TransportRetryProfile.TlsCompatibility);
        }

        if (proxyLikeFailure && proxyBrowserAvailable)
        {
            retries.Add(TransportRetryProfile.ProxyBrowser);
        }

        if ((tlsLikeFailure || proxyLikeFailure) &&
            currentUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
            proxyTlsCompatibilityAvailable)
        {
            retries.Add(TransportRetryProfile.ProxyTlsCompatibility);
        }

        return retries;
    }

    public static string Categorize(Exception exception)
    {
        foreach (var current in Enumerate(exception))
        {
            if (current is AuthenticationException)
            {
                return "tls_handshake";
            }

            if (current is SocketException socketException)
            {
                return socketException.SocketErrorCode switch
                {
                    SocketError.ConnectionRefused => "connection_refused",
                    SocketError.ConnectionReset => "connection_reset",
                    SocketError.TimedOut => "timeout",
                    SocketError.HostNotFound => "name_resolution",
                    SocketError.NetworkUnreachable => "network_unreachable",
                    _ => "socket_failure"
                };
            }

            var message = current.Message ?? string.Empty;
            if (TlsRetryableMarkers.Any(marker => message.Contains(marker, StringComparison.OrdinalIgnoreCase)))
            {
                return "tls_handshake";
            }

            if (ProxyReachabilityMarkers.Any(marker => message.Contains(marker, StringComparison.OrdinalIgnoreCase)))
            {
                return message.Contains("refused", StringComparison.OrdinalIgnoreCase)
                    ? "connection_refused"
                    : message.Contains("timed out", StringComparison.OrdinalIgnoreCase)
                        ? "timeout"
                        : message.Contains("resolved", StringComparison.OrdinalIgnoreCase) ||
                          message.Contains("host", StringComparison.OrdinalIgnoreCase)
                            ? "name_resolution"
                            : "connectivity_failure";
            }

            if (current is TimeoutException || current is TaskCanceledException)
            {
                return "timeout";
            }
        }

        return "unknown_transport";
    }

    public static string Summarize(Exception exception)
    {
        foreach (var current in Enumerate(exception))
        {
            if (!string.IsNullOrWhiteSpace(current.Message))
            {
                return current.Message
                    .Replace('\r', ' ')
                    .Replace('\n', ' ')
                    .Replace('"', '\'')
                    .Trim();
            }
        }

        return "unknown";
    }

    public static bool ShouldAttemptBrowserRenderRecovery(
        Uri currentUri,
        WebPageFetchDiagnostics diagnostics,
        WebSourceTypeProfile sourceType)
    {
        ArgumentNullException.ThrowIfNull(currentUri);
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentNullException.ThrowIfNull(sourceType);

        if (!diagnostics.TransportFailureObserved)
        {
            return false;
        }

        if (!currentUri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            !currentUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.Equals(sourceType.Kind, "interactive_shell", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var category = diagnostics.FinalFailureCategory ?? "unknown_transport";
        var renderRecoverable = category is "connection_refused" or "connection_reset" or "timeout" or "tls_handshake" or "connectivity_failure";
        if (!renderRecoverable)
        {
            return false;
        }

        return sourceType.DocumentLike || HttpFetchSupport.LooksLikeRenderableDocumentPath(currentUri);
    }

    private static bool ShouldRetryWithProxyFallback(Exception exception)
    {
        foreach (var current in Enumerate(exception))
        {
            if (current is SocketException socketException &&
                socketException.SocketErrorCode is SocketError.ConnectionRefused or SocketError.ConnectionReset or SocketError.TimedOut or SocketError.HostNotFound or SocketError.NetworkUnreachable)
            {
                return true;
            }

            if (current is TimeoutException || current is TaskCanceledException)
            {
                return true;
            }

            var message = current.Message ?? string.Empty;
            if (ProxyReachabilityMarkers.Any(marker => message.Contains(marker, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<Exception> Enumerate(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException!)
        {
            yield return current;
        }
    }
}

