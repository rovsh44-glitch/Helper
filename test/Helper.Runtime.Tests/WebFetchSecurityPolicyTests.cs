using System.Net;
using Helper.Runtime.WebResearch.Fetching;

namespace Helper.Runtime.Tests;

public sealed class WebFetchSecurityPolicyTests
{
    [Fact]
    public async Task EvaluateAsync_BlocksPrivateIpv4Target_ForPageFetch()
    {
        var policy = new WebFetchSecurityPolicy(new StubDnsResolver(IPAddress.Parse("192.168.1.20")));

        var decision = await policy.EvaluateAsync(
            new Uri("http://intranet.example/resource"),
            WebFetchTargetKind.PageFetch,
            ct: CancellationToken.None);

        Assert.False(decision.Allowed);
        Assert.Equal("private_or_loopback_address", decision.ReasonCode);
    }

    [Fact]
    public async Task EvaluateAsync_BlocksMetadataAddress_ForPageFetch()
    {
        var policy = new WebFetchSecurityPolicy(new StubDnsResolver(IPAddress.Parse("169.254.169.254")));

        var decision = await policy.EvaluateAsync(
            new Uri("http://metadata.internal/latest/meta-data"),
            WebFetchTargetKind.PageFetch,
            ct: CancellationToken.None);

        Assert.False(decision.Allowed);
        Assert.Equal("private_or_loopback_address", decision.ReasonCode);
    }

    [Fact]
    public async Task EvaluateAsync_AllowsTrustedLoopback_ForLocalSearchProvider()
    {
        var policy = new WebFetchSecurityPolicy(new StubDnsResolver(IPAddress.Loopback));

        var decision = await policy.EvaluateAsync(
            new Uri("http://localhost:8080/search?q=test"),
            WebFetchTargetKind.SearchProvider,
            allowTrustedLoopback: true,
            CancellationToken.None);

        Assert.True(decision.Allowed);
    }

    [Fact]
    public async Task RedirectGuard_BlocksSchemeDowngrade()
    {
        var guard = new RedirectGuard(new WebFetchSecurityPolicy(new StubDnsResolver(IPAddress.Parse("93.184.216.34"))));

        var decision = await guard.EvaluateAsync(
            new Uri("https://example.org/search"),
            new Uri("http://example.org/search"),
            redirectHop: 1,
            ct: CancellationToken.None);

        Assert.False(decision.Allowed);
        Assert.Equal("redirect_scheme_downgrade", decision.ReasonCode);
    }

    [Fact]
    public async Task RedirectGuard_BlocksPrivateRedirectTarget()
    {
        var guard = new RedirectGuard(new WebFetchSecurityPolicy(new StubDnsResolver(IPAddress.Parse("10.0.0.5"))));

        var decision = await guard.EvaluateAsync(
            new Uri("https://example.org/search"),
            new Uri("https://private.example/search"),
            redirectHop: 1,
            ct: CancellationToken.None);

        Assert.False(decision.Allowed);
        Assert.Equal("private_or_loopback_address", decision.ReasonCode);
    }

    private sealed class StubDnsResolver : ISafeDnsResolver
    {
        private readonly IReadOnlyList<IPAddress> _addresses;

        public StubDnsResolver(params IPAddress[] addresses)
        {
            _addresses = addresses;
        }

        public Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken ct = default)
        {
            return Task.FromResult(_addresses);
        }
    }
}

