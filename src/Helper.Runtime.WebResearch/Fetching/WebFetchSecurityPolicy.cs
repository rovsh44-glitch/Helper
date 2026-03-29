using System.Net;

namespace Helper.Runtime.WebResearch.Fetching;

public enum WebFetchTargetKind
{
    SearchProvider,
    Redirect,
    PageFetch
}

public sealed record WebFetchSecurityDecision(
    bool Allowed,
    string ReasonCode,
    IReadOnlyList<string> Trace);

public interface IWebFetchSecurityPolicy
{
    Task<WebFetchSecurityDecision> EvaluateAsync(
        Uri targetUri,
        WebFetchTargetKind targetKind,
        bool allowTrustedLoopback = false,
        CancellationToken ct = default);
}

public sealed class WebFetchSecurityPolicy : IWebFetchSecurityPolicy
{
    private readonly ISafeDnsResolver _dnsResolver;

    public WebFetchSecurityPolicy(ISafeDnsResolver? dnsResolver = null)
    {
        _dnsResolver = dnsResolver ?? new SafeDnsResolver();
    }

    public async Task<WebFetchSecurityDecision> EvaluateAsync(
        Uri targetUri,
        WebFetchTargetKind targetKind,
        bool allowTrustedLoopback = false,
        CancellationToken ct = default)
    {
        if (!targetUri.IsAbsoluteUri)
        {
            return Block("non_absolute_uri", targetUri);
        }

        if (!targetUri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            !targetUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return Block("unsupported_scheme", targetUri);
        }

        if (!string.IsNullOrWhiteSpace(targetUri.UserInfo))
        {
            return Block("credentialed_uri", targetUri);
        }

        if (string.IsNullOrWhiteSpace(targetUri.Host))
        {
            return Block("missing_host", targetUri);
        }

        if (targetUri.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
        {
            return Block("local_tld_blocked", targetUri);
        }

        var addresses = await _dnsResolver.ResolveAsync(targetUri.Host, ct).ConfigureAwait(false);
        if (addresses.Count == 0)
        {
            return Block("dns_resolution_failed", targetUri);
        }

        if (!allowTrustedLoopback && addresses.Any(IsBlockedAddress))
        {
            return Block("private_or_loopback_address", targetUri, addresses);
        }

        if (allowTrustedLoopback)
        {
            var allowed = addresses.All(IPAddress.IsLoopback) && IsTrustedLocalHost(targetUri.Host);
            if (!allowed)
            {
                return Block("trusted_loopback_only", targetUri, addresses);
            }
        }

        return Allow(targetUri, addresses, targetKind, allowTrustedLoopback);
    }

    private static WebFetchSecurityDecision Allow(Uri targetUri, IReadOnlyList<IPAddress> addresses, WebFetchTargetKind targetKind, bool allowTrustedLoopback)
    {
        return new WebFetchSecurityDecision(
            true,
            "allowed",
            new[]
            {
                $"web_fetch.allowed target={targetUri}",
                $"web_fetch.kind={targetKind.ToString().ToLowerInvariant()}",
                $"web_fetch.loopback_override={(allowTrustedLoopback ? "yes" : "no")}",
                $"web_fetch.addresses={string.Join(",", addresses.Select(static address => address.ToString()))}"
            });
    }

    private static WebFetchSecurityDecision Block(string reasonCode, Uri targetUri, IReadOnlyList<IPAddress>? addresses = null)
    {
        var trace = new List<string>
        {
            $"web_fetch.blocked reason={reasonCode} target={targetUri}"
        };

        if (addresses is { Count: > 0 })
        {
            trace.Add($"web_fetch.addresses={string.Join(",", addresses.Select(static address => address.ToString()))}");
        }

        return new WebFetchSecurityDecision(false, reasonCode, trace);
    }

    private static bool IsTrustedLocalHost(string host)
    {
        return host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
               host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
               host.Equals("::1", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBlockedAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            return address.IsIPv6LinkLocal ||
                   address.IsIPv6SiteLocal ||
                   IsIpv6UniqueLocal(address);
        }

        var bytes = address.GetAddressBytes();
        return bytes[0] switch
        {
            10 => true,
            127 => true,
            169 when bytes[1] == 254 => true,
            172 when bytes[1] is >= 16 and <= 31 => true,
            192 when bytes[1] == 168 => true,
            _ => false
        };
    }

    private static bool IsIpv6UniqueLocal(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes.Length > 0 && (bytes[0] & 0xFE) == 0xFC;
    }
}

