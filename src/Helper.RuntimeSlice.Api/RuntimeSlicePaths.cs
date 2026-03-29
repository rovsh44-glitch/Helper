namespace Helper.RuntimeSlice.Api;

internal static class RuntimeSlicePaths
{
    public static string DiscoverRepoRoot(string startPath)
    {
        var current = new DirectoryInfo(Path.GetFullPath(startPath));
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Helper.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not discover repo root containing Helper.sln.");
    }

    public static string ResolveFixtureRoot(string repoRoot)
    {
        var configured = Environment.GetEnvironmentVariable("HELPER_RUNTIME_SLICE_FIXTURE_ROOT");
        var resolved = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(repoRoot, "slice", "runtime-review", "sample_data")
            : Path.GetFullPath(configured);

        if (!Directory.Exists(resolved))
        {
            throw new InvalidOperationException($"Runtime slice fixture root is missing: {resolved}");
        }

        return resolved;
    }

    public static string? ResolveWebRoot(string repoRoot)
    {
        var path = Path.Combine(repoRoot, "slice", "runtime-review", "dist");
        return Directory.Exists(path) ? path : null;
    }
}

