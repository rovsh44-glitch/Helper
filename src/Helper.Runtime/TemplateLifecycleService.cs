using System.Text.Json;
using Helper.Runtime.Core;

namespace Helper.Runtime.Infrastructure;

public sealed class TemplateLifecycleService : ITemplateLifecycleService
{
    private readonly string _templatesRoot;

    public TemplateLifecycleService(string templatesRoot)
    {
        _templatesRoot = templatesRoot;
    }

    public async Task<IReadOnlyList<TemplateVersionInfo>> GetVersionsAsync(string templateId, CancellationToken ct = default)
    {
        var root = Path.Combine(_templatesRoot, templateId);
        if (!Directory.Exists(root))
        {
            return Array.Empty<TemplateVersionInfo>();
        }

        var active = await ReadActiveVersionAsync(templateId, ct);
        var versions = await DiscoverVersionsAsync(root, templateId, ct);
        return versions
            .OrderByDescending(v => v.IsActive)
            .ThenByDescending(v => ParseVersion(v.Version))
            .ThenBy(v => v.Version, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<TemplateVersionActivationResult> ActivateVersionAsync(string templateId, string version, CancellationToken ct = default)
    {
        var root = Path.Combine(_templatesRoot, templateId);
        if (!Directory.Exists(root))
        {
            return new TemplateVersionActivationResult(false, templateId, null, "Template root does not exist.");
        }

        var versions = await DiscoverVersionsAsync(root, templateId, ct);
        var target = versions.FirstOrDefault(v => string.Equals(v.Version, version, StringComparison.OrdinalIgnoreCase));
        if (target is null)
        {
            return new TemplateVersionActivationResult(false, templateId, null, $"Version '{version}' was not found.");
        }

        var previousActive = await ReadActiveVersionAsync(templateId, ct);
        await WriteActiveVersionAsync(templateId, target.Version, ct);
        await AppendHistoryAsync(templateId, previousActive, target.Version, ct);
        return new TemplateVersionActivationResult(true, templateId, target.Version, "Template version activated.");
    }

    public async Task<TemplateVersionActivationResult> RollbackAsync(string templateId, CancellationToken ct = default)
    {
        var history = await ReadHistoryAsync(templateId, ct);
        if (history.Count == 0)
        {
            return new TemplateVersionActivationResult(false, templateId, null, "No activation history found.");
        }

        var previous = history.LastOrDefault(x => !string.IsNullOrWhiteSpace(x.PreviousVersion))?.PreviousVersion;
        if (string.IsNullOrWhiteSpace(previous))
        {
            return new TemplateVersionActivationResult(false, templateId, null, "No previous active version found for rollback.");
        }

        return await ActivateVersionAsync(templateId, previous, ct);
    }

    private async Task<List<TemplateVersionInfo>> DiscoverVersionsAsync(string root, string templateId, CancellationToken ct)
    {
        var versions = new List<TemplateVersionInfo>();
        var activeVersion = await ReadActiveVersionAsync(templateId, ct);

        var rootMetadata = await TemplateMetadataReader.TryLoadAsync(root, ct);
        if (rootMetadata is not null)
        {
            var versionLabel = NormalizeVersion(rootMetadata.Version, new DirectoryInfo(root).Name);
            versions.Add(new TemplateVersionInfo(versionLabel, rootMetadata.Deprecated, string.Equals(versionLabel, activeVersion, StringComparison.OrdinalIgnoreCase), root));
            return versions;
        }

        foreach (var dir in Directory.GetDirectories(root))
        {
            ct.ThrowIfCancellationRequested();
            var metadata = await TemplateMetadataReader.TryLoadAsync(dir, ct);
            if (metadata is null)
            {
                continue;
            }

            var versionLabel = NormalizeVersion(metadata.Version, new DirectoryInfo(dir).Name);
            versions.Add(new TemplateVersionInfo(
                versionLabel,
                metadata.Deprecated,
                string.Equals(versionLabel, activeVersion, StringComparison.OrdinalIgnoreCase),
                dir));
        }

        return versions;
    }

    private async Task<string?> ReadActiveVersionAsync(string templateId, CancellationToken ct)
    {
        var path = Path.Combine(_templatesRoot, templateId, ".active_version");
        if (!File.Exists(path))
        {
            return null;
        }

        var content = await File.ReadAllTextAsync(path, ct);
        return string.IsNullOrWhiteSpace(content) ? null : content.Trim();
    }

    private async Task WriteActiveVersionAsync(string templateId, string version, CancellationToken ct)
    {
        var root = Path.Combine(_templatesRoot, templateId);
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, ".active_version");
        await File.WriteAllTextAsync(path, version.Trim(), ct);
    }

    private async Task AppendHistoryAsync(string templateId, string? previousVersion, string activatedVersion, CancellationToken ct)
    {
        var history = await ReadHistoryAsync(templateId, ct);
        history.Add(new TemplateActivationHistoryEntry(DateTimeOffset.UtcNow, previousVersion, activatedVersion));
        if (history.Count > 64)
        {
            history = history.TakeLast(64).ToList();
        }

        var root = Path.Combine(_templatesRoot, templateId);
        Directory.CreateDirectory(root);
        var historyPath = Path.Combine(root, ".active_version.history.json");
        var json = JsonSerializer.Serialize(history, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(historyPath, json, ct);
    }

    private async Task<List<TemplateActivationHistoryEntry>> ReadHistoryAsync(string templateId, CancellationToken ct)
    {
        var path = Path.Combine(_templatesRoot, templateId, ".active_version.history.json");
        if (!File.Exists(path))
        {
            return new List<TemplateActivationHistoryEntry>();
        }

        try
        {
            var json = await File.ReadAllTextAsync(path, ct);
            return JsonSerializer.Deserialize<List<TemplateActivationHistoryEntry>>(json) ?? new List<TemplateActivationHistoryEntry>();
        }
        catch
        {
            return new List<TemplateActivationHistoryEntry>();
        }
    }

    private static string NormalizeVersion(string? metadataVersion, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(metadataVersion))
        {
            return metadataVersion.Trim();
        }

        return fallback;
    }

    private static Version ParseVersion(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new Version(0, 0, 0, 0);
        }

        return Version.TryParse(raw.TrimStart('v', 'V'), out var parsed)
            ? parsed
            : new Version(0, 0, 0, 0);
    }

    private sealed record TemplateActivationHistoryEntry(
        DateTimeOffset ActivatedAtUtc,
        string? PreviousVersion,
        string ActivatedVersion);
}

