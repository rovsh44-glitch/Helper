namespace Helper.Runtime.Generation;

internal static class ParitySnapshotPathResolver
{
    private const string SnapshotRootEnv = "HELPER_PARITY_SNAPSHOT_ROOT";

    internal static string ResolveRoot(string workspaceRoot)
    {
        var configuredRoot = Environment.GetEnvironmentVariable(SnapshotRootEnv);
        if (string.IsNullOrWhiteSpace(configuredRoot))
        {
            return Path.Combine(workspaceRoot, "doc", "parity_nightly");
        }

        return Path.IsPathRooted(configuredRoot)
            ? Path.GetFullPath(configuredRoot)
            : Path.GetFullPath(Path.Combine(workspaceRoot, configuredRoot));
    }

    internal static string ResolveDailyDirectory(string workspaceRoot)
        => Path.Combine(ResolveRoot(workspaceRoot), "daily");

    internal static string ResolveHistoryDirectory(string workspaceRoot)
        => Path.Combine(ResolveRoot(workspaceRoot), "history");

    internal static string ResolveBackfillDirectory(string workspaceRoot)
        => Path.Combine(ResolveRoot(workspaceRoot), "backfill");
}

