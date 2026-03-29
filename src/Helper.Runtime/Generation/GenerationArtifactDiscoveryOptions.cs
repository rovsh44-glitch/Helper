using Helper.Runtime.Infrastructure;

namespace Helper.Runtime.Generation;

public sealed record GenerationArtifactDiscoveryOptions(
    string WorkspaceRoot,
    string HelperRoot,
    string CanonicalDataRoot,
    string CanonicalProjectsRoot,
    string LegacyWorkspaceProjectsRoot,
    string LegacyHelperProjectsRoot,
    GenerationArtifactDiscoveryMode Mode,
    IReadOnlyList<string> DirectRoots,
    IReadOnlyList<string> RecursiveRoots)
{
    public static GenerationArtifactDiscoveryOptions Resolve(
        string? workspaceRoot = null,
        GenerationArtifactDiscoveryMode? mode = null,
        string? helperRoot = null,
        string? canonicalDataRoot = null,
        string? canonicalProjectsRoot = null)
        => GenerationArtifactDiscoveryOptionsResolver.Resolve(
            workspaceRoot,
            mode,
            helperRoot,
            canonicalDataRoot,
            canonicalProjectsRoot);
}

internal static class GenerationArtifactDiscoveryOptionsResolver
{
    internal static GenerationArtifactDiscoveryOptions Resolve(
        string? workspaceRoot = null,
        GenerationArtifactDiscoveryMode? mode = null,
        string? helperRoot = null,
        string? canonicalDataRoot = null,
        string? canonicalProjectsRoot = null)
    {
        var explicitWorkspaceRootProvided = !string.IsNullOrWhiteSpace(workspaceRoot);
        var resolvedWorkspaceRoot = string.IsNullOrWhiteSpace(workspaceRoot)
            ? HelperWorkspacePathResolver.ResolveHelperRoot()
            : Path.GetFullPath(workspaceRoot);
        var resolvedHelperRoot = string.IsNullOrWhiteSpace(helperRoot)
            ? ResolveWorkspaceRoot(resolvedWorkspaceRoot)
            : Path.GetFullPath(helperRoot);
        var resolvedMode = mode ?? GenerationArtifactDiscoveryModeResolver.ResolveDefault(explicitWorkspaceRootProvided);
        var resolvedCanonicalProjectsRoot = ResolveCanonicalProjectsRoot(canonicalProjectsRoot, canonicalDataRoot, resolvedHelperRoot);
        var resolvedCanonicalDataRoot = ResolveCanonicalDataRoot(canonicalDataRoot, resolvedCanonicalProjectsRoot, resolvedHelperRoot);
        var legacyWorkspaceProjectsRoot = Path.Combine(resolvedWorkspaceRoot, "PROJECTS");
        var legacyHelperProjectsRoot = Path.Combine(resolvedHelperRoot, "PROJECTS");

        var directRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var recursiveRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        switch (resolvedMode)
        {
            case GenerationArtifactDiscoveryMode.WorkspaceOnly:
                AddDirectRoot(resolvedWorkspaceRoot, directRoots);
                AddRecursiveRoot(legacyWorkspaceProjectsRoot, recursiveRoots);
                break;
            case GenerationArtifactDiscoveryMode.CanonicalDataRoot:
                AddDirectRoot(resolvedCanonicalDataRoot, directRoots);
                AddDirectRoot(resolvedCanonicalProjectsRoot, directRoots);
                AddRecursiveRoot(resolvedCanonicalProjectsRoot, recursiveRoots);
                break;
            case GenerationArtifactDiscoveryMode.LegacyFallback:
                AddDirectRoot(resolvedWorkspaceRoot, directRoots);
                AddDirectRoot(resolvedHelperRoot, directRoots);
                AddRecursiveRoot(legacyWorkspaceProjectsRoot, recursiveRoots);
                AddRecursiveRoot(legacyHelperProjectsRoot, recursiveRoots);
                break;
            default:
                AddDirectRoot(resolvedWorkspaceRoot, directRoots);
                AddDirectRoot(resolvedHelperRoot, directRoots);
                AddDirectRoot(resolvedCanonicalDataRoot, directRoots);
                AddDirectRoot(resolvedCanonicalProjectsRoot, directRoots);
                AddRecursiveRoot(legacyWorkspaceProjectsRoot, recursiveRoots);
                AddRecursiveRoot(resolvedCanonicalProjectsRoot, recursiveRoots);
                AddRecursiveRoot(legacyHelperProjectsRoot, recursiveRoots);
                break;
        }

        return new GenerationArtifactDiscoveryOptions(
            WorkspaceRoot: resolvedWorkspaceRoot,
            HelperRoot: resolvedHelperRoot,
            CanonicalDataRoot: resolvedCanonicalDataRoot,
            CanonicalProjectsRoot: resolvedCanonicalProjectsRoot,
            LegacyWorkspaceProjectsRoot: legacyWorkspaceProjectsRoot,
            LegacyHelperProjectsRoot: legacyHelperProjectsRoot,
            Mode: resolvedMode,
            DirectRoots: directRoots.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
            RecursiveRoots: recursiveRoots.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList());
    }

    private static string ResolveCanonicalProjectsRoot(
        string? canonicalProjectsRoot,
        string? canonicalDataRoot,
        string helperRoot)
    {
        if (!string.IsNullOrWhiteSpace(canonicalProjectsRoot))
        {
            return Path.GetFullPath(canonicalProjectsRoot);
        }

        if (!string.IsNullOrWhiteSpace(canonicalDataRoot))
        {
            return Path.Combine(Path.GetFullPath(canonicalDataRoot), "PROJECTS");
        }

        return HelperWorkspacePathResolver.ResolveProjectsRoot(helperRoot: helperRoot);
    }

    private static string ResolveCanonicalDataRoot(
        string? canonicalDataRoot,
        string resolvedCanonicalProjectsRoot,
        string helperRoot)
    {
        if (!string.IsNullOrWhiteSpace(canonicalDataRoot))
        {
            return Path.GetFullPath(canonicalDataRoot);
        }

        var parent = Directory.GetParent(Path.GetFullPath(resolvedCanonicalProjectsRoot));
        if (parent is not null &&
            string.Equals(Path.GetFileName(Path.GetFullPath(resolvedCanonicalProjectsRoot)), "PROJECTS", StringComparison.OrdinalIgnoreCase))
        {
            return parent.FullName;
        }

        return HelperWorkspacePathResolver.ResolveDataRoot(
            Environment.GetEnvironmentVariable("HELPER_DATA_ROOT"),
            helperRoot);
    }

    private static void AddDirectRoot(string root, ISet<string> directRoots)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            return;
        }

        directRoots.Add(Path.GetFullPath(root));
    }

    private static void AddRecursiveRoot(string root, ISet<string> recursiveRoots)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            return;
        }

        var fullRoot = Path.GetFullPath(root);
        if (Directory.Exists(fullRoot))
        {
            recursiveRoots.Add(fullRoot);
        }
    }

    private static string ResolveWorkspaceRoot(string workspaceRoot)
    {
        var fullPath = Path.GetFullPath(workspaceRoot);
        return Directory.Exists(fullPath)
            ? HelperWorkspacePathResolver.DiscoverHelperRoot(fullPath)
            : HelperWorkspacePathResolver.ResolveHelperRoot();
    }
}

