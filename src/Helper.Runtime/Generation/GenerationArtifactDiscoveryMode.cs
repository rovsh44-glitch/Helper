namespace Helper.Runtime.Generation;

public enum GenerationArtifactDiscoveryMode
{
    Mixed = 0,
    WorkspaceOnly = 1,
    CanonicalDataRoot = 2,
    LegacyFallback = 3
}

internal static class GenerationArtifactDiscoveryModeResolver
{
    internal const string EnvName = "HELPER_PARITY_DISCOVERY_MODE";

    internal static GenerationArtifactDiscoveryMode ResolveDefault(bool explicitWorkspaceRootProvided)
    {
        if (TryParse(Environment.GetEnvironmentVariable(EnvName), out var mode))
        {
            return mode;
        }

        return explicitWorkspaceRootProvided
            ? GenerationArtifactDiscoveryMode.WorkspaceOnly
            : GenerationArtifactDiscoveryMode.Mixed;
    }

    internal static bool TryParse(string? raw, out GenerationArtifactDiscoveryMode mode)
    {
        mode = GenerationArtifactDiscoveryMode.Mixed;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        return Enum.TryParse(raw.Trim(), ignoreCase: true, out mode);
    }
}

