using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace Helper.Runtime.Infrastructure;

internal static class HostCommandResolver
{
    public static string GetPowerShellExecutable()
    {
        var candidates = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? new[]
            {
                "pwsh.exe",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"),
                "powershell.exe",
                "pwsh",
                "powershell"
            }
            : new[] { "pwsh", "powershell" };

        return ResolveFirstExisting(candidates)
            ?? (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "powershell.exe" : "pwsh");
    }

    public static string GetCommandShellExecutable()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var comSpec = Environment.GetEnvironmentVariable("ComSpec");
            if (!string.IsNullOrWhiteSpace(comSpec) && File.Exists(comSpec))
            {
                return comSpec;
            }

            return "cmd.exe";
        }

        return ResolveFirstExisting("/bin/bash", "bash", "/bin/sh", "sh") ?? "sh";
    }

    public static string GetPreferredShellName()
    {
        string shellPath = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? GetPowerShellExecutable()
            : GetCommandShellExecutable();
        return Path.GetFileNameWithoutExtension(shellPath);
    }

    private static string? ResolveFirstExisting(params string[] candidates)
    {
        foreach (string candidate in candidates.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            string? resolved = ResolveExecutable(candidate);
            if (!string.IsNullOrWhiteSpace(resolved))
            {
                return resolved;
            }
        }

        return null;
    }

    private static string? ResolveExecutable(string candidate)
    {
        if (Path.IsPathRooted(candidate))
        {
            return File.Exists(candidate) ? candidate : null;
        }

        var paths = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (string directory in paths)
        {
            foreach (string fileName in ExpandCandidateNames(candidate))
            {
                string fullPath = Path.Combine(directory, fileName);
                if (File.Exists(fullPath))
                {
                    return fullPath;
                }
            }
        }

        return null;
    }

    private static IEnumerable<string> ExpandCandidateNames(string candidate)
    {
        yield return candidate;

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || Path.HasExtension(candidate))
        {
            yield break;
        }

        var pathExt = (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT;.COM")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (string extension in pathExt)
        {
            yield return candidate + extension.ToLowerInvariant();
            yield return candidate + extension.ToUpperInvariant();
        }
    }
}

