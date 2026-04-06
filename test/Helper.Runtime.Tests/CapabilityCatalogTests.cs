using System.Text.Json;
using Helper.Api.Backend.Capabilities;
using Helper.Api.Backend.Configuration;
using Helper.Api.Backend.ModelGateway;
using Helper.Testing;
using Helper.Runtime.Core;
using Helper.Runtime.Generation;
using Helper.Runtime.Infrastructure;

namespace Helper.Runtime.Tests;

public sealed class CapabilityCatalogTests
{
    [Fact]
    public async Task DeclaredCapabilityCatalogProvider_BuildsTemplateToolAndExtensionEntries()
    {
        using var temp = new TempDirectoryScope();
        var previousTemplatesRoot = Environment.GetEnvironmentVariable("HELPER_TEMPLATES_ROOT");
        Environment.SetEnvironmentVariable("HELPER_TEMPLATES_ROOT", temp.Path);

        try
        {
            var templateRoot = Path.Combine(temp.Path, "Template_Calculator");
            Directory.CreateDirectory(templateRoot);
            await File.WriteAllTextAsync(Path.Combine(templateRoot, "template.json"), JsonSerializer.Serialize(new
            {
                id = "Template_Calculator",
                name = "Calculator",
                capabilities = new[] { "math-engine", "history-export" }
            }));
            await TemplateCertificationStatusStore.WriteAsync(
                templateRoot,
                new TemplateCertificationStatus(DateTimeOffset.UtcNow, Passed: true, HasCriticalAlerts: false, CriticalAlerts: Array.Empty<string>(), ReportPath: "doc/certification/calculator.md"));

            var registry = new StubExtensionRegistry(new ExtensionRegistrySnapshot(
                Manifests: new[]
                {
                    new ExtensionManifest(
                        SchemaVersion: ExtensionRegistry.ManifestSchemaVersion,
                        Id: "helper-local-tools",
                        DisplayName: "Helper Local Tools",
                        Category: ExtensionCategory.BuiltIn,
                        ProviderType: "local_tools",
                        Transport: ExtensionTransport.None,
                        Description: "Built-in local tools.",
                        Command: null,
                        Args: Array.Empty<string>(),
                        DeclaredTools: new[] { "shell_execute", "dotnet_test" },
                        RequiredEnv: Array.Empty<string>(),
                        Capabilities: new[] { "filesystem" },
                        TrustLevel: ExtensionTrustLevel.BuiltIn,
                        DefaultEnabled: true,
                        DisabledInCertificationMode: false,
                        QuietWhenUnavailable: false,
                        ToolPolicy: new ExtensionToolPolicy(false, Array.Empty<string>()),
                        SourcePath: "mcp_config/extensions/helper.local-tools.json")
                },
                Failures: Array.Empty<string>(),
                Warnings: Array.Empty<string>()));

            var provider = new DeclaredCapabilityCatalogProvider(new ProjectTemplateManager(temp.Path), registry);
            var snapshot = await provider.GetSnapshotAsync();

            Assert.Contains(snapshot.Entries, entry =>
                entry.CapabilityId == CapabilityCatalogIds.TemplateCapability("Template_Calculator", "math-engine") &&
                entry.SurfaceKind == "template" &&
                entry.OwningGate == "capability-contract" &&
                entry.Status == "certified");

            Assert.Contains(snapshot.Entries, entry =>
                entry.SurfaceKind == "tool" &&
                entry.OwnerId == "shell_execute" &&
                entry.EvidenceType == "extension-manifest" &&
                entry.OwningGate is null);

            Assert.Contains(snapshot.Entries, entry =>
                entry.SurfaceKind == "tool" &&
                entry.OwnerId == "dotnet_test" &&
                entry.EvidenceType == "extension-manifest" &&
                entry.OwningGate is null);

            Assert.Contains(snapshot.Entries, entry =>
                entry.SurfaceKind == "extension" &&
                entry.OwnerId == "helper-local-tools" &&
                entry.DeclaredCapability == "filesystem");
        }
        finally
        {
            Environment.SetEnvironmentVariable("HELPER_TEMPLATES_ROOT", previousTemplatesRoot);
        }
    }

