using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Helper.Runtime.Core;
using Helper.Runtime.Generation;

namespace Helper.Runtime.Infrastructure
{
    public class ProjectTemplateManager : ITemplateManager
    {
        private readonly string _templatesBaseDir;
        private readonly bool _routingRequireCertified;
        private readonly bool _routingExcludeCriticalAlerts;
        private static readonly HashSet<string> ExcludedSegments = new(StringComparer.OrdinalIgnoreCase)
        {
            "bin",
            "obj",
            ".git",
            ".vs",
            "node_modules",
            ".compile_gate",
            "__pycache__"
        };

        public ProjectTemplateManager(string? baseDir = null)
        {
            _templatesBaseDir = HelperWorkspacePathResolver.ResolveTemplatesRoot(baseDir);
            if (!Directory.Exists(_templatesBaseDir)) Directory.CreateDirectory(_templatesBaseDir);
            _routingRequireCertified = ReadFlag("HELPER_TEMPLATE_ROUTING_REQUIRE_CERTIFIED", false);
            _routingExcludeCriticalAlerts = ReadFlag("HELPER_TEMPLATE_ROUTING_EXCLUDE_CRITICAL_ALERTS", true);
        }

        public async Task<List<ProjectTemplate>> GetAvailableTemplatesAsync(CancellationToken ct = default)
        {
            var templates = new List<ProjectTemplate>();
            if (!Directory.Exists(_templatesBaseDir)) return templates;

            foreach (var dir in Directory.GetDirectories(_templatesBaseDir))
            {
                ct.ThrowIfCancellationRequested();
                var discovered = await DiscoverTemplatesInDirectoryAsync(dir, ct);
                templates.AddRange(discovered);
            }

            return templates
                .OrderByDescending(t => !t.Deprecated)
                .ThenByDescending(t => ParseVersion(t.Version))
                .ThenBy(t => t.Id, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public async Task<ProjectTemplate?> GetTemplateByIdAsync(string id, CancellationToken ct = default)
        {
            var resolution = await ResolveTemplateAvailabilityAsync(id, ct);
            return resolution.Template;
        }

        public async Task<TemplateAvailabilityResolution> ResolveTemplateAvailabilityAsync(string id, CancellationToken ct = default)
        {
            var templateRoot = Path.Combine(_templatesBaseDir, id);
            if (!Directory.Exists(templateRoot))
            {
                return new TemplateAvailabilityResolution(
                    TemplateId: id,
                    Template: null,
                    TemplateRootPath: templateRoot,
                    ExistsOnDisk: false,
                    State: TemplateAvailabilityState.Missing,
                    Reason: $"Template '{id}' is not present under '{templateRoot}'.");
            }

            var rootMetadata = await TemplateMetadataReader.TryLoadAsync(templateRoot, ct);
            if (rootMetadata is not null)
            {
                var rootStatus = TemplateCertificationStatusStore.TryRead(templateRoot);
                var routingStatus = NormalizeStatusForRouting(templateRoot, rootStatus);
                if (ShouldIncludeVersion(routingStatus))
                {
                    return BuildAvailableResolution(id, BuildTemplateFromMetadata(id, templateRoot, rootMetadata, routingStatus), templateRoot);
                }

                return BuildBlockedResolution(id, templateRoot, rootStatus);
            }

            var candidates = new List<TemplateCandidate>();
            var blocked = new List<BlockedTemplateCandidate>();
            foreach (var versionDir in Directory.GetDirectories(templateRoot))
            {
                ct.ThrowIfCancellationRequested();
                if (string.Equals(Path.GetFileName(versionDir), "candidates", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var metadata = await TemplateMetadataReader.TryLoadAsync(versionDir, ct);
                if (metadata is null)
                {
                    continue;
                }

                var certificationStatus = TemplateCertificationStatusStore.TryRead(versionDir);
                var routingStatus = NormalizeStatusForRouting(versionDir, certificationStatus);
                if (ShouldIncludeVersion(routingStatus))
                {
                    candidates.Add(new TemplateCandidate(BuildTemplateFromMetadata(id, versionDir, metadata, routingStatus)));
                    continue;
                }

                blocked.Add(new BlockedTemplateCandidate(
                    Resolution: BuildBlockedResolution(id, versionDir, certificationStatus),
                    VersionLabel: NormalizeVersionLabel(metadata.Version, versionDir)));
            }

            if (candidates.Count > 0)
            {
                var resolved = SelectPreferredCandidate(id, candidates.Select(x => x.Template).ToList());
                return BuildAvailableResolution(id, resolved, templateRoot);
            }

            if (blocked.Count > 0)
            {
                return SelectPreferredBlockedCandidate(id, blocked);
            }

            if (!_routingRequireCertified)
            {
                return BuildAvailableResolution(id, BuildTemplateFromMetadata(id, templateRoot, null, null), templateRoot);
            }

            return new TemplateAvailabilityResolution(
                TemplateId: id,
                Template: null,
                TemplateRootPath: templateRoot,
                ExistsOnDisk: true,
                State: TemplateAvailabilityState.BlockedByCertificationRequirement,
                Reason: $"Template '{id}' exists at '{templateRoot}', but no certification-eligible versions were discovered.");
        }

        public async Task<string> CloneTemplateAsync(string templateId, string targetPath, CancellationToken ct = default)
        {
            var template = await GetTemplateByIdAsync(templateId, ct);
            if (template == null) throw new DirectoryNotFoundException($"Template {templateId} not found.");

            if (!Directory.Exists(targetPath)) Directory.CreateDirectory(targetPath);

            foreach (string dirPath in Directory.GetDirectories(template.RootPath, "*", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();
                var relative = Path.GetRelativePath(template.RootPath, dirPath);
                if (ShouldSkipRelativePath(relative))
                {
                    continue;
                }

                Directory.CreateDirectory(Path.Combine(targetPath, relative));
            }

            foreach (string newPath in Directory.GetFiles(template.RootPath, "*.*", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();
                var relative = Path.GetRelativePath(template.RootPath, newPath);
                if (ShouldSkipRelativePath(relative))
                {
                    continue;
                }

                var destination = Path.Combine(targetPath, relative);
                var destinationDirectory = Path.GetDirectoryName(destination);
                if (!string.IsNullOrWhiteSpace(destinationDirectory))
                {
                    Directory.CreateDirectory(destinationDirectory);
                }

                File.Copy(newPath, destination, true);
            }

            return targetPath;
        }

        private static ProjectTemplate BuildTemplateFromMetadata(
            string fallbackId,
            string dir,
            TemplateMetadataModel? metadata,
            TemplateCertificationStatus? certificationStatus)
        {
            var id = string.IsNullOrWhiteSpace(metadata?.Id) ? fallbackId : metadata!.Id.Trim();
            var name = string.IsNullOrWhiteSpace(metadata?.Name) ? id.Replace("_", " ") : metadata!.Name.Trim();
            var description = string.IsNullOrWhiteSpace(metadata?.Description)
                ? $"Offline {id} template with pre-cached dependencies."
                : metadata!.Description.Trim();
            var language = string.IsNullOrWhiteSpace(metadata?.Language) ? id.Split('_')[0] : metadata!.Language.Trim();
            var tags = metadata?.Tags?.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var version = string.IsNullOrWhiteSpace(metadata?.Version) ? null : metadata!.Version.Trim();
            var capabilities = metadata?.Capabilities?.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var constraints = metadata?.Constraints?.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var projectType = string.IsNullOrWhiteSpace(metadata?.ProjectType) ? null : metadata!.ProjectType!.Trim();
            var platform = string.IsNullOrWhiteSpace(metadata?.Platform) ? null : metadata!.Platform!.Trim();

            return new ProjectTemplate(
                Id: id,
                Name: name,
                Description: description,
                Language: language,
                RootPath: dir,
                Tags: tags,
                Version: version,
                Deprecated: metadata?.Deprecated ?? false,
                Capabilities: capabilities,
                Constraints: constraints,
                ProjectType: projectType,
                Platform: platform,
                Certified: certificationStatus?.Passed ?? false,
                HasCriticalAlerts: certificationStatus?.HasCriticalAlerts ?? false);
        }

        private async Task<IReadOnlyList<ProjectTemplate>> DiscoverTemplatesInDirectoryAsync(string root, CancellationToken ct)
        {
            var list = new List<ProjectTemplate>();
            var rootId = Path.GetFileName(root);
            var rootMetadata = await TemplateMetadataReader.TryLoadAsync(root, ct);
            if (rootMetadata != null)
            {
                var rootStatus = TemplateCertificationStatusStore.TryRead(root);
                var routingStatus = NormalizeStatusForRouting(root, rootStatus);
                if (ShouldIncludeVersion(routingStatus))
                {
                    list.Add(BuildTemplateFromMetadata(rootId, root, rootMetadata, routingStatus));
                }

                return list;
            }

            foreach (var versionDir in Directory.GetDirectories(root))
            {
                ct.ThrowIfCancellationRequested();
                if (string.Equals(Path.GetFileName(versionDir), "candidates", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var metadata = await TemplateMetadataReader.TryLoadAsync(versionDir, ct);
                if (metadata is null)
                {
                    continue;
                }

                var certificationStatus = TemplateCertificationStatusStore.TryRead(versionDir);
                var routingStatus = NormalizeStatusForRouting(versionDir, certificationStatus);
                if (!ShouldIncludeVersion(routingStatus))
                {
                    continue;
                }

                list.Add(BuildTemplateFromMetadata(rootId, versionDir, metadata, routingStatus));
            }

            if (list.Count == 0)
            {
                if (!_routingRequireCertified)
                {
                    list.Add(BuildTemplateFromMetadata(rootId, root, null, null));
                }
            }

            return list;
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

        private string? ResolveActiveVersion(string templateId)
        {
            var activePath = Path.Combine(_templatesBaseDir, templateId, ".active_version");
            if (!File.Exists(activePath))
            {
                return null;
            }

            try
            {
                var value = File.ReadAllText(activePath).Trim();
                return string.IsNullOrWhiteSpace(value) ? null : value;
            }
            catch
            {
                return null;
            }
        }

        private static string NormalizeVersionLabel(string? version, string rootPath)
        {
            if (!string.IsNullOrWhiteSpace(version))
            {
                return version.Trim();
            }

            var directory = new DirectoryInfo(rootPath);
            return directory.Name;
        }

        private static bool ShouldSkipRelativePath(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                return false;
            }

            var segments = relativePath
                .Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var segment in segments)
            {
                if (ExcludedSegments.Contains(segment))
                {
                    return true;
                }
            }

            var fileName = Path.GetFileName(relativePath);
            return fileName.EndsWith(".user", StringComparison.OrdinalIgnoreCase) ||
                   fileName.EndsWith(".suo", StringComparison.OrdinalIgnoreCase);
        }

        private TemplateAvailabilityResolution BuildAvailableResolution(string id, ProjectTemplate template, string templateRoot)
        {
            return new TemplateAvailabilityResolution(
                TemplateId: id,
                Template: template,
                TemplateRootPath: templateRoot,
                ExistsOnDisk: true,
                State: TemplateAvailabilityState.Available,
                Reason: $"Template '{id}' is available for routing.");
        }

        private TemplateAvailabilityResolution BuildBlockedResolution(string id, string templateRoot, TemplateCertificationStatus? status)
        {
            if (status is null)
            {
                return new TemplateAvailabilityResolution(
                    TemplateId: id,
                    Template: null,
                    TemplateRootPath: templateRoot,
                    ExistsOnDisk: true,
                    State: TemplateAvailabilityState.BlockedByCertificationRequirement,
                    Reason: $"Template '{id}' exists at '{templateRoot}', but routing requires certification and no certification status was found.");
            }

            if (_routingExcludeCriticalAlerts && status.HasCriticalAlerts)
            {
                var firstAlert = status.CriticalAlerts.FirstOrDefault();
                var reason = $"Template '{id}' exists at '{templateRoot}', but is excluded because certification_status.json contains critical alerts.";
                if (!string.IsNullOrWhiteSpace(firstAlert))
                {
                    reason += $" First alert: {firstAlert}";
                }

                return new TemplateAvailabilityResolution(
                    TemplateId: id,
                    Template: null,
                    TemplateRootPath: templateRoot,
                    ExistsOnDisk: true,
                    State: TemplateAvailabilityState.BlockedByCriticalAlerts,
                    Reason: reason,
                    CertificationReportPath: status.ReportPath,
                    CriticalAlerts: status.CriticalAlerts);
            }

            return new TemplateAvailabilityResolution(
                TemplateId: id,
                Template: null,
                TemplateRootPath: templateRoot,
                ExistsOnDisk: true,
                State: TemplateAvailabilityState.BlockedByCertificationRequirement,
                Reason: $"Template '{id}' exists at '{templateRoot}', but routing requires a certified template and the current certification status is not passing.",
                CertificationReportPath: status.ReportPath,
                CriticalAlerts: status.CriticalAlerts);
        }

        private TemplateCertificationStatus? NormalizeStatusForRouting(string templateVersionRoot, TemplateCertificationStatus? status)
        {
            if (status is null)
            {
                return null;
            }

            if (TemplateCertificationStatusStore.IsStale(templateVersionRoot, status) && !_routingRequireCertified)
            {
                return null;
            }

            return status;
        }

        private bool ShouldIncludeVersion(TemplateCertificationStatus? status)
        {
            if (status is null)
            {
                return !_routingRequireCertified;
            }

            if (_routingExcludeCriticalAlerts && status.HasCriticalAlerts)
            {
                return false;
            }

            if (_routingRequireCertified && !status.Passed)
            {
                return false;
            }

            return true;
        }

        private static bool ReadFlag(string envName, bool fallback)
        {
            var raw = Environment.GetEnvironmentVariable(envName);
            return bool.TryParse(raw, out var parsed) ? parsed : fallback;
        }

        private ProjectTemplate SelectPreferredCandidate(string templateId, IReadOnlyList<ProjectTemplate> candidates)
        {
            var activeVersion = ResolveActiveVersion(templateId);
            return candidates
                .OrderByDescending(t => string.Equals(NormalizeVersionLabel(t.Version, t.RootPath), activeVersion, StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(t => !t.Deprecated)
                .ThenByDescending(t => ParseVersion(t.Version))
                .First();
        }

        private TemplateAvailabilityResolution SelectPreferredBlockedCandidate(string templateId, IReadOnlyList<BlockedTemplateCandidate> blocked)
        {
            var activeVersion = ResolveActiveVersion(templateId);
            return blocked
                .OrderByDescending(x => string.Equals(x.VersionLabel, activeVersion, StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(x => x.Resolution.State == TemplateAvailabilityState.BlockedByCriticalAlerts)
                .ThenByDescending(x => ParseVersion(x.VersionLabel))
                .Select(x => x.Resolution)
                .First();
        }

        private sealed record TemplateCandidate(ProjectTemplate Template);

        private sealed record BlockedTemplateCandidate(
            TemplateAvailabilityResolution Resolution,
            string VersionLabel);

    }
}

