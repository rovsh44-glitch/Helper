namespace Helper.Runtime.Core;

public sealed record ModelCapabilityCatalogEntry(
    string CapabilityId,
    string RouteKey,
    string ModelClass,
    string IntendedUse,
    string LatencyTier,
    bool SupportsStreaming,
    bool SupportsToolUse,
    bool SupportsVision,
    string FallbackClass,
    string? ConfiguredFallbackModel,
    string ResolvedModel,
    bool ResolvedModelAvailable,
    IReadOnlyList<string> Notes);

public sealed record DeclaredCapabilityCatalogEntry(
    string CapabilityId,
    string SurfaceKind,
    string OwnerId,
    string DisplayName,
    string DeclaredCapability,
    string Status,
    string? OwningGate,
    string? EvidenceType,
    string? EvidenceRef,
    bool Available,
    bool CertificationRelevant,
    bool EnabledInCertification,
    bool Certified,
    bool HasCriticalAlerts,
    IReadOnlyList<string> Notes);

public sealed record DeclaredCapabilityCatalogSnapshot(
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<DeclaredCapabilityCatalogEntry> Entries,
    IReadOnlyList<string> Alerts);

public sealed record CapabilityCatalogSurfaceSummary(
    string SurfaceKind,
    int Total,
    int Available,
    int Certified,
    int MissingGateOwnership,
    int DisabledInCertification,
    int Degraded);

public sealed record CapabilityCatalogSummary(
    int TotalDeclaredCapabilities,
    int MissingGateOwnership,
    int DisabledInCertification,
    int Degraded,
    IReadOnlyList<CapabilityCatalogSurfaceSummary> Surfaces);

public sealed record CapabilityCatalogSnapshot(
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<ModelCapabilityCatalogEntry> Models,
    IReadOnlyList<DeclaredCapabilityCatalogEntry> DeclaredCapabilities,
    CapabilityCatalogSummary Summary,
    IReadOnlyList<string> Alerts);

public interface IDeclaredCapabilityCatalogProvider
{
    Task<DeclaredCapabilityCatalogSnapshot> GetSnapshotAsync(CancellationToken ct = default);
}

public interface ICapabilityCatalogService
{
    Task<CapabilityCatalogSnapshot> GetSnapshotAsync(CancellationToken ct = default);
}

