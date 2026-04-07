using Helper.Api.Backend.Providers;

namespace Helper.Api.Backend.Diagnostics;

public sealed record ProviderDoctorRunRequestDto(
    string? ProfileId = null,
    bool IncludeInactive = true);

public sealed record ProviderDoctorCheck(
    string Code,
    string Status,
    string Severity,
    string Summary,
    string? Detail = null,
    long? DurationMs = null);

public sealed record ProviderDoctorProfileReport(
    string ProfileId,
    string DisplayName,
    string TransportKind,
    string BaseUrl,
    bool IsActive,
    bool IsEnabled,
    string Status,
    ProviderCapabilitySummaryDto Capabilities,
    IReadOnlyList<ProviderDoctorCheck> Checks,
    IReadOnlyList<string> Alerts,
    IReadOnlyList<string> Warnings);

public sealed record ProviderDoctorReport(
    DateTimeOffset GeneratedAtUtc,
    string Status,
    string? ActiveProfileId,
    IReadOnlyList<ProviderDoctorProfileReport> Profiles,
    IReadOnlyList<string> Alerts);

public interface IProviderDoctorService
{
    Task<ProviderDoctorReport> RunAsync(string? profileId, bool includeInactive, CancellationToken ct);
}

public interface IProviderProbe
{
    bool CanProbe(ProviderProfileSummary summary);
    Task<IReadOnlyList<ProviderDoctorCheck>> ProbeAsync(ProviderProfileSummary summary, CancellationToken ct);
}

public interface IProviderProbeFactory
{
    IProviderProbe Resolve(ProviderProfileSummary summary);
}
