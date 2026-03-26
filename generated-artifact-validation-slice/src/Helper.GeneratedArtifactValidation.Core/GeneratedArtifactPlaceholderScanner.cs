using System.Text.RegularExpressions;
using Helper.GeneratedArtifactValidation.Contracts;

namespace Helper.GeneratedArtifactValidation.Core;

public static class GeneratedArtifactPlaceholderScanner
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs",
        ".xaml",
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
        ".toml",
        ".html",
        ".css",
        ".csproj",
        ".props",
        ".targets",
        ".txt",
        ".md",
        ".ps1"
    };

    private static readonly PlaceholderRule[] LineRules = GenerationFallbackRegistry.PlaceholderLineRules
        .Select(static rule => new PlaceholderRule(rule.RuleId, new Regex(rule.Pattern, rule.Options)))
        .ToArray();

    private static readonly Regex EmptyMethodRegex = new(
        @"(?ms)^\s*(?:public|private|internal|protected|static|virtual|override|sealed|partial|async|new|\s)+[^\r\n{;=]+\([^;{}]*\)\s*\{\s*\}",
        RegexOptions.Compiled);

    public static IReadOnlyList<GeneratedArtifactPlaceholderFinding> ScanGeneratedFiles(IEnumerable<GeneratedFile> files)
    {
        var findings = new List<GeneratedArtifactPlaceholderFinding>();
        foreach (var file in files)
        {
            findings.AddRange(ScanContent(file.RelativePath, file.Content));
        }

        return findings;
    }

    public static async Task<IReadOnlyList<GeneratedArtifactPlaceholderFinding>> ScanDirectoryAsync(string rootPath, CancellationToken ct = default)
    {
        var findings = new List<GeneratedArtifactPlaceholderFinding>();
        foreach (var file in Directory.EnumerateFiles(rootPath, "*.*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            if (IsIgnoredPath(file) || !IsSupportedFile(file))
            {
                continue;
            }

            string content;
            try
            {
                content = await File.ReadAllTextAsync(file, ct);
            }
            catch
            {
                continue;
            }

            var relativePath = Path.GetRelativePath(rootPath, file).Replace(Path.DirectorySeparatorChar, '/');
            findings.AddRange(ScanContent(relativePath, content));
        }

        return findings;
    }

    public static IReadOnlyList<GeneratedArtifactPlaceholderFinding> ScanContent(string relativePath, string content)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || string.IsNullOrEmpty(content))
        {
            return Array.Empty<GeneratedArtifactPlaceholderFinding>();
        }

        var findings = new List<GeneratedArtifactPlaceholderFinding>();
        var extension = Path.GetExtension(relativePath);
        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

        for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            var line = lines[lineIndex];
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            foreach (var rule in LineRules)
            {
                if (!rule.Pattern.IsMatch(line))
                {
                    continue;
                }

                findings.Add(new GeneratedArtifactPlaceholderFinding(
                    relativePath,
                    lineIndex + 1,
                    rule.RuleId,
                    line.Trim()));
            }
        }

        if (string.Equals(extension, ".cs", StringComparison.OrdinalIgnoreCase))
        {
            foreach (Match match in EmptyMethodRegex.Matches(content))
            {
                findings.Add(new GeneratedArtifactPlaceholderFinding(
                    relativePath,
                    CountLines(content, match.Index),
                    "empty-method-body",
                    TrimEvidence(match.Value)));
            }
        }

        return findings
            .DistinctBy(static finding => (finding.RelativePath, finding.LineNumber, finding.RuleId, finding.Evidence))
            .ToArray();
    }

    private static bool IsSupportedFile(string path) => AllowedExtensions.Contains(Path.GetExtension(path));

    private static bool IsIgnoredPath(string path)
    {
        return path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
               path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
               path.Contains($"{Path.DirectorySeparatorChar}.compile_gate{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
    }

    private static int CountLines(string content, int offset)
    {
        var boundedOffset = Math.Clamp(offset, 0, content.Length);
        var lineCount = 1;
        for (var index = 0; index < boundedOffset; index++)
        {
            if (content[index] == '\n')
            {
                lineCount++;
            }
        }

        return lineCount;
    }

    private static string TrimEvidence(string value)
    {
        var trimmed = value.Replace(Environment.NewLine, " ", StringComparison.Ordinal).Trim();
        return trimmed.Length <= 160 ? trimmed : trimmed[..160];
    }

    private sealed record PlaceholderRule(string RuleId, Regex Pattern);
}

