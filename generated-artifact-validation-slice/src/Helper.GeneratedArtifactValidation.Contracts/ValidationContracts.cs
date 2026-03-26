using Helper.Generation.Contracts;

namespace Helper.GeneratedArtifactValidation.Contracts;

public sealed record PathSanitizationResult(
    bool IsValid,
    string? SanitizedPath,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings);

public sealed record MethodSignatureValidationResult(
    bool IsValid,
    string? NormalizedSignature,
    IReadOnlyList<string> Errors);

public sealed record MethodSignatureNormalizationResult(
    bool IsValid,
    string? Signature,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings);

public sealed record BlueprintValidationResult(
    bool IsValid,
    SwarmBlueprint? Blueprint,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings);

public sealed record GeneratedFileValidationResult(
    bool IsValid,
    string SanitizedCode,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings);

public sealed record GeneratedArtifactPlaceholderFinding(
    string RelativePath,
    int? LineNumber,
    string RuleId,
    string Evidence)
{
    public string ToDisplayString()
    {
        var location = LineNumber.HasValue
            ? $"{RelativePath}:{LineNumber.Value}"
            : RelativePath;
        return $"{location} [{RuleId}] {Evidence}";
    }
}

public sealed record ArtifactFixtureFile(
    string FixtureFile,
    string RelativePath,
    FileRole Role,
    string ExpectedNamespace,
    string ExpectedTypeName);

public sealed record ArtifactFixtureManifest(
    string RootNamespace,
    List<ArtifactFixtureFile> Files);

public sealed record ArtifactFileValidationResult(
    string RelativePath,
    bool Success,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<GeneratedArtifactPlaceholderFinding> PlaceholderFindings);

public sealed record ArtifactValidationReport(
    bool Success,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<GeneratedArtifactPlaceholderFinding> PlaceholderFindings,
    IReadOnlyList<ArtifactFileValidationResult> Files);

public sealed record CompileGateResult(
    bool Success,
    IReadOnlyList<BuildError> Errors,
    string CompileWorkspace);
