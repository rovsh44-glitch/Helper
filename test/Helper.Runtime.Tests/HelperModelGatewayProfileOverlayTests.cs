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
    public void ResolveModel_UsesProviderBinding_BeforeConfiguredFallbacks()
    {
        var gateway = new HelperModelGateway(
            new AILink(),
            new BackendOptionsCatalog(new ApiRuntimeConfig("root", "projects", "library", "logs", "templates", "key")),
            new ModelGatewayTelemetry(),
            new StubResolver("profile-reasoning"));

        var model = gateway.ResolveModel(HelperModelClass.Reasoning);

        Assert.Equal("profile-reasoning", model);
    }

    [Fact]
    public void GetSnapshot_ProjectsActiveProfileId()
    {
        var gateway = new HelperModelGateway(
            new AILink(),
            new BackendOptionsCatalog(new ApiRuntimeConfig("root", "projects", "library", "logs", "templates", "key")),
            new ModelGatewayTelemetry(),
            new StubResolver("profile-reasoning"));

        var snapshot = gateway.GetSnapshot();

        Assert.Equal("profile-overlay", snapshot.ActiveProfileId);
    }

    private sealed class StubResolver : IProviderProfileResolver
    {
        private readonly string? _binding;

        public StubResolver(string? binding)
        {
            _binding = binding;
        }

        public ProviderProfileSummary? GetActiveProfile()
        {
            return new ProviderProfileSummary(
                new ProviderProfile(
                    "profile-overlay",
                    "Profile Overlay",
                    ProviderKind.Ollama,
                    ProviderTransportKind.Ollama,
                    "http://localhost:11434",
                    Enabled: true,
                    IsBuiltIn: false,
                    IsLocal: true,
                    ProviderTrustMode.Local,
                    new[] { ProviderWorkloadGoal.LocalFast },
                    new[] { new ProviderModelClassBinding(HelperModelClass.Reasoning, _binding ?? "fallback") }),
                new ProviderProfileValidationResult(true, Array.Empty<string>(), Array.Empty<string>()),
                new ProviderCapabilitySummary(true, true, true, false, true, true, true, true),
                IsActive: true);
        }

        public ProviderRuntimeConfiguration? GetRuntimeConfiguration() => null;
        public string? ResolveModelBinding(HelperModelClass modelClass) => _binding;
        public string? ResolvePreferredReasoningEffort() => null;
        public bool SupportsVision() => false;
        public bool PrefersResearchVerification() => false;
        public bool IsLocalOnly() => true;
        public string? ApplyToRuntime() => null;
    }
}
