using System.Net;

namespace Helper.Runtime.WebResearch.Fetching;

public interface ISafeDnsResolver
{
    Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken ct = default);
}

public sealed class SafeDnsResolver : ISafeDnsResolver
{
    public async Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return Array.Empty<IPAddress>();
        }

        if (IPAddress.TryParse(host, out var literal))
        {
            return new[] { literal };
        }

        try
        {
            var addresses = await Dns.GetHostAddressesAsync(host, ct).ConfigureAwait(false);
            return addresses
                .Distinct()
                .ToArray();
        }
        catch
        {
            return Array.Empty<IPAddress>();
        }
    }
}

