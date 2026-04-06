using Helper.Api.Backend.Diagnostics;
using Helper.Api.Backend.ModelGateway;
using Helper.Api.Backend.Providers;

namespace Helper.Runtime.Tests;

[Trait("Lane", "Fast")]
public sealed class ProviderDoctorServiceTests
{
    [Fact]
    public async Task RunAsync_ReportsHealthyProbe_ForActiveProfile()
    {
        var summary = new ProviderProfileSummary(
            new ProviderProfile(
                "local_default",
                "Local Default",
                ProviderKind.Ollama,
                ProviderTransportKind.Ollama,
                "http://localhost:11434",
                Enabled: true,
                IsBuiltIn: true,
                IsLocal: true,
                ProviderTrustMode.Local,
                new[] { ProviderWorkloadGoal.LocalFast },
                new[] { new ProviderModelClassBinding(HelperModelClass.Reasoning, "qwen3:30b") }),
            new ProviderProfileValidationResult(true, Array.Empty<string>(), Array.Empty<string>()),
            new ProviderCapabilitySummary(true, true, true, false, true, true, true, true),
            IsActive: true);
        var snapshot = new ProviderProfilesSnapshot(DateTimeOffset.UtcNow, "local_default", new[] { summary }, Array.Empty<string>());
        var service = new ProviderDoctorService(new StubCatalog(snapshot), new StubProbeFactory(new StubProbe()));

        var report = await service.RunAsync(profileId: null, includeInactive: true, CancellationToken.None);

        Assert.Equal("healthy", report.Status);
        var profileReport = Assert.Single(report.Profiles);
        Assert.Equal("healthy", profileReport.Status);
        Assert.Contains(profileReport.Checks, check => check.Code == "probe" && check.Status == "healthy");
    }

    [Fact]
    public async Task RunAsync_SkipsRuntimeProbe_ForInvalidProfile()
    {
        var summary = new ProviderProfileSummary(
            new ProviderProfile(
                "invalid_profile",
                "Invalid Profile",
                ProviderKind.OpenAiCompatible,
                ProviderTransportKind.OpenAiCompatible,
                "https://api.example.com/v1",
                Enabled: true,
                IsBuiltIn: false,
                IsLocal: false,
                ProviderTrustMode.RemoteTrusted,
                new[] { ProviderWorkloadGoal.HostedReasoning },
                new[] { new ProviderModelClassBinding(HelperModelClass.Reasoning, "gpt-4.1-mini") }),
            new ProviderProfileValidationResult(false, new[] { "Missing credential." }, Array.Empty<string>()),
            new ProviderCapabilitySummary(true, true, true, true, true, true, false, false),
            IsActive: true);
        var snapshot = new ProviderProfilesSnapshot(DateTimeOffset.UtcNow, "invalid_profile", new[] { summary }, Array.Empty<string>());
        var service = new ProviderDoctorService(new StubCatalog(snapshot), new StubProbeFactory(new StubProbe()));

        var report = await service.RunAsync(profileId: null, includeInactive: true, CancellationToken.None);

        Assert.Equal("failed", report.Status);
        var profileReport = Assert.Single(report.Profiles);
        Assert.Contains(profileReport.Checks, check => check.Code == "runtime_probe" && check.Status == "skipped");
    }

    private sealed class StubCatalog : IProviderProfileCatalog
    {
        private readonly ProviderProfilesSnapshot _snapshot;

        public StubCatalog(ProviderProfilesSnapshot snapshot)
        {
            _snapshot = snapshot;
        }

        public ProviderProfilesSnapshot GetSnapshot() => _snapshot;

        public ProviderProfileSummary? GetActiveProfile() => _snapshot.Profiles.FirstOrDefault(profile => profile.IsActive);

        public ProviderProfileSummary? GetById(string profileId)
            => _snapshot.Profiles.FirstOrDefault(profile => string.Equals(profile.Profile.Id, profileId, StringComparison.OrdinalIgnoreCase));
    }

    private sealed class StubProbeFactory : IProviderProbeFactory
    {
        private readonly IProviderProbe _probe;

        public StubProbeFactory(IProviderProbe probe)
        {
            _probe = probe;
        }

        public IProviderProbe Resolve(ProviderProfileSummary summary) => _probe;
    }

    private sealed class StubProbe : IProviderProbe
    {
        public bool CanProbe(ProviderProfileSummary summary) => true;

        public Task<IReadOnlyList<ProviderDoctorCheck>> ProbeAsync(ProviderProfileSummary summary, CancellationToken ct)
        {
            return Task.FromResult<IReadOnlyList<ProviderDoctorCheck>>(new[]
            {
                new ProviderDoctorCheck("probe", "healthy", "info", "Probe passed.", "ok", 5)
            });
        }
    }
}
