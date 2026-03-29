using Helper.Api.Backend.Configuration;
using Helper.Api.Backend.ModelGateway;
using Helper.Runtime.Core;

namespace Helper.Api.Backend.Capabilities;

public sealed class RuntimeCapabilityCatalogService : ICapabilityCatalogService
{
    private readonly IModelGateway _modelGateway;
    private readonly IBackendOptionsCatalog _options;
    private readonly IDeclaredCapabilityCatalogProvider _declaredCapabilityCatalogProvider;

    public RuntimeCapabilityCatalogService(
        IModelGateway modelGateway,
        IBackendOptionsCatalog options,
        IDeclaredCapabilityCatalogProvider declaredCapabilityCatalogProvider)
    {
        _modelGateway = modelGateway;
        _options = options;
        _declaredCapabilityCatalogProvider = declaredCapabilityCatalogProvider;
    }

    public async Task<CapabilityCatalogSnapshot> GetSnapshotAsync(CancellationToken ct = default)
    {
        var modelSnapshot = _modelGateway.GetSnapshot();
        var declaredSnapshot = await _declaredCapabilityCatalogProvider.GetSnapshotAsync(ct).ConfigureAwait(false);
        var models = BuildModelEntries(modelSnapshot);
        var summary = BuildSummary(models, declaredSnapshot.Entries);

        var alerts = declaredSnapshot.Alerts.ToList();
        if (modelSnapshot.AvailableModels.Count == 0)
        {
            alerts.Add("Model capability catalog has no available models in the current gateway snapshot.");
        }

        alerts.AddRange(models
            .Where(model => !model.ResolvedModelAvailable)
            .Select(model => $"Model route '{model.RouteKey}' resolves to '{model.ResolvedModel}', which is not visible in the current model catalog."));

        return new CapabilityCatalogSnapshot(
            DateTimeOffset.UtcNow,
            models,
            declaredSnapshot.Entries,
            summary,
            alerts.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private IReadOnlyList<ModelCapabilityCatalogEntry> BuildModelEntries(ModelGatewaySnapshot snapshot)
    {
        return ModelCapabilityCatalogDefinitions.All
            .Select(definition =>
            {
                var resolvedModel = ResolveModel(definition.ModelClass);
                return new ModelCapabilityCatalogEntry(
                    CapabilityId: CapabilityCatalogIds.ModelRoute(definition.RouteKey),
                    RouteKey: definition.RouteKey,
                    ModelClass: definition.ModelClass.ToString().ToLowerInvariant(),
                    IntendedUse: definition.IntendedUse,
                    LatencyTier: definition.LatencyTier,
                    SupportsStreaming: definition.SupportsStreaming,
                    SupportsToolUse: definition.SupportsToolUse,
                    SupportsVision: definition.SupportsVision,
                    FallbackClass: definition.FallbackClass,
                    ConfiguredFallbackModel: ResolveConfiguredFallback(definition.ModelClass),
                    ResolvedModel: resolvedModel,
                    ResolvedModelAvailable: snapshot.AvailableModels.Contains(resolvedModel, StringComparer.OrdinalIgnoreCase),
                    Notes: definition.Notes);
            })
            .OrderBy(model => model.RouteKey, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private CapabilityCatalogSummary BuildSummary(
        IReadOnlyList<ModelCapabilityCatalogEntry> models,
        IReadOnlyList<DeclaredCapabilityCatalogEntry> declaredCapabilities)
    {
        static CapabilityCatalogSurfaceSummary BuildSurfaceSummary(
            string surfaceKind,
            IEnumerable<DeclaredCapabilityCatalogEntry> entries)
        {
            var list = entries.ToArray();
            return new CapabilityCatalogSurfaceSummary(
                SurfaceKind: surfaceKind,
                Total: list.Length,
                Available: list.Count(entry => entry.Available),
                Certified: list.Count(entry => entry.Certified),
                MissingGateOwnership: list.Count(entry =>
                    entry.CertificationRelevant &&
                    entry.EnabledInCertification &&
                    string.IsNullOrWhiteSpace(entry.OwningGate)),
                DisabledInCertification: list.Count(entry => !entry.EnabledInCertification),
                Degraded: list.Count(entry => entry.HasCriticalAlerts || string.Equals(entry.Status, "degraded", StringComparison.OrdinalIgnoreCase)));
        }

        var surfaceSummaries = new List<CapabilityCatalogSurfaceSummary>
        {
            new(
                SurfaceKind: "model",
                Total: models.Count,
                Available: models.Count(model => model.ResolvedModelAvailable),
                Certified: 0,
                MissingGateOwnership: 0,
                DisabledInCertification: 0,
                Degraded: models.Count(model => !model.ResolvedModelAvailable))
        };

        surfaceSummaries.AddRange(
            declaredCapabilities
                .GroupBy(entry => entry.SurfaceKind, StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group => BuildSurfaceSummary(group.Key, group)));

        return new CapabilityCatalogSummary(
            TotalDeclaredCapabilities: declaredCapabilities.Count,
            MissingGateOwnership: declaredCapabilities.Count(entry =>
                entry.CertificationRelevant &&
                entry.EnabledInCertification &&
                string.IsNullOrWhiteSpace(entry.OwningGate)),
            DisabledInCertification: declaredCapabilities.Count(entry => !entry.EnabledInCertification),
            Degraded: declaredCapabilities.Count(entry => entry.HasCriticalAlerts || string.Equals(entry.Status, "degraded", StringComparison.OrdinalIgnoreCase)),
            Surfaces: surfaceSummaries);
    }

    private string ResolveModel(HelperModelClass modelClass)
    {
        try
        {
            return _modelGateway.ResolveModel(modelClass);
        }
        catch (Exception ex)
        {
            return $"unresolved:{CapabilityCatalogIds.NormalizePart(ex.Message)}";
        }
    }

    private string? ResolveConfiguredFallback(HelperModelClass modelClass)
    {
        return modelClass switch
        {
            HelperModelClass.Fast => _options.ModelGateway.FastFallbackModel,
            HelperModelClass.Reasoning => _options.ModelGateway.ReasoningFallbackModel,
            HelperModelClass.Critic => _options.ModelGateway.CriticFallbackModel,
            _ => _options.ModelGateway.SafeFallbackModel
        };
    }
}