    [Fact]
    public void CapabilityContractValidator_ReportsStableCapabilityIdentifiers_WhenCoverageFails()
    {
        using var temp = new TempDirectoryScope();
        var templateRoot = Path.Combine(temp.Path, "Template_Report");
        Directory.CreateDirectory(templateRoot);
        File.WriteAllText(Path.Combine(templateRoot, "Program.cs"), "public class Program { }");

        var metadata = JsonSerializer.Deserialize<TemplateMetadataModel>("{\"id\":\"Template_Report\",\"capabilities\":[\"report-export\"]}", new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        var profile = new PolyglotProjectProfile(PolyglotProjectKind.Dotnet, "dotnet");
        var scenario = CapabilityContractValidator.EvaluateContract(
            "capability-contract",
            templateRoot,
            metadata,
            profile,
            static _ => false);

        Assert.False(scenario.Passed);
        Assert.Contains(CapabilityCatalogIds.TemplateCapability("Template_Report", "report-export"), scenario.Details, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RuntimeCapabilityCatalogService_ComputesMissingGateOwnershipSummary()
    {
        var provider = new StubDeclaredCapabilityCatalogProvider(new DeclaredCapabilityCatalogSnapshot(
            DateTimeOffset.UtcNow,
            new[]
            {
                new DeclaredCapabilityCatalogEntry(
                    CapabilityId: CapabilityCatalogIds.Tool("shell_execute"),
                    SurfaceKind: "tool",
                    OwnerId: "shell_execute",
                    DisplayName: "shell_execute",
                    DeclaredCapability: "shell_execute",
                    Status: "unmapped",
                    OwningGate: null,
                    EvidenceType: "extension-manifest",
                    EvidenceRef: "mcp_config/extensions/helper.local-tools.json",
                    Available: true,
                    CertificationRelevant: true,
                    EnabledInCertification: true,
                    Certified: false,
                    HasCriticalAlerts: false,
                    Notes: Array.Empty<string>())
            },
            Alerts: Array.Empty<string>()));

        var service = new RuntimeCapabilityCatalogService(
            new StubModelGateway(
                new[] { "coder-model", "reasoning-model" },
                currentModel: "coder-model"),
            new StubBackendOptionsCatalog(),
            provider);

        var snapshot = await service.GetSnapshotAsync();

        Assert.True(snapshot.Summary.MissingGateOwnership > 0);
        Assert.Contains(snapshot.Models, model => model.RouteKey == "coder");
    }

    private sealed class StubExtensionRegistry : IExtensionRegistry
    {
        private readonly ExtensionRegistrySnapshot _snapshot;

        public StubExtensionRegistry(ExtensionRegistrySnapshot snapshot)
        {
            _snapshot = snapshot;
        }

        public ExtensionRegistrySnapshot GetSnapshot() => _snapshot;

        public IReadOnlyList<ExtensionManifest> GetByCategory(ExtensionCategory category)
            => _snapshot.Manifests.Where(manifest => manifest.Category == category).ToArray();

        public bool TryGetManifest(string extensionId, out ExtensionManifest manifest)
        {
            var resolved = _snapshot.Manifests.FirstOrDefault(x => string.Equals(x.Id, extensionId, StringComparison.OrdinalIgnoreCase));
            if (resolved is null)
            {
                manifest = null!;
                return false;
            }

            manifest = resolved;
            return true;
        }
    }

    private sealed class StubDeclaredCapabilityCatalogProvider : IDeclaredCapabilityCatalogProvider
    {
        private readonly DeclaredCapabilityCatalogSnapshot _snapshot;

        public StubDeclaredCapabilityCatalogProvider(DeclaredCapabilityCatalogSnapshot snapshot)
        {
            _snapshot = snapshot;
        }

        public Task<DeclaredCapabilityCatalogSnapshot> GetSnapshotAsync(CancellationToken ct = default)
            => Task.FromResult(_snapshot);
    }

    private sealed class StubModelGateway : IModelGateway
    {
        private readonly IReadOnlyList<string> _availableModels;
        private readonly string _currentModel;

        public StubModelGateway(IReadOnlyList<string> availableModels, string currentModel)
        {
            _availableModels = availableModels;
            _currentModel = currentModel;
        }

        public Task DiscoverAsync(CancellationToken ct) => Task.CompletedTask;
        public IReadOnlyList<string> GetAvailableModelsSnapshot() => _availableModels;
        public string GetCurrentModel() => _currentModel;
        public string ResolveModel(HelperModelClass modelClass) => modelClass switch
        {
            HelperModelClass.Coder => "coder-model",
            HelperModelClass.Reasoning => "reasoning-model",
            _ => "reasoning-model"
        };
        public Task WarmAsync(HelperModelClass modelClass, CancellationToken ct) => Task.CompletedTask;
        public Task<string> AskAsync(ModelGatewayRequest request, CancellationToken ct) => Task.FromResult("ok");
        public async IAsyncEnumerable<ModelGatewayStreamChunk> StreamAsync(ModelGatewayRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            yield return new ModelGatewayStreamChunk("ok", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
            await Task.CompletedTask;
        }

        public ModelGatewaySnapshot GetSnapshot()
            => new(_availableModels, _currentModel, Array.Empty<ModelPoolSnapshot>(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, Array.Empty<string>());
    }

    private sealed class StubBackendOptionsCatalog : IBackendOptionsCatalog
    {
        public AuthOptions Auth { get; } = new(true, "test", 5, 60);
        public WarmupOptions Warmup { get; } = new("minimal", Array.Empty<string>(), false, 5, 1000, 1000, 1000);
        public ModelGatewayOptions ModelGateway { get; } = new(1, 1, 1, 30, 60, 60, "fast-fallback", "reasoning-fallback", "long-context-fallback", "deep-reasoning-fallback", "verifier-fallback", "critic-fallback", "safe-fallback");
        public PersistenceOptions Persistence { get; } = new("store", 1000, 10, 100, 10, 10, 1000);
        public AuditOptions Audit { get; } = new(100, 30, 3, 10, 0.1);
        public ResearchOptions Research { get; } = new(true, 60, true, 5, 10);
        public TransportOptions Transport { get; } = new(5, 60);
        public PerformanceBudgetOptions Performance { get; } = new(1000, 1000, 1000, 1000, 1000, 10, 10, 1000);
        public BackendRuntimePolicies Policies { get; } = new(true, true, true, true, false, true);
    }

}

