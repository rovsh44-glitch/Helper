using System.Text.Json;
using Helper.Api.Backend.Diagnostics;
using Helper.Api.Hosting;
using Helper.Api.Backend.Providers;
using Microsoft.Extensions.DependencyInjection;

namespace Helper.Runtime.Tests;

[Trait("Lane", "Fast")]
public sealed class ProviderSettingsContractTests
{
    [Fact]
    public void OpenApiDocument_ContainsProviderSettingsEndpoints()
    {
        var payload = JsonSerializer.Serialize(OpenApiDocumentFactory.Create());

        Assert.Contains("/api/settings/provider-profiles", payload, StringComparison.Ordinal);
        Assert.Contains("/api/settings/provider-profiles/activate", payload, StringComparison.Ordinal);
        Assert.Contains("/api/settings/provider-profiles/recommend", payload, StringComparison.Ordinal);
        Assert.Contains("/api/settings/runtime-doctor/run", payload, StringComparison.Ordinal);
    }

    [Fact]
    public void ApiAuthorizationService_ResolvesScopes_ForProviderSettingsEndpoints()
    {
        var config = new ApiRuntimeConfig("root", "projects", "library", "logs", "templates", "primary-key");
        var sessions = new ApiSessionTokenService(config);
        var keys = new AuthKeysStore(config);
        var authz = new ApiAuthorizationService(config, sessions, keys);

        Assert.Equal("metrics:read", authz.ResolveRequiredScope("/api/settings/provider-profiles", "GET"));
        Assert.Equal("metrics:read", authz.ResolveRequiredScope("/api/settings/runtime-doctor/run", "POST"));
        Assert.Equal("evolution:control", authz.ResolveRequiredScope("/api/settings/provider-profiles/activate", "POST"));
    }

    [Fact]
    public void ApplicationServiceRegistration_RegistersProviderSettingsServices()
    {
        var services = new ServiceCollection();

        services.AddHelperApplicationServices(new ApiRuntimeConfig("root", "data", "projects", "library", "logs", "templates", "primary-key"));

        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IProviderProfileStore));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IProviderProfileCatalog));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IProviderProfileResolver));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IProviderProfileActivationService));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IProviderDoctorService));
    }
}
