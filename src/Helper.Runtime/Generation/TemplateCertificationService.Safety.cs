using System.Text.RegularExpressions;

namespace Helper.Runtime.Generation;

public sealed partial class TemplateCertificationService
{
    private static readonly Regex[] SafetyPatterns =
    {
        new("AIza[0-9A-Za-z_-]{35}", RegexOptions.Compiled),
        new("sk-[0-9A-Za-z]{20,}", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new("ghp_[A-Za-z0-9]{20,}", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new("-----BEGIN [A-Z ]*PRIVATE KEY-----", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new("(api[_-]?key|access[_-]?token|session[_-]?token|secret|password)\\s*[:=]\\s*(['\"]?)([A-Za-z0-9_\\-]{8,})\\2", RegexOptions.Compiled | RegexOptions.IgnoreCase)
    };

    private static bool ReadFlag(string envName, bool fallback)
    {
        var raw = Environment.GetEnvironmentVariable(envName);
        return bool.TryParse(raw, out var parsed) ? parsed : fallback;
    }

    private static async Task<IReadOnlyList<string>> RunSafetyScanAsync(string templatePath, CancellationToken ct)
    {
        var findings = new List<string>();
        foreach (var file in EnumerateSafetyScanFiles(templatePath))
        {
            ct.ThrowIfCancellationRequested();

            string[] lines;
            try
            {
                lines = await File.ReadAllLinesAsync(file, ct);
            }
            catch
            {
                continue;
            }

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (string.IsNullOrWhiteSpace(line) || IsAllowlistedSafetyLine(line))
                {
                    continue;
                }

                foreach (var pattern in SafetyPatterns)
                {
                    if (!pattern.IsMatch(line))
                    {
                        continue;
                    }

                    var relative = Path.GetRelativePath(templatePath, file).Replace(Path.DirectorySeparatorChar, '/');
                    findings.Add($"{relative}:{i + 1} matches '{pattern}'");
                    break;
                }
            }
        }

        return findings;
    }

    private static IEnumerable<string> EnumerateSafetyScanFiles(string templatePath)
    {
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".cs",
            ".js",
            ".cjs",
            ".mjs",
            ".ts",
            ".tsx",
            ".py",
            ".json",
            ".config",
            ".yml",
            ".yaml",
            ".xml",
            ".xaml",
            ".toml",
            ".html",
            ".css",
            ".csproj",
            ".props",
            ".targets",
            ".txt",
            ".md",
            ".ps1",
            ".env"
        };

        foreach (var file in Directory.EnumerateFiles(templatePath, "*.*", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
                file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
                file.Contains($"{Path.DirectorySeparatorChar}.compile_gate{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var extension = Path.GetExtension(file);
            if (!allowed.Contains(extension) &&
                !Path.GetFileName(file).StartsWith(".env", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            yield return file;
        }
    }

    private static bool IsAllowlistedSafetyLine(string line)
    {
        var normalized = line.Trim();
        if (normalized.Length == 0 || normalized.StartsWith("#", StringComparison.Ordinal))
        {
            return true;
        }

        return normalized.Contains("<set-me>", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("<redacted>", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("changeme", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("change_me", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("replace_me", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("${", StringComparison.Ordinal);
    }

    private static void TryDeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch
        {
            // keep workspace for diagnostics
        }
    }
}
