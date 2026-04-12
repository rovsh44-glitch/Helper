using Helper.Runtime.Core;

namespace Helper.Runtime.Infrastructure;

internal sealed record DotnetBuildTargetResolution(
    string? TargetPath,
    string? ErrorCode,
    string? ErrorMessage)
{
    public bool Succeeded => !string.IsNullOrWhiteSpace(TargetPath);

    public List<BuildError> ToBuildErrors()
    {
        if (Succeeded)
        {
            return new List<BuildError>();
        }

        return new List<BuildError>
        {
            new("Build", 0, ErrorCode ?? "DOTNET_TARGET_INVALID", ErrorMessage ?? "dotnet target resolution failed.")
        };
    }
}

internal static class DotnetBuildTargetResolver
{
    private static readonly HashSet<string> IgnoredSegments = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin",
        "obj",
        ".compile_gate",
        ".git",
        ".vs",
        "node_modules",
        "__pycache__"
    };

    public static DotnetBuildTargetResolution Resolve(string workingDirectory, bool allowRecursiveDiscovery)
    {
        var root = Path.GetFullPath(workingDirectory);
        if (!Directory.Exists(root))
        {
            return new DotnetBuildTargetResolution(
                null,
                "DOTNET_TARGET_ROOT_MISSING",
                $"dotnet build root does not exist: {root}");
        }

        var topLevel = SelectBestCandidate(
            root,
            Directory.GetFiles(root, "*.sln", SearchOption.TopDirectoryOnly),
            Directory.GetFiles(root, "*.csproj", SearchOption.TopDirectoryOnly),
            $"workspace root '{root}'");
        if (topLevel is not null)
        {
            return topLevel;
        }

        if (!allowRecursiveDiscovery)
        {
            return new DotnetBuildTargetResolution(
                null,
                "DOTNET_TARGET_NOT_FOUND",
                $"No .sln or .csproj was found directly under '{root}'.");
        }

        var recursiveSolutions = EnumerateCandidates(root, "*.sln").ToArray();
        var recursiveProjects = EnumerateCandidates(root, "*.csproj").ToArray();
        return SelectBestCandidate(root, recursiveSolutions, recursiveProjects, $"workspace tree '{root}'")
            ?? new DotnetBuildTargetResolution(
                null,
                "DOTNET_TARGET_NOT_FOUND",
                $"No .sln or .csproj was found under '{root}' outside ignored build-artifact directories.");
    }

    public static DotnetBuildTargetResolution ResolveExplicit(string workingDirectory, string targetPath)
    {
        var root = Path.GetFullPath(workingDirectory);
        if (!Directory.Exists(root))
        {
            return new DotnetBuildTargetResolution(
                null,
                "DOTNET_TARGET_ROOT_MISSING",
                $"dotnet build root does not exist: {root}");
        }

        var resolvedTarget = Path.GetFullPath(Path.IsPathRooted(targetPath)
            ? targetPath
            : Path.Combine(root, targetPath));
        if (!File.Exists(resolvedTarget))
        {
            return new DotnetBuildTargetResolution(
                null,
                "DOTNET_TARGET_NOT_FOUND",
                $"Explicit dotnet target does not exist: {resolvedTarget}");
        }

        if (!resolvedTarget.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) &&
            !resolvedTarget.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            return new DotnetBuildTargetResolution(
                null,
                "DOTNET_TARGET_INVALID",
                $"Explicit dotnet target must be a .sln or .csproj file: {resolvedTarget}");
        }

        var relative = Path.GetRelativePath(root, resolvedTarget);
        if (relative.StartsWith("..", StringComparison.Ordinal))
        {
            return new DotnetBuildTargetResolution(
                null,
                "DOTNET_TARGET_OUTSIDE_ROOT",
                $"Explicit dotnet target must stay within '{root}': {resolvedTarget}");
        }

        if (ShouldIgnoreRelativePath(relative))
        {
            return new DotnetBuildTargetResolution(
                null,
                "DOTNET_TARGET_IGNORED_PATH",
                $"Explicit dotnet target points into an ignored build-artifact path: {resolvedTarget}");
        }

        return new DotnetBuildTargetResolution(resolvedTarget, null, null);
    }

    private static DotnetBuildTargetResolution? SelectBestCandidate(
        string root,
        IReadOnlyCollection<string> solutions,
        IReadOnlyCollection<string> projects,
        string scopeLabel)
    {
        if (solutions.Count > 1)
        {
            return new DotnetBuildTargetResolution(
                null,
                "DOTNET_TARGET_AMBIGUOUS",
                $"Multiple solution files were found under {scopeLabel}: {string.Join(", ", solutions.Select(path => Path.GetRelativePath(root, path)))}");
        }

        if (solutions.Count == 1)
        {
            return new DotnetBuildTargetResolution(solutions.Single(), null, null);
        }

        if (projects.Count > 1)
        {
            return new DotnetBuildTargetResolution(
                null,
                "DOTNET_TARGET_AMBIGUOUS",
                $"Multiple project files were found under {scopeLabel}: {string.Join(", ", projects.Select(path => Path.GetRelativePath(root, path)))}");
        }

        return projects.Count == 1
            ? new DotnetBuildTargetResolution(projects.Single(), null, null)
            : null;
    }

    private static IEnumerable<string> EnumerateCandidates(string root, string pattern)
    {
        foreach (var file in Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, file);
            if (ShouldIgnoreRelativePath(relative))
            {
                continue;
            }

            yield return file;
        }
    }

    private static bool ShouldIgnoreRelativePath(string relativePath)
    {
        var segments = relativePath.Split(
            new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
            StringSplitOptions.RemoveEmptyEntries);
        foreach (var segment in segments)
        {
            if (IgnoredSegments.Contains(segment))
            {
                return true;
            }
        }

        return false;
    }
}
