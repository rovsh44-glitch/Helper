using Helper.Api.Backend.Configuration;
using Helper.Api.Backend.ModelGateway;
using Helper.Api.Backend.Providers;
using Helper.Api.Hosting;
using Helper.Runtime.Infrastructure;

namespace Helper.Runtime.Tests;

[Trait("Lane", "Fast")]
public sealed class HelperModelGatewayProfileOverlayTests
{
    [Fact]
    public void ResolveModel_UsesConfiguredFallback_FromBackendOptionsCatalog()
    {
        using var scope = new EnvironmentVariableScope(new Dictionary<string, string?>
        {
            ["HELPER_MODEL_REASONING"] = "profile-reasoning"
        });
        var gateway = new HelperModelGateway(
            new AILink(),
            new BackendOptionsCatalog(new ApiRuntimeConfig("root", "projects", "library", "logs", "templates", "key")),
            new ModelGatewayTelemetry());

        var model = gateway.ResolveModel(HelperModelClass.Reasoning);

        Assert.Equal("profile-reasoning", model);
    }

    [Fact]
    public void GetSnapshot_ProjectsCurrentModel_AndStandardPools()
    {
        var gateway = new HelperModelGateway(
            new AILink(),
            new BackendOptionsCatalog(new ApiRuntimeConfig("root", "projects", "library", "logs", "templates", "key")),
            new ModelGatewayTelemetry());

        var snapshot = gateway.GetSnapshot();

        Assert.Equal("qwen2.5-coder:14b", snapshot.CurrentModel);
        Assert.Equal(3, snapshot.Pools.Count);
        Assert.Contains(snapshot.Pools, pool => string.Equals(pool.Pool, "interactive", StringComparison.Ordinal));
        Assert.Contains(snapshot.Pools, pool => string.Equals(pool.Pool, "background", StringComparison.Ordinal));
        Assert.Contains(snapshot.Pools, pool => string.Equals(pool.Pool, "maintenance", StringComparison.Ordinal));
    }

    [Fact]
    public void ResolveModel_UsesProviderProfileBinding_WhenResolverIsPresent()
    {
        var gateway = new HelperModelGateway(
            new AILink(),
            new BackendOptionsCatalog(new ApiRuntimeConfig("root", "projects", "library", "logs", "templates", "key")),
            new ModelGatewayTelemetry(),
            new StubResolver("profile-coder"));

        var model = gateway.ResolveModel(HelperModelClass.Coder);

        Assert.Equal("profile-coder", model);
    }

    private sealed class StubResolver : IProviderProfileResolver
    {
        private readonly string _model;

        public StubResolver(string model)
        {
            _model = model;
        }

        public ProviderProfileSummary? GetActiveProfile() => null;

        public ProviderRuntimeConfiguration? GetRuntimeConfiguration() => null;

        public string? ResolveModelBinding(HelperModelClass modelClass)
            => modelClass == HelperModelClass.Coder ? _model : null;

        public string? ResolvePreferredReasoningEffort() => null;

        public bool SupportsVision() => true;

        public bool PrefersResearchVerification() => false;

        public bool IsLocalOnly() => false;

        public string? ApplyToRuntime() => null;
    }
}
