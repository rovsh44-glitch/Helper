using Helper.Api.Backend.Providers;

namespace Helper.Api.Backend.Diagnostics;

public sealed class ProviderDoctorService : IProviderDoctorService
{
    private readonly IProviderProfileCatalog _catalog;
    private readonly IProviderProbeFactory _probeFactory;

    public ProviderDoctorService(
        IProviderProfileCatalog catalog,
        IProviderProbeFactory probeFactory)
    {
        _catalog = catalog;
        _probeFactory = probeFactory;
    }

    public async Task<ProviderDoctorReport> RunAsync(string? profileId, bool includeInactive, CancellationToken ct)
    {
        var snapshot = _catalog.GetSnapshot();
        var candidates = snapshot.Profiles
            .Where(summary => includeInactive || summary.IsActive || summary.Profile.Enabled)
            .Where(summary => string.IsNullOrWhiteSpace(profileId) || string.Equals(summary.Profile.Id, profileId, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var reports = new List<ProviderDoctorProfileReport>(candidates.Length);
        foreach (var summary in candidates)
        {
            reports.Add(await BuildReportAsync(summary, ct));
        }

        var alerts = reports
            .SelectMany(report => report.Alerts)
            .Concat(snapshot.Alerts)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new ProviderDoctorReport(
            DateTimeOffset.UtcNow,
            ComputeOverallStatus(reports),
            snapshot.ActiveProfileId,
            reports,
            alerts);
    }

    private async Task<ProviderDoctorProfileReport> BuildReportAsync(ProviderProfileSummary summary, CancellationToken ct)
    {
        var checks = new List<ProviderDoctorCheck>();
        var alerts = new List<string>(summary.Validation.Alerts);
        var warnings = new List<string>(summary.Validation.Warnings);

        checks.Add(new ProviderDoctorCheck(
            "profile_enabled",
            summary.Profile.Enabled ? "healthy" : "failed",
            summary.Profile.Enabled ? "info" : "error",
            summary.Profile.Enabled ? "Profile is enabled." : "Profile is disabled."));

        checks.Add(new ProviderDoctorCheck(
            "profile_validation",
            summary.Validation.IsValid ? "healthy" : "failed",
            summary.Validation.IsValid ? "info" : "error",
            summary.Validation.IsValid
                ? "Profile validation passed."
                : $"Profile validation reported {summary.Validation.Alerts.Count} alert(s)."));

        if (summary.Profile.Credential?.Required == true)
        {
            var configured = !string.IsNullOrWhiteSpace(summary.Profile.Credential.ApiKeyEnvVar) &&
                             !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(summary.Profile.Credential.ApiKeyEnvVar));
            checks.Add(new ProviderDoctorCheck(
                "credential_configured",
                configured ? "healthy" : "failed",
                configured ? "info" : "error",
                configured
                    ? $"Credential env '{summary.Profile.Credential.ApiKeyEnvVar}' is configured."
                    : $"Credential env '{summary.Profile.Credential.ApiKeyEnvVar}' is missing."));
        }

        checks.Add(new ProviderDoctorCheck(
            "capability_summary",
            "healthy",
            "info",
            "Capability matrix resolved.",
            $"fast={summary.Capabilities.SupportsFast}, reasoning={summary.Capabilities.SupportsReasoning}, coder={summary.Capabilities.SupportsCoder}, vision={summary.Capabilities.SupportsVision}"));

        if (!summary.Profile.Enabled || !summary.Validation.IsValid)
        {
            checks.Add(new ProviderDoctorCheck(
                "runtime_probe",
                "skipped",
                "warning",
                "Runtime probe skipped because the profile is disabled or invalid."));
        }
        else
        {
            try
            {
                var probe = _probeFactory.Resolve(summary);
                checks.AddRange(await probe.ProbeAsync(summary, ct));
            }
            catch (Exception ex)
            {
                alerts.Add(ex.Message);
                checks.Add(new ProviderDoctorCheck(
                    "runtime_probe",
                    "failed",
                    "error",
                    "Runtime probe could not be resolved or executed.",
                    ex.Message));
            }
        }

        return new ProviderDoctorProfileReport(
            summary.Profile.Id,
            summary.Profile.DisplayName,
            summary.Profile.TransportKind.ToString(),
            summary.Profile.BaseUrl,
            summary.IsActive,
            summary.Profile.Enabled,
            ComputeProfileStatus(checks),
            new ProviderCapabilitySummaryDto(
                summary.Capabilities.SupportsFast,
                summary.Capabilities.SupportsReasoning,
                summary.Capabilities.SupportsCoder,
                summary.Capabilities.SupportsVision,
                summary.Capabilities.SupportsBackground,
                summary.Capabilities.SupportsResearchVerified,
                summary.Capabilities.SupportsPrivacyFirst,
                summary.Capabilities.RequiresLocalRuntime),
            checks,
            alerts.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static string ComputeOverallStatus(IReadOnlyList<ProviderDoctorProfileReport> reports)
    {
        if (reports.Count == 0)
        {
            return "degraded";
        }

        if (reports.Any(report => report.IsActive && string.Equals(report.Status, "failed", StringComparison.OrdinalIgnoreCase)))
        {
            return "failed";
        }

        if (reports.Any(report => !string.Equals(report.Status, "healthy", StringComparison.OrdinalIgnoreCase)))
        {
            return "degraded";
        }

        return "healthy";
    }

    private static string ComputeProfileStatus(IReadOnlyList<ProviderDoctorCheck> checks)
    {
        if (checks.Any(check => string.Equals(check.Status, "failed", StringComparison.OrdinalIgnoreCase)))
        {
            return "failed";
        }

        if (checks.Any(check => string.Equals(check.Status, "warning", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(check.Status, "skipped", StringComparison.OrdinalIgnoreCase)))
        {
            return "degraded";
        }

        return "healthy";
    }
}
