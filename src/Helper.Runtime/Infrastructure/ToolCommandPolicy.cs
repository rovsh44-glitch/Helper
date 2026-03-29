using Helper.Runtime.Core;

namespace Helper.Runtime.Infrastructure;

internal static class ToolCommandPolicy
{
    public static bool IsMcpExtensionManifest(ExtensionManifest manifest)
    {
        return manifest.Transport == ExtensionTransport.Stdio &&
               manifest.Category is ExtensionCategory.External or ExtensionCategory.Experimental &&
               string.Equals(manifest.ProviderType, "mcp", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsMcpCommandAllowed(string command)
    {
        var normalized = command.Trim().ToLowerInvariant();
        var forbiddenPatterns = new[] { "&&", "||", ";", "|", ">", "<", "$(", "`" };
        if (forbiddenPatterns.Any(normalized.Contains))
        {
            return false;
        }

        var allowPrefixes = new[] { "node", "npx", "python", "python3", "dotnet", "pwsh", "powershell", "cmd" };
        return allowPrefixes.Any(prefix => normalized == prefix || normalized.StartsWith(prefix + " "));
    }

    public static bool ShouldActivateManifest(ExtensionManifest manifest, bool certificationMode)
    {
        var disabledIds = ReadNameSet("HELPER_EXTENSION_DISABLED_IDS");
        foreach (var legacyDisabledId in ReadNameSet("HELPER_MCP_DISABLED_SERVERS"))
        {
            disabledIds.Add(legacyDisabledId);
        }

        if (disabledIds.Contains("*") || disabledIds.Contains(manifest.Id))
        {
            return false;
        }

        if (certificationMode && manifest.DisabledInCertificationMode)
        {
            return false;
        }

        var enabledIds = ReadNameSet("HELPER_EXTENSION_ENABLED_IDS");
        var explicitlyEnabled = enabledIds.Contains("*") || enabledIds.Contains(manifest.Id);
        if (manifest.Category == ExtensionCategory.Experimental &&
            !explicitlyEnabled &&
            !ReadBoolEnvironmentFlag("HELPER_ENABLE_EXPERIMENTAL_EXTENSIONS"))
        {
            return false;
        }

        if (manifest.DefaultEnabled)
        {
            return true;
        }

        return explicitlyEnabled;
    }

    public static bool TryValidateMcpServerEnvironment(ExtensionManifest manifest, out string reason)
    {
        foreach (var variable in manifest.RequiredEnv)
        {
            var envValue = Environment.GetEnvironmentVariable(variable);
            if (string.IsNullOrWhiteSpace(envValue) || LooksLikePlaceholder(envValue))
            {
                reason = $"required env '{variable}' is not configured";
                return false;
            }
        }

        reason = string.Empty;
        return true;
    }

    public static bool TryResolveExecutablePath(string command, out string resolvedPath)
    {
        resolvedPath = command;
        if (string.IsNullOrWhiteSpace(command))
        {
            return false;
        }

        if (Path.IsPathRooted(command) || command.Contains(Path.DirectorySeparatorChar) || command.Contains(Path.AltDirectorySeparatorChar))
        {
            resolvedPath = Path.GetFullPath(command);
            return File.Exists(resolvedPath);
        }

        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var extensions = ResolveExecutableExtensions(command);
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var extension in extensions)
            {
                var candidate = Path.Combine(directory, command + extension);
                if (File.Exists(candidate))
                {
                    resolvedPath = candidate;
                    return true;
                }
            }
        }

        return false;
    }

    public static bool ShouldSkipCommandBootstrap(string command, IReadOnlyList<string> args, out string reason)
    {
        if (string.Equals(command, "npx", StringComparison.OrdinalIgnoreCase) &&
            args.Any(arg => arg.Contains("@modelcontextprotocol/", StringComparison.OrdinalIgnoreCase)) &&
            !ReadBoolEnvironmentFlag("HELPER_MCP_ENABLE_NPX_BOOTSTRAP"))
        {
            reason = "npx registry bootstrap disabled by default; set HELPER_MCP_ENABLE_NPX_BOOTSTRAP=1 to allow";
            return true;
        }

        reason = string.Empty;
        return false;
    }

    public static bool IsCertificationMode()
    {
        return ReadBoolEnvironmentFlag("HELPER_CERT_MODE") || ReadBoolEnvironmentFlag("HELPER_EVAL_MODE");
    }

    private static bool LooksLikePlaceholder(string value)
    {
        var normalized = value.Trim();
        return normalized.StartsWith("INSERT_", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith("YOUR_", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("PLACEHOLDER", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith("<", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> ResolveExecutableExtensions(string command)
    {
        if (Path.HasExtension(command))
        {
            return new[] { string.Empty };
        }

        if (!OperatingSystem.IsWindows())
        {
            return new[] { string.Empty };
        }

        var pathExt = Environment.GetEnvironmentVariable("PATHEXT");
        if (string.IsNullOrWhiteSpace(pathExt))
        {
            return new[] { ".exe", ".cmd", ".bat", ".ps1" };
        }

        return pathExt
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(ext => ext.StartsWith(".", StringComparison.Ordinal))
            .Concat(new[] { string.Empty })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool ReadBoolEnvironmentFlag(string name)
    {
        var raw = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        return raw.Equals("1", StringComparison.OrdinalIgnoreCase) ||
               raw.Equals("true", StringComparison.OrdinalIgnoreCase) ||
               raw.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
               raw.Equals("on", StringComparison.OrdinalIgnoreCase);
    }

    private static HashSet<string> ReadNameSet(string envName)
    {
        var raw = Environment.GetEnvironmentVariable(envName);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        return raw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static value => value.Trim().ToLowerInvariant())
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}

