using Helper.GeneratedArtifactValidation.Contracts;

namespace Helper.GeneratedArtifactValidation.Core;

public sealed class ArtifactValidationService
{
    private readonly GenerationPathSanitizer _pathSanitizer;
    private readonly GeneratedFileAstValidator _astValidator;
    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public ArtifactValidationService()
        : this(new GenerationPathSanitizer(), new GeneratedFileAstValidator())
    {
    }

    internal ArtifactValidationService(
        GenerationPathSanitizer pathSanitizer,
        GeneratedFileAstValidator astValidator)
    {
        _pathSanitizer = pathSanitizer;
        _astValidator = astValidator;
    }

    public async Task<ArtifactValidationReport> ValidateFixtureDirectoryAsync(string fixtureRoot, CancellationToken ct = default)
    {
        if (!Directory.Exists(fixtureRoot))
        {
            return new ArtifactValidationReport(
                false,
                [$"Fixture root '{fixtureRoot}' does not exist."],
                Array.Empty<string>(),
                Array.Empty<GeneratedArtifactPlaceholderFinding>(),
                Array.Empty<ArtifactFileValidationResult>());
        }

        var manifestPath = Path.Combine(fixtureRoot, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            return new ArtifactValidationReport(
                false,
                [$"Fixture manifest '{manifestPath}' does not exist."],
                Array.Empty<string>(),
                Array.Empty<GeneratedArtifactPlaceholderFinding>(),
                Array.Empty<ArtifactFileValidationResult>());
        }

        var manifest = await ValidationJson.LoadArtifactFixtureManifestAsync(manifestPath, ct);
        if (manifest is null || manifest.Files.Count == 0)
        {
            return new ArtifactValidationReport(
                false,
                [$"Fixture manifest '{manifestPath}' is empty or invalid."],
                Array.Empty<string>(),
                Array.Empty<GeneratedArtifactPlaceholderFinding>(),
                Array.Empty<ArtifactFileValidationResult>());
        }

        var errors = new List<string>();
        var warnings = new List<string>();
        var allFindings = new List<GeneratedArtifactPlaceholderFinding>();
        var fileReports = new List<ArtifactFileValidationResult>();
        var fixtureRootFullPath = EnsureTrailingSeparator(Path.GetFullPath(fixtureRoot));

        foreach (var file in manifest.Files)
        {
            ct.ThrowIfCancellationRequested();

            var fileErrors = new List<string>();
            var fileWarnings = new List<string>();
            var declaredPath = string.IsNullOrWhiteSpace(file.RelativePath) ? file.FixtureFile : file.RelativePath;
            var pathResult = _pathSanitizer.SanitizeRelativePath(declaredPath);
            var fixturePathResult = _pathSanitizer.SanitizeRelativePath(file.FixtureFile);
            var displayPath = pathResult.SanitizedPath ?? declaredPath;

            fileErrors.AddRange(pathResult.Errors.Select(x => $"Path: {x}"));
            fileWarnings.AddRange(pathResult.Warnings.Select(x => $"Path: {x}"));
            fileErrors.AddRange(fixturePathResult.Errors.Select(x => $"FixtureFile: {x}"));
            fileWarnings.AddRange(fixturePathResult.Warnings.Select(x => $"FixtureFile: {x}"));

            if (!fixturePathResult.IsValid || string.IsNullOrWhiteSpace(fixturePathResult.SanitizedPath))
            {
                var invalidFixtureReport = new ArtifactFileValidationResult(displayPath, false, fileErrors, fileWarnings, Array.Empty<GeneratedArtifactPlaceholderFinding>());
                fileReports.Add(invalidFixtureReport);
                errors.AddRange(fileErrors.Select(x => $"{displayPath}: {x}"));
                warnings.AddRange(fileWarnings.Select(x => $"{displayPath}: {x}"));
                continue;
            }

            var fixtureFilePath = Path.GetFullPath(Path.Combine(fixtureRootFullPath, fixturePathResult.SanitizedPath));
            if (!fixtureFilePath.StartsWith(fixtureRootFullPath, PathComparison))
            {
                fileErrors.Add($"Fixture file '{file.FixtureFile}' resolves outside the fixture root.");
                var outOfRootReport = new ArtifactFileValidationResult(displayPath, false, fileErrors, fileWarnings, Array.Empty<GeneratedArtifactPlaceholderFinding>());
                fileReports.Add(outOfRootReport);
                errors.AddRange(fileErrors.Select(x => $"{displayPath}: {x}"));
                warnings.AddRange(fileWarnings.Select(x => $"{displayPath}: {x}"));
                continue;
            }

            if (!File.Exists(fixtureFilePath))
            {
                fileErrors.Add($"Fixture file '{file.FixtureFile}' does not exist.");
                var missingReport = new ArtifactFileValidationResult(displayPath, false, fileErrors, fileWarnings, Array.Empty<GeneratedArtifactPlaceholderFinding>());
                fileReports.Add(missingReport);
                errors.AddRange(fileErrors.Select(x => $"{displayPath}: {x}"));
                warnings.AddRange(fileWarnings.Select(x => $"{displayPath}: {x}"));
                continue;
            }

            var content = await File.ReadAllTextAsync(fixtureFilePath, ct);
            var findings = GeneratedArtifactPlaceholderScanner.ScanContent(displayPath, content);
            if (findings.Count > 0)
            {
                fileErrors.Add($"Placeholder scan found {findings.Count} issue(s).");
            }

            if (pathResult.IsValid)
            {
                var astResult = _astValidator.ValidateFile(displayPath, content, file.Role, file.ExpectedNamespace, file.ExpectedTypeName);
                fileErrors.AddRange(astResult.Errors.Select(x => $"AST: {x}"));
                fileWarnings.AddRange(astResult.Warnings.Select(x => $"AST: {x}"));
            }

            var report = new ArtifactFileValidationResult(displayPath, fileErrors.Count == 0, fileErrors, fileWarnings, findings);
            fileReports.Add(report);
            allFindings.AddRange(findings);
            errors.AddRange(fileErrors.Select(x => $"{displayPath}: {x}"));
            warnings.AddRange(fileWarnings.Select(x => $"{displayPath}: {x}"));
        }

        return new ArtifactValidationReport(errors.Count == 0, errors, warnings, allFindings, fileReports);
    }

    private static string EnsureTrailingSeparator(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return path;
        }

        if (path.EndsWith(Path.DirectorySeparatorChar) || path.EndsWith(Path.AltDirectorySeparatorChar))
        {
            return path;
        }

        return path + Path.DirectorySeparatorChar;
    }
}
