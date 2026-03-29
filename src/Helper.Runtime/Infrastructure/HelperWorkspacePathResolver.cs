namespace Helper.Runtime.Infrastructure;

public static class HelperWorkspacePathResolver
{
    private static readonly StringComparer PathComparer = StringComparer.OrdinalIgnoreCase;
    private static readonly object EnvLoadLock = new();
    private static readonly HashSet<string> LoadedEnvRoots = new(PathComparer);

    public static string ResolveHelperRoot(string? startPath = null)
    {
        var effectiveStart = string.IsNullOrWhiteSpace(startPath)
            ? AppContext.BaseDirectory
            : startPath;
        var helperRoot = DiscoverHelperRoot(effectiveStart);
        EnsureHelperEnvLoaded(helperRoot);
        return helperRoot;
    }

    public static string DiscoverHelperRoot(string startPath)
    {
        var current = new DirectoryInfo(Path.GetFullPath(startPath));
        while (current is not null)
        {
            var candidate = current.FullName;
            var hasSolution = File.Exists(Path.Combine(candidate, "Helper.sln"));
            var hasApiProject = File.Exists(Path.Combine(candidate, "src", "Helper.Api", "Helper.Api.csproj"));
            var hasRuntimeProject = File.Exists(Path.Combine(candidate, "src", "Helper.Runtime", "Helper.Runtime.csproj"));
            if (hasSolution || hasApiProject || hasRuntimeProject)
            {
                return candidate;
            }

            current = current.Parent;
        }

        return Path.GetFullPath(startPath);
    }

    public static string ResolveDataRoot(string? configuredDataRoot, string helperRoot)
    {
        EnsureHelperEnvLoaded(helperRoot);
        if (!string.IsNullOrWhiteSpace(configuredDataRoot))
        {
            return Path.GetFullPath(configuredDataRoot);
        }

        var parent = Directory.GetParent(Path.GetFullPath(helperRoot))?.FullName ?? helperRoot;
        return Path.Combine(parent, "HELPER_DATA");
    }

    public static string ResolveProjectsRoot(string? configuredRoot = null, string? helperRoot = null)
        => ResolveUnderDataRoot("HELPER_PROJECTS_ROOT", configuredRoot, helperRoot, "PROJECTS");

    public static string ResolveLegacyProjectsRoot(string? helperRoot = null)
        => Path.GetFullPath(Path.Combine(ResolveHelperRoot(helperRoot), "PROJECTS"));

    public static string ResolveWritableProjectsRoot(string? configuredRoot = null, string? helperRoot = null, bool allowLegacyFallback = false)
    {
        var canonicalRoot = ResolveProjectsRoot(configuredRoot, helperRoot);
        if (TryEnsureWritableDirectory(canonicalRoot))
        {
            return canonicalRoot;
        }

        if (!allowLegacyFallback)
        {
            return canonicalRoot;
        }

        var legacyRoot = ResolveLegacyProjectsRoot(helperRoot);
        if (TryEnsureWritableDirectory(legacyRoot))
        {
            return legacyRoot;
        }

        return canonicalRoot;
    }

    public static string ResolveLibraryRoot(string? configuredRoot = null, string? helperRoot = null)
        => ResolveUnderDataRoot("HELPER_LIBRARY_ROOT", configuredRoot, helperRoot, "library");

    public static string ResolveLibraryDocsRoot(string? configuredLibraryRoot = null, string? helperRoot = null)
        => Path.Combine(ResolveLibraryRoot(configuredLibraryRoot, helperRoot), "docs");

    public static string ResolveLogsRoot(string? configuredRoot = null, string? helperRoot = null)
        => ResolveUnderDataRoot("HELPER_LOGS_ROOT", configuredRoot, helperRoot, "LOG");

    public static string ResolveTemplatesRoot(string? configuredRoot = null, string? helperRoot = null)
        => ResolveUnderDataRoot("HELPER_TEMPLATES_ROOT", configuredRoot, helperRoot, Path.Combine("library", "forge_templates"));

    public static string ResolveWorkspaceFile(string relativePath, string? helperRoot = null)
        => ResolveWithinRoot(ResolveHelperRoot(helperRoot), relativePath);

