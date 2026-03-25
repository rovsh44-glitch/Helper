namespace Helper.RuntimeSlice.Api;

internal static class RuntimeSlicePaths
{
    public static string DiscoverRepoRoot(string startPath)
    {
        var current = new DirectoryInfo(Path.GetFullPath(startPath));
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "package.json")) &&
                Directory.Exists(Path.Combine(current.FullName, "sample_data")) &&
                Directory.Exists(Path.Combine(current.FullName, "src")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not discover runtime-review-slice root.");
    }

    public static string ResolveFixtureRoot(string repoRoot)
    {
        var configured = Environment.GetEnvironmentVariable("HELPER_RUNTIME_SLICE_FIXTURE_ROOT");
        var resolved = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(repoRoot, "sample_data")
            : Path.GetFullPath(configured);

        if (!Directory.Exists(resolved))
        {
            throw new InvalidOperationException($"Runtime slice fixture root is missing: {resolved}");
        }

        return resolved;
    }

    public static string? ResolveWebRoot(string repoRoot)
    {
        var path = Path.Combine(repoRoot, "dist");
        return Directory.Exists(path) ? path : null;
    }
}
