using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Helper.Runtime.Core;

namespace Helper.Runtime.Generation;

public sealed class RuntimeConfigPatchApplier : IFixPatchApplier
{
    private static readonly Regex PathTokenRegex = new(@"['""](?<path>[A-Za-z]:\\[^'""]+|/[^'""]+)['""]", RegexOptions.Compiled);
    private static readonly Regex PortRegex = new(@"\b(?<port>\d{2,5})\b", RegexOptions.Compiled);
    private static readonly Regex UrlPortRegex = new(@"^(?<base>https?://[^:]+:)(?<port>\d{2,5})(?<suffix>/?.*)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public FixStrategyKind Strategy => FixStrategyKind.RuntimeConfig;

    public async Task<FixPatchApplyResult> ApplyAsync(
        FixPatchApplyContext context,
        CancellationToken ct = default)
    {
        if (!Directory.Exists(context.CurrentResult.ProjectPath))
        {
            return new FixPatchApplyResult(
                Applied: false,
                Success: false,
                Errors: context.CurrentResult.Errors,
                ChangedFiles: Array.Empty<string>(),
                Notes: "Project path does not exist for runtime config repair.");
        }

        var errors = context.CurrentResult.Errors;
        if (!LooksLikeRuntimeConfigIssue(errors))
        {
            return new FixPatchApplyResult(
                Applied: false,
                Success: false,
                Errors: errors,
                ChangedFiles: Array.Empty<string>(),
                Notes: "Runtime config strategy skipped: no matching error signatures.");
        }

        var changedFiles = new List<string>();
        var createdDirectories = new List<string>();
        var projectRoot = context.CurrentResult.ProjectPath;
        var conflictPorts = ExtractConflictPorts(errors);

        changedFiles.AddRange(await FixLaunchSettingsPortsAsync(projectRoot, conflictPorts, ct));
        changedFiles.AddRange(await FixAppSettingsPortsAsync(projectRoot, conflictPorts, ct));
        createdDirectories.AddRange(EnsureMissingDirectories(errors));

        var applied = changedFiles.Count > 0 || createdDirectories.Count > 0;
        var notes = applied
            ? $"Runtime config repair applied. changed_files={changedFiles.Count}; created_directories={createdDirectories.Count}"
            : "Runtime config repair found no mutable targets.";

        return new FixPatchApplyResult(
            Applied: applied,
            Success: false,
            Errors: errors,
            ChangedFiles: changedFiles.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
            Notes: notes);
    }

    private static bool LooksLikeRuntimeConfigIssue(IReadOnlyList<BuildError> errors)
    {
        foreach (var error in errors)
        {
            var message = error.Message ?? string.Empty;
            if (message.Contains("address already in use", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("failed to bind", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("port", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("could not find a part of the path", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("directorynotfoundexception", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static HashSet<int> ExtractConflictPorts(IReadOnlyList<BuildError> errors)
    {
        var ports = new HashSet<int>();
        foreach (var error in errors)
        {
            var message = error.Message ?? string.Empty;
            foreach (Match match in PortRegex.Matches(message))
            {
                if (!int.TryParse(match.Groups["port"].Value, out var port))
                {
                    continue;
                }

                if (port is >= 80 and <= 65535 && (message.Contains("port", StringComparison.OrdinalIgnoreCase) ||
                                                    message.Contains("address", StringComparison.OrdinalIgnoreCase) ||
                                                    message.Contains("http", StringComparison.OrdinalIgnoreCase)))
                {
                    ports.Add(port);
                }
            }
        }

        return ports;
    }

    private static async Task<IReadOnlyList<string>> FixLaunchSettingsPortsAsync(
        string projectRoot,
        HashSet<int> conflictPorts,
        CancellationToken ct)
    {
        var changed = new List<string>();
        var files = Directory.EnumerateFiles(projectRoot, "launchSettings.json", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            var text = await File.ReadAllTextAsync(file, ct);
            JsonDocument? doc = null;
            try
            {
                doc = JsonDocument.Parse(text);
            }
            catch
            {
                continue;
            }

            using (doc)
            {
                if (!doc.RootElement.TryGetProperty("profiles", out var profiles) ||
                    profiles.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var root = JsonNode.Parse(text) as JsonObject;
                var rootProfiles = root?["profiles"] as JsonObject;
                if (rootProfiles is null)
                {
                    continue;
                }

                var usedPorts = new HashSet<int>(conflictPorts);
                var fileChanged = false;
                foreach (var profileProperty in rootProfiles)
                {
                    if (profileProperty.Value is not JsonObject profileObject)
                    {
                        continue;
                    }

                    var appUrlValue = profileObject["applicationUrl"]?.GetValue<string>();
                    if (string.IsNullOrWhiteSpace(appUrlValue))
                    {
                        continue;
                    }

                    var urls = appUrlValue.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    var rewrittenUrls = new List<string>();
                    var changedAnyUrl = false;
                    foreach (var url in urls)
                    {
                        var updated = RewriteUrlPort(url, conflictPorts, usedPorts, out var changedUrl);
                        rewrittenUrls.Add(updated);
                        changedAnyUrl |= changedUrl;
                    }

                    if (changedAnyUrl)
                    {
                        profileObject["applicationUrl"] = string.Join(';', rewrittenUrls);
                        fileChanged = true;
                    }
                }

                if (!fileChanged)
                {
                    continue;
                }

                var rewritten = root!.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(file, rewritten, ct);
                changed.Add(Path.GetRelativePath(projectRoot, file).Replace(Path.DirectorySeparatorChar, '/'));
            }
        }

        return changed;
    }

    private static async Task<IReadOnlyList<string>> FixAppSettingsPortsAsync(
        string projectRoot,
        HashSet<int> conflictPorts,
        CancellationToken ct)
    {
        var changed = new List<string>();
        if (conflictPorts.Count == 0)
        {
            return changed;
        }

        var files = Directory.EnumerateFiles(projectRoot, "appsettings*.json", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            var text = await File.ReadAllTextAsync(file, ct);
            var changedFile = false;
            var patchedText = text;
            foreach (var port in conflictPorts)
            {
                var replacement = FindNextPort(port, conflictPorts);
                var replaced = patchedText
                    .Replace($":{port}\"", $":{replacement}\"", StringComparison.Ordinal)
                    .Replace($":{port}/", $":{replacement}/", StringComparison.Ordinal);
                if (!string.Equals(replaced, patchedText, StringComparison.Ordinal))
                {
                    patchedText = replaced;
                    changedFile = true;
                }
            }

            if (!changedFile)
            {
                continue;
            }

            await File.WriteAllTextAsync(file, patchedText, ct);
            changed.Add(Path.GetRelativePath(projectRoot, file).Replace(Path.DirectorySeparatorChar, '/'));
        }

        return changed;
    }

    private static IReadOnlyList<string> EnsureMissingDirectories(IReadOnlyList<BuildError> errors)
    {
        var created = new List<string>();
        foreach (var error in errors)
        {
            var message = error.Message ?? string.Empty;
            foreach (Match match in PathTokenRegex.Matches(message))
            {
                var rawPath = match.Groups["path"].Value;
                if (string.IsNullOrWhiteSpace(rawPath))
                {
                    continue;
                }

                string targetDirectory;
                if (Path.HasExtension(rawPath))
                {
                    targetDirectory = Path.GetDirectoryName(rawPath) ?? string.Empty;
                }
                else
                {
                    targetDirectory = rawPath;
                }

                if (string.IsNullOrWhiteSpace(targetDirectory))
                {
                    continue;
                }

                try
                {
                    if (!Directory.Exists(targetDirectory))
                    {
                        Directory.CreateDirectory(targetDirectory);
                        created.Add(targetDirectory);
                    }
                }
                catch
                {
                    // skip invalid or inaccessible paths
                }
            }
        }

        return created;
    }

    private static string RewriteUrlPort(
        string url,
        HashSet<int> conflictPorts,
        HashSet<int> usedPorts,
        out bool changed)
    {
        changed = false;
        var match = UrlPortRegex.Match(url);
        if (!match.Success || !int.TryParse(match.Groups["port"].Value, out var port))
        {
            return url;
        }

        var shouldRewrite = conflictPorts.Contains(port) || usedPorts.Contains(port);
        if (!shouldRewrite)
        {
            usedPorts.Add(port);
            return url;
        }

        var replacement = FindNextPort(port, usedPorts);
        usedPorts.Add(replacement);
        changed = true;
        return $"{match.Groups["base"].Value}{replacement}{match.Groups["suffix"].Value}";
    }

    private static int FindNextPort(int preferredPort, IReadOnlySet<int> occupied)
    {
        var candidate = Math.Clamp(preferredPort + 1, 1024, 65000);
        for (var i = 0; i < 1024; i++)
        {
            if (!occupied.Contains(candidate))
            {
                return candidate;
            }

            candidate++;
            if (candidate > 65000)
            {
                candidate = 1024;
            }
        }

        return preferredPort;
    }
}

