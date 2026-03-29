using Helper.Runtime.Core;

namespace Helper.Runtime.Generation;

internal static class CompileGateRepairDiagnostics
{
    internal static IEnumerable<string> EnumerateCodeFiles(string root)
    {
        return Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));
    }

    internal static IReadOnlyList<DiagnosticReference> ResolveDiagnosticReferences(
        string compileWorkspace,
        IReadOnlyList<BuildError> errors,
        IReadOnlySet<string> targetCodes)
    {
        var references = new List<DiagnosticReference>();
        foreach (var error in errors)
        {
            var code = ExtractCode(error);
            if (!targetCodes.Contains(code))
            {
                continue;
            }

            var filePath = ResolveFilePath(compileWorkspace, error.File);
            if (string.IsNullOrWhiteSpace(filePath) || !filePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            references.Add(new DiagnosticReference(filePath, Math.Max(error.Line, 0), code, error));
        }

        return references;
    }

    internal static IEnumerable<string> EnumerateTargetCodeFiles(IEnumerable<DiagnosticReference> references)
    {
        return references
            .Select(static reference => reference.FilePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase);
    }

    internal static string ExtractCode(BuildError error)
    {
        if (!string.IsNullOrWhiteSpace(error.Code) && !string.Equals(error.Code, "FAIL", StringComparison.OrdinalIgnoreCase))
        {
            return error.Code;
        }

        var match = CompileGateRepairPatterns.CsCodeRegex.Match(error.Message ?? string.Empty);
        return match.Success ? match.Value : error.Code;
    }

    internal static string? ResolveFilePath(string compileWorkspace, string? rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return null;
        }

        var trimmed = rawPath.Trim();
        if (File.Exists(trimmed))
        {
            return trimmed;
        }

        var combined = Path.Combine(compileWorkspace, trimmed);
        if (File.Exists(combined))
        {
            return combined;
        }

        var fileName = Path.GetFileName(trimmed);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        return Directory.EnumerateFiles(compileWorkspace, fileName, SearchOption.AllDirectories)
            .FirstOrDefault();
    }
}

internal sealed record DiagnosticReference(string FilePath, int Line, string Code, BuildError Error);

