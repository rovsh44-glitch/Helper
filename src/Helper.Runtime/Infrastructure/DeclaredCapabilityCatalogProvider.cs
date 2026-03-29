using Helper.Runtime.Core;
using Helper.Runtime.Generation;

namespace Helper.Runtime.Infrastructure;

public sealed class DeclaredCapabilityCatalogProvider : IDeclaredCapabilityCatalogProvider
{
    private readonly ITemplateManager _templateManager;
    private readonly IExtensionRegistry _extensionRegistry;
    private readonly string _templatesRoot;

    public DeclaredCapabilityCatalogProvider(
        ITemplateManager templateManager,
        IExtensionRegistry extensionRegistry)
    {
        _templateManager = templateManager;
        _extensionRegistry = extensionRegistry;
        _templatesRoot = HelperWorkspacePathResolver.ResolveTemplatesRoot();
    }

    public async Task<DeclaredCapabilityCatalogSnapshot> GetSnapshotAsync(CancellationToken ct = default)
    {
        var alerts = new List<string>();
        var entries = new List<DeclaredCapabilityCatalogEntry>();

        entries.AddRange(await BuildTemplateEntriesAsync(alerts, ct).ConfigureAwait(false));

        var extensionSnapshot = _extensionRegistry.GetSnapshot();
        entries.AddRange(BuildToolEntries(extensionSnapshot));
        entries.AddRange(BuildExtensionEntries(extensionSnapshot));

        alerts.AddRange(extensionSnapshot.Failures.Select(failure => $"Extension registry failure: {failure}"));
        alerts.AddRange(extensionSnapshot.Warnings.Select(warning => $"Extension registry warning: {warning}"));

        var missingGateOwnership = entries.Count(entry =>
            entry.CertificationRelevant &&
            entry.EnabledInCertification &&
            string.IsNullOrWhiteSpace(entry.OwningGate));
        if (missingGateOwnership > 0)
        {
            alerts.Add($"{missingGateOwnership} declared capability record(s) are missing owning certification gates.");
        }

        return new DeclaredCapabilityCatalogSnapshot(
            DateTimeOffset.UtcNow,
            entries
                .OrderBy(entry => entry.SurfaceKind, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.OwnerId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.DeclaredCapability, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            alerts.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private async Task<IReadOnlyList<DeclaredCapabilityCatalogEntry>> BuildTemplateEntriesAsync(List<string> alerts, CancellationToken ct)
    {
        if (!Directory.Exists(_templatesRoot))
        {
            alerts.Add($"Template capability catalog is unavailable because templates root '{_templatesRoot}' does not exist.");
            return Array.Empty<DeclaredCapabilityCatalogEntry>();
        }

        var entries = new List<DeclaredCapabilityCatalogEntry>();
        foreach (var templateRoot in Directory.GetDirectories(_templatesRoot).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();

            var templateId = Path.GetFileName(templateRoot);
            if (string.IsNullOrWhiteSpace(templateId))
            {
                continue;
            }

            var resolution = await _templateManager.ResolveTemplateAvailabilityAsync(templateId, ct).ConfigureAwait(false);
            var metadataRoot = resolution.Template?.RootPath ?? resolution.TemplateRootPath ?? templateRoot;
            var metadata = await TemplateMetadataReader.TryLoadAsync(metadataRoot, ct).ConfigureAwait(false);
            var status = TemplateCertificationStatusStore.TryRead(metadataRoot);
            var capabilities = (resolution.Template?.Capabilities ?? metadata?.Capabilities ?? Array.Empty<string>())
                .Where(capability => !string.IsNullOrWhiteSpace(capability))
                .Select(capability => capability.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (capabilities.Length == 0)
            {
                alerts.Add($"Template '{templateId}' declares no capabilities in template metadata.");
                continue;
            }

            var available = resolution.Template is not null;
            var certified = resolution.Template?.Certified ?? status?.Passed ?? false;
            var hasCriticalAlerts = resolution.Template?.HasCriticalAlerts ?? status?.HasCriticalAlerts ?? false;
            var enabledInCertification = available && !hasCriticalAlerts;
            var statusLabel = resolution.State switch
            {
                TemplateAvailabilityState.BlockedByCriticalAlerts => "degraded",
                TemplateAvailabilityState.BlockedByCertificationRequirement => "blocked",
                TemplateAvailabilityState.Missing => "blocked",
                _ when certified => "certified",
                _ => "declared",
            };

            var displayName = resolution.Template?.Name ?? metadata?.Name ?? templateId;
            var evidenceRef = status?.ReportPath ?? Path.Combine(metadataRoot, "template.json");
            var notes = new List<string>();
            if (!string.IsNullOrWhiteSpace(resolution.Reason))
            {
                notes.Add(resolution.Reason);
            }

            if (status?.CriticalAlerts is { Count: > 0 })
            {
                notes.AddRange(status.CriticalAlerts);
            }

            foreach (var capability in capabilities)
            {
                entries.Add(new DeclaredCapabilityCatalogEntry(
                    CapabilityId: CapabilityCatalogIds.TemplateCapability(templateId, capability),
                    SurfaceKind: "template",
                    OwnerId: templateId,
                    DisplayName: displayName,
                    DeclaredCapability: capability,
                    Status: statusLabel,
                    OwningGate: "capability-contract",
                    EvidenceType: "template-smoke-scenario",
                    EvidenceRef: evidenceRef,
                    Available: available,
                    CertificationRelevant: true,
                    EnabledInCertification: enabledInCertification,
                    Certified: certified,
                    HasCriticalAlerts: hasCriticalAlerts,
                    Notes: notes.ToArray()));
            }
        }

        return entries;
    }

    private static IReadOnlyList<DeclaredCapabilityCatalogEntry> BuildToolEntries(ExtensionRegistrySnapshot snapshot)
    {
        return snapshot.Manifests
            .SelectMany(manifest => manifest.DeclaredTools.Select(tool => BuildToolEntry(manifest, tool)))
            .OrderBy(entry => entry.OwnerId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static DeclaredCapabilityCatalogEntry BuildToolEntry(ExtensionManifest manifest, string tool)
    {
        var status = manifest.DisabledInCertificationMode ? "disabled_in_certification" : "unmapped";
        var notes = new[]
        {
            $"Extension: {manifest.DisplayName}",
            $"Category: {manifest.Category}",
            $"Trust: {manifest.TrustLevel}"
        };

        return new DeclaredCapabilityCatalogEntry(
            CapabilityId: CapabilityCatalogIds.Tool(tool),
            SurfaceKind: "tool",
            OwnerId: tool,
            DisplayName: tool,
            DeclaredCapability: tool,
            Status: status,
            OwningGate: null,
            EvidenceType: "extension-manifest",
            EvidenceRef: manifest.SourcePath,
            Available: manifest.DefaultEnabled,
            CertificationRelevant: !manifest.DisabledInCertificationMode,
            EnabledInCertification: !manifest.DisabledInCertificationMode,
            Certified: false,
            HasCriticalAlerts: false,
            Notes: notes);
    }

    private static IReadOnlyList<DeclaredCapabilityCatalogEntry> BuildExtensionEntries(ExtensionRegistrySnapshot snapshot)
    {
        return snapshot.Manifests
            .SelectMany(manifest => manifest.Capabilities.Select(capability => BuildExtensionEntry(manifest, capability)))
            .OrderBy(entry => entry.OwnerId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.DeclaredCapability, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static DeclaredCapabilityCatalogEntry BuildExtensionEntry(ExtensionManifest manifest, string capability)
    {
        var status = manifest.DisabledInCertificationMode ? "disabled_in_certification" : "unmapped";
        var notes = new[]
        {
            $"Transport: {manifest.Transport}",
            $"Category: {manifest.Category}",
            $"Trust: {manifest.TrustLevel}"
        };

        return new DeclaredCapabilityCatalogEntry(
            CapabilityId: CapabilityCatalogIds.ExtensionCapability(manifest.Id, capability),
            SurfaceKind: "extension",
            OwnerId: manifest.Id,
            DisplayName: manifest.DisplayName,
            DeclaredCapability: capability,
            Status: status,
            OwningGate: null,
            EvidenceType: "extension-manifest",
            EvidenceRef: manifest.SourcePath,
            Available: manifest.DefaultEnabled,
            CertificationRelevant: !manifest.DisabledInCertificationMode,
            EnabledInCertification: !manifest.DisabledInCertificationMode,
            Certified: false,
            HasCriticalAlerts: false,
            Notes: notes);
    }
}

