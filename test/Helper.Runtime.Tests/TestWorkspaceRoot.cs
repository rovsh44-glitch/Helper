namespace Helper.Runtime.Tests;

internal static class TestWorkspaceRoot
{
    public static string ResolveFile(params string[] segments)
    {
        var workspaceRoot = ResolveRoot();
        if (segments.Length > 0 &&
            string.Equals(segments[0], "library", StringComparison.OrdinalIgnoreCase))
        {
            var libraryRoot = ResolveLibraryRoot(workspaceRoot);
            var librarySegments = segments.Skip(1).ToArray();
            var libraryPath = librarySegments.Length == 0
                ? libraryRoot
                : Path.Combine(libraryRoot, Path.Combine(librarySegments));
            if (Directory.Exists(libraryPath) || File.Exists(libraryPath))
            {
                return libraryPath;
            }
        }

        return segments.Length == 0
            ? workspaceRoot
            : Path.Combine(workspaceRoot, Path.Combine(segments));
    }

    public static string ResolveRoot()
    {
        foreach (var root in EnumerateRoots())
        {
            return root;
        }

        throw new DirectoryNotFoundException("Workspace root was not found.");
    }

    private static IEnumerable<string> EnumerateRoots()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var candidates = new[]
        {
            Environment.GetEnvironmentVariable("HELPER_WORKSPACE_ROOT"),
            GetCallsitePath(),
            Environment.CurrentDirectory,
            AppContext.BaseDirectory
        };

        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            var current = new DirectoryInfo(Path.GetFullPath(candidate));
            while (current is not null)
            {
                if (!seen.Add(current.FullName))
                {
                    current = current.Parent;
                    continue;
                }

                if (LooksLikeWorkspaceRoot(current.FullName))
                {
                    yield return current.FullName;
                    break;
                }

                current = current.Parent;
            }
        }
    }

    private static string GetCallsitePath([System.Runtime.CompilerServices.CallerFilePath] string path = "")
    {
        return path;
    }

    private static string ResolveLibraryRoot(string workspaceRoot)
    {
        var configuredLibraryRoot = Environment.GetEnvironmentVariable("HELPER_LIBRARY_ROOT");
        if (!string.IsNullOrWhiteSpace(configuredLibraryRoot))
        {
            return Path.GetFullPath(configuredLibraryRoot);
        }

        var workspaceLibrary = Path.Combine(workspaceRoot, "library");
        if (Directory.Exists(workspaceLibrary))
        {
            return workspaceLibrary;
        }

        var configuredDataRoot = Environment.GetEnvironmentVariable("HELPER_DATA_ROOT");
        if (!string.IsNullOrWhiteSpace(configuredDataRoot))
        {
            return Path.Combine(Path.GetFullPath(configuredDataRoot), "library");
        }

        var parent = Directory.GetParent(workspaceRoot)?.FullName ?? workspaceRoot;
        return Path.Combine(parent, "HELPER_DATA", "library");
    }

    private static bool LooksLikeWorkspaceRoot(string candidate)
    {
        var hasSolution = File.Exists(Path.Combine(candidate, "Helper.sln"));
        var hasApiProject = File.Exists(Path.Combine(candidate, "src", "Helper.Api", "Helper.Api.csproj"));
        var hasRuntimeProject = File.Exists(Path.Combine(candidate, "src", "Helper.Runtime", "Helper.Runtime.csproj"));
        var hasPersonality = File.Exists(Path.Combine(candidate, "personality.json"));
        var hasArchitectureDocs = Directory.Exists(Path.Combine(candidate, "doc", "architecture"));
        var hasSliceFixtures = Directory.Exists(Path.Combine(candidate, "slice", "runtime-review", "sample_data"));

        return (hasSolution || hasApiProject || hasRuntimeProject) &&
               (hasPersonality || hasArchitectureDocs || hasSliceFixtures);
    }
}