    public static string ResolveDataFilePath(string relativePath, string? configuredDataRoot = null, string? helperRoot = null)
        => ResolveWithinRoot(ResolveDataRoot(configuredDataRoot ?? Environment.GetEnvironmentVariable("HELPER_DATA_ROOT"), ResolveHelperRoot(helperRoot)), relativePath);

    public static string ResolveProjectsPath(string relativePath, string? configuredRoot = null, string? helperRoot = null)
        => ResolveWithinRoot(ResolveProjectsRoot(configuredRoot, helperRoot), relativePath);

    public static string ResolveProjectRunLogPath(string rawProjectRoot, string? configuredProjectsRoot = null, string? helperRoot = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawProjectRoot);

        var ancestorProjectsRoot = TryFindAncestorDirectory(rawProjectRoot, "PROJECTS");
        if (!string.IsNullOrWhiteSpace(ancestorProjectsRoot))
        {
            return Path.Combine(ancestorProjectsRoot, "generation_runs.jsonl");
        }

        return Path.Combine(ResolveProjectsRoot(configuredProjectsRoot, helperRoot), "generation_runs.jsonl");
    }

    public static string ResolveLogsPath(string relativePath, string? configuredRoot = null, string? helperRoot = null)
        => ResolveWithinRoot(ResolveLogsRoot(configuredRoot, helperRoot), relativePath);

    public static string ResolveLibraryPath(string relativePath, string? configuredRoot = null, string? helperRoot = null)
        => ResolveWithinRoot(ResolveLibraryRoot(configuredRoot, helperRoot), relativePath);

    public static string ResolveTemplatesPath(string relativePath, string? configuredRoot = null, string? helperRoot = null)
        => ResolveWithinRoot(ResolveTemplatesRoot(configuredRoot, helperRoot), relativePath);

    public static string ResolveDocPath(string relativePath, string? helperRoot = null)
        => ResolveWithinRoot(Path.Combine(ResolveHelperRoot(helperRoot), "doc"), relativePath);

    public static string ResolveIndexingQueuePath(string? configuredPath = null, string? helperRoot = null)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            return Path.GetFullPath(configuredPath);
        }

        var resolvedHelperRoot = ResolveHelperRoot(helperRoot);
        var dataRoot = ResolveDataRoot(Environment.GetEnvironmentVariable("HELPER_DATA_ROOT"), resolvedHelperRoot);
        return Path.Combine(dataRoot, "indexing_queue.json");
    }

    public static string CanonicalizeLibraryPath(string path, string? configuredLibraryRoot = null, string? helperRoot = null)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        var resolvedHelperRoot = ResolveHelperRoot(helperRoot);
        var libraryRoot = ResolveLibraryRoot(configuredLibraryRoot, resolvedHelperRoot);
        var legacyLibraryRoot = Path.Combine(resolvedHelperRoot, "library");
        var candidate = Path.IsPathRooted(path)
            ? path
            : Path.Combine(libraryRoot, path);
        var fullPath = Path.GetFullPath(candidate);

        if (IsPathUnderRoot(fullPath, legacyLibraryRoot))
        {
            var relativePath = Path.GetRelativePath(legacyLibraryRoot, fullPath);
            return Path.GetFullPath(Path.Combine(libraryRoot, relativePath));
        }

        return fullPath;
    }

    public static Dictionary<string, string> NormalizeLibraryQueue(
        IReadOnlyDictionary<string, string> queue,
        out bool changed,
        string? configuredLibraryRoot = null,
        string? helperRoot = null,
        bool pruneMissing = false)
    {
        changed = false;

        var normalized = new Dictionary<string, string>(PathComparer);
        foreach (var entry in queue)
        {
            if (string.IsNullOrWhiteSpace(entry.Key))
            {
                changed = true;
                continue;
            }

            var canonicalPath = CanonicalizeLibraryPath(entry.Key, configuredLibraryRoot, helperRoot);
            if (!string.Equals(Path.GetFullPath(entry.Key), canonicalPath, StringComparison.OrdinalIgnoreCase))
            {
                changed = true;
            }

            if (pruneMissing && !File.Exists(canonicalPath))
            {
                changed = true;
                continue;
            }

            if (normalized.TryGetValue(canonicalPath, out var existingStatus))
            {
                var mergedStatus = SelectPreferredQueueStatus(existingStatus, entry.Value);
                if (!string.Equals(existingStatus, mergedStatus, StringComparison.OrdinalIgnoreCase))
                {
                    normalized[canonicalPath] = mergedStatus;
                }
                changed = true;
                continue;
            }

            normalized[canonicalPath] = entry.Value;
        }

        return normalized;
    }

    public static bool IsPathUnderRoot(string path, string root)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(root))
        {
            return false;
        }

        var fullPath = Path.GetFullPath(path);
        var fullRoot = EnsureTrailingSeparator(Path.GetFullPath(root));
        return fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(fullPath, fullRoot.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveUnderDataRoot(string envName, string? configuredRoot, string? helperRoot, string fallbackRelative)
    {
        var envValue = Environment.GetEnvironmentVariable(envName);
        var raw = string.IsNullOrWhiteSpace(configuredRoot) ? envValue : configuredRoot;
        var resolvedHelperRoot = ResolveHelperRoot(helperRoot);
        var dataRoot = ResolveDataRoot(Environment.GetEnvironmentVariable("HELPER_DATA_ROOT"), resolvedHelperRoot);

        if (string.IsNullOrWhiteSpace(raw))
        {
            raw = fallbackRelative;
        }

        if (!Path.IsPathRooted(raw))
        {
            raw = Path.Combine(dataRoot, raw);
        }

        return Path.GetFullPath(raw);
    }

    private static string SelectPreferredQueueStatus(string current, string candidate)
    {
        return QueueStatusRank(candidate) > QueueStatusRank(current) ? candidate : current;
    }

    private static int QueueStatusRank(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return 0;
        }

        return status.Trim().ToLowerInvariant() switch
        {
            "done" => 4,
            "processing" => 3,
            "pending" => 2,
            _ when status.StartsWith("Error", StringComparison.OrdinalIgnoreCase) => 1,
            _ => 0
        };
    }

    private static string EnsureTrailingSeparator(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar) ? path : path + Path.DirectorySeparatorChar;
    }

    private static string? TryFindAncestorDirectory(string startPath, string directoryName)
    {
        var fullPath = Path.GetFullPath(startPath);
        var directory = Directory.Exists(fullPath)
            ? new DirectoryInfo(fullPath)
            : Directory.GetParent(fullPath);

        while (directory is not null)
        {
            if (string.Equals(directory.Name, directoryName, StringComparison.OrdinalIgnoreCase))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static string ResolveWithinRoot(string root, string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        if (Path.IsPathRooted(relativePath))
        {
            return Path.GetFullPath(relativePath);
        }

        return Path.GetFullPath(Path.Combine(root, relativePath));
    }

    private static bool TryEnsureWritableDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            var fullPath = Path.GetFullPath(path);
            Directory.CreateDirectory(fullPath);
            var probePath = Path.Combine(fullPath, $".helper_write_probe_{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probePath, string.Empty);

            try
            {
                File.Delete(probePath);
            }
            catch
            {
                // Probe cleanup is best-effort; successful write is enough to treat the root as writable.
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void EnsureHelperEnvLoaded(string? helperRoot)
    {
        if (string.IsNullOrWhiteSpace(helperRoot))
        {
            return;
        }

        var normalizedRoot = Path.GetFullPath(helperRoot);
        lock (EnvLoadLock)
        {
            if (!LoadedEnvRoots.Add(normalizedRoot))
            {
                return;
            }
        }

        try
        {
            var envPath = Path.Combine(normalizedRoot, ".env.local");
            if (!File.Exists(envPath))
            {
                return;
            }

            foreach (var rawLine in File.ReadLines(envPath))
            {
                var line = rawLine.Trim();
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
                {
                    continue;
                }

                var separatorIndex = line.IndexOf('=');
                if (separatorIndex <= 0)
                {
                    continue;
                }

                var name = line[..separatorIndex].Trim();
                var value = line[(separatorIndex + 1)..].Trim();
                if (string.IsNullOrWhiteSpace(name) || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name)))
                {
                    continue;
                }

                Environment.SetEnvironmentVariable(name, value);
            }
        }
        catch
        {
            // Path resolution should stay non-fatal even if local env bootstrap fails.
        }
    }
}

