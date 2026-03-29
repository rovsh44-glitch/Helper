using System.Net;
using System.Net.Sockets;
using Helper.Api.Hosting;

namespace Helper.Runtime.Tests;

public sealed class ApiBindingPlanResolverTests
{
    [Fact]
    public void Resolve_UsesConfiguredUrlsWithoutLoopbackOverride_WhenAspNetCoreUrlsProvided()
    {
        var intent = new ApiBindingIntent(null, "https://localhost:7083;http://localhost:5239");

        var plan = ApiBindingPlanResolver.Resolve(intent);

        Assert.True(plan.UsesConfiguredUrls);
        Assert.False(plan.ConfigureLoopbackListener);
        Assert.False(plan.AllowPortFallback);
        Assert.Equal(5239, plan.PrimaryPort);
        Assert.Equal("http://localhost:5239", ApiBindingPlanResolver.ResolveStartupDisplayUrl(plan, plan.PrimaryPort));
    }

    [Fact]
    public void Resolve_PrefersHelperApiPort_AndSuppressesAspNetCoreUrlsOverride()
    {
        var intent = new ApiBindingIntent(5056, "http://localhost:5239");

        var plan = ApiBindingPlanResolver.Resolve(intent);

        Assert.True(intent.ShouldSuppressAspNetCoreUrls);
        Assert.False(plan.UsesConfiguredUrls);
        Assert.True(plan.ConfigureLoopbackListener);
        Assert.False(plan.AllowPortFallback);
        Assert.Equal(5056, plan.PrimaryPort);
        Assert.Equal("http://localhost:5056", ApiBindingPlanResolver.ResolveStartupDisplayUrl(plan, plan.PrimaryPort));
    }

    [Fact]
    public void Resolve_FallsBackToAutoLoopbackPort_WhenNoExplicitBindingExists()
    {
        var intent = new ApiBindingIntent(null, null);

        var plan = ApiBindingPlanResolver.Resolve(intent);

        Assert.False(plan.UsesConfiguredUrls);
        Assert.True(plan.ConfigureLoopbackListener);
        Assert.True(plan.AllowPortFallback);
        Assert.Equal(5000, plan.PrimaryPort);
        Assert.Equal("http://localhost:5000", ApiBindingPlanResolver.ResolveStartupDisplayUrl(plan, plan.PrimaryPort));
    }

    [Fact]
    public void EnsureConfiguredUrlsAvailable_Throws_WhenLoopbackEndpointIsAlreadyOccupied()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var ex = Assert.Throws<InvalidOperationException>(() =>
            ApiBindingPlanResolver.EnsureConfiguredUrlsAvailable($"http://localhost:{port}"));

        Assert.Contains("unavailable", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(port.ToString(), ex.Message, StringComparison.Ordinal);
    }
}

