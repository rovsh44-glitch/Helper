using Helper.Api.Hosting;

namespace Helper.Api.Backend.Configuration;

public static class StartupValidationGuards
{
    public static IReadOnlyList<string> GetFatalAlerts(IBackendOptionsCatalog options, ApiRuntimeConfig runtimeConfig)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(runtimeConfig);

        if (BackendOptionsCatalog.IsDevelopmentLikeCurrentEnvironment())
        {
            return GetSourceSurfaceAlerts(runtimeConfig);
        }

        var alerts = new List<string>();

        if (options.Auth.SessionSigningKeySource == "api_key_derived")
        {
            alerts.Add("Non-local startup requires HELPER_SESSION_SIGNING_KEY. API-key-derived session signing is not allowed.");
        }

        if (options.Auth.AllowLocalBootstrap)
        {
            alerts.Add("Non-local startup requires HELPER_AUTH_ALLOW_LOCAL_BOOTSTRAP=false.");
        }

        alerts.AddRange(GetSourceSurfaceAlerts(runtimeConfig));

        return alerts;
    }

    private static IReadOnlyList<string> GetSourceSurfaceAlerts(ApiRuntimeConfig runtimeConfig)
    {
        var alerts = new List<string>();
        var sourceRoot = Path.Combine(runtimeConfig.RootPath, "src");
        var apiSourceRoot = Path.Combine(sourceRoot, "Helper.Api");

        if (IsUnderPath(runtimeConfig.DataRoot, sourceRoot))
        {
            alerts.Add("HELPER_DATA_ROOT may not point inside src/. Move runtime data outside the source tree.");
        }

        var configuredAuthKeysPath = Environment.GetEnvironmentVariable("HELPER_AUTH_KEYS_PATH");
        if (!string.IsNullOrWhiteSpace(configuredAuthKeysPath))
        {
            var fullAuthKeysPath = Path.GetFullPath(configuredAuthKeysPath);
            if (IsUnderPath(fullAuthKeysPath, sourceRoot))
            {
                alerts.Add("HELPER_AUTH_KEYS_PATH may not point inside src/. Runtime auth artifacts must live under HELPER_DATA_ROOT.");
            }
        }

        var sourceAuthKeysPath = Path.Combine(apiSourceRoot, "auth_keys.json");
        if (File.Exists(sourceAuthKeysPath))
        {
            alerts.Add("Source-tree auth artifact detected at src/Helper.Api/auth_keys.json. Remove it and use HELPER_DATA_ROOT/auth_keys.json instead.");
        }

        return alerts;
    }

    private static bool IsUnderPath(string candidatePath, string parentPath)
    {
        var normalizedCandidate = Path.GetFullPath(candidatePath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedParent = Path.GetFullPath(parentPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return normalizedCandidate.Equals(normalizedParent, comparison)
            || normalizedCandidate.StartsWith(normalizedParent + Path.DirectorySeparatorChar, comparison)
            || normalizedCandidate.StartsWith(normalizedParent + Path.AltDirectorySeparatorChar, comparison);
    }
}

