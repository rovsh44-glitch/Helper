using System.Globalization;
using Helper.Runtime.Infrastructure;

namespace Helper.Runtime.Generation;

public sealed class GenerationPathPolicy : IGenerationPathPolicy
{
    private readonly IIdentifierSanitizer _identifierSanitizer;
    private readonly string _workspaceRoot;

    public GenerationPathPolicy(IIdentifierSanitizer identifierSanitizer)
    {
        _identifierSanitizer = identifierSanitizer;
        _workspaceRoot = HelperWorkspacePathResolver.ResolveHelperRoot();
    }

    public GenerationRunContext CreateRunContext(string outputRoot, string projectName)
    {
        var outputFullPath = Path.GetFullPath(outputRoot);
        EnsureNotRuntimeSourceZone(outputFullPath);

        var runId = BuildRunId();
        var sanitizedProjectName = _identifierSanitizer.SanitizeProjectName(projectName);
        var rawRunRoot = Path.Combine(outputFullPath, "generated_raw", runId);
        var validatedRunRoot = Path.Combine(outputFullPath, "generated_validated", runId);
        var rawProjectRoot = Path.Combine(rawRunRoot, sanitizedProjectName);
        var validatedProjectRoot = Path.Combine(validatedRunRoot, sanitizedProjectName);

        Directory.CreateDirectory(rawProjectRoot);
        Directory.CreateDirectory(validatedRunRoot);

        return new GenerationRunContext(
            runId,
            _workspaceRoot,
            outputFullPath,
            rawRunRoot,
            validatedRunRoot,
            rawProjectRoot,
            validatedProjectRoot);
    }

    private void EnsureNotRuntimeSourceZone(string outputFullPath)
    {
        var srcRoot = Path.Combine(_workspaceRoot, "src");
        var testRoot = Path.Combine(_workspaceRoot, "test");

        if (IsSubpath(outputFullPath, srcRoot) || IsSubpath(outputFullPath, testRoot))
        {
            throw new InvalidOperationException(
                $"Generation output '{outputFullPath}' is forbidden. Use sandbox paths outside 'src' and 'test'.");
        }
    }

    private static bool IsSubpath(string candidate, string basePath)
    {
        var normalizedCandidate = EnsureTrailingSeparator(Path.GetFullPath(candidate));
        var normalizedBase = EnsureTrailingSeparator(Path.GetFullPath(basePath));
        return normalizedCandidate.StartsWith(normalizedBase, StringComparison.OrdinalIgnoreCase);
    }

    private static string EnsureTrailingSeparator(string value)
    {
        if (value.EndsWith(Path.DirectorySeparatorChar))
        {
            return value;
        }

        return value + Path.DirectorySeparatorChar;
    }

    private static string BuildRunId()
    {
        var utc = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        return $"{utc}_{suffix}";
    }

}

