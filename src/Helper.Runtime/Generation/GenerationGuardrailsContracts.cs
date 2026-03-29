using Helper.Runtime.Core;

namespace Helper.Runtime.Generation;

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

public sealed record SchemaValidationResult(
    bool IsValid,
    IReadOnlyList<string> Errors);

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

public sealed record CompileGateResult(
    bool Success,
    IReadOnlyList<BuildError> Errors,
    string CompileWorkspace);

public sealed record MethodBodySafetyResult(
    bool UsedFallback,
    string Body,
    IReadOnlyList<string> Diagnostics);

public sealed record GenerationRunContext(
    string RunId,
    string WorkspaceRoot,
    string OutputRoot,
    string RawRunRoot,
    string ValidatedRunRoot,
    string RawProjectRoot,
    string ValidatedProjectRoot);

public sealed record GenerationRunReport(
    string RunId,
    string Prompt,
    string ModelRoute,
    string ProjectName,
    DateTime StartedAtUtc,
    DateTime CompletedAtUtc,
    string RawProjectRoot,
    string? ValidatedProjectRoot,
    int FileCount,
    int MethodCount,
    int RetryCount,
    bool BlueprintAccepted,
    bool CompileGatePassed,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings,
    bool? RouteMatched = null,
    string? RoutedTemplateId = null,
    double? RouteConfidence = null,
    bool? GoldenTemplateMatched = null,
    bool? GoldenTemplateEligible = null,
    string? WorkloadClass = null,
    IReadOnlyDictionary<string, double>? StageDurationsSec = null,
    IReadOnlyList<string>? PlaceholderFindings = null,
    bool? ArtifactValidationPassed = null,
    bool? SmokePassed = null,
    IReadOnlyList<TemplateCertificationSmokeScenario>? SmokeScenarios = null);

public sealed record GenerationMetricsSnapshot(
    long GenerationRunsTotal,
    long GenerationValidationFailTotal,
    long GenerationCompileFailTotal,
    long GenerationPromotedTotal,
    long GenerationGoldenTemplateHitTotal,
    long GenerationGoldenTemplateMissTotal,
    long GenerationTimeoutRoutingTotal,
    long GenerationTimeoutForgeTotal,
    long GenerationTimeoutSynthesisTotal,
    long GenerationTimeoutAutofixTotal,
    long GenerationTimeoutUnknownTotal,
    long GenerationAutofixAttemptsTotal,
    long GenerationAutofixSuccessTotal,
    long GenerationAutofixFailTotal,
    long TemplatePromotionAttemptTotal,
    long TemplatePromotionSuccessTotal,
    long TemplatePromotionFailTotal,
    long TemplatePromotionFormatFixAppliedTotal,
    long TemplatePromotionFormatStillFailingTotal,
    IReadOnlyDictionary<string, long> TemplatePromotionFailReasonTotals);

public enum GenerationTimeoutStage
{
    Routing,
    Forge,
    Synthesis,
    Autofix,
    Unknown
}

public sealed record GenerationPromotionRequest(
    string SourceValidatedProjectPath,
    string TargetProjectPath,
    bool RunContractTests = true,
    bool RunUnitTests = true,
    bool RunSecurityScan = true,
    bool GenerateDiff = true);

public sealed record GenerationPromotionResult(
    bool Success,
    IReadOnlyList<string> Steps,
    IReadOnlyList<string> Errors,
    string? DiffPath);

public interface IIdentifierSanitizer
{
    string SanitizeProjectName(string value);
    string SanitizeNamespace(string value);
    string SanitizeTypeName(string value);
    string SanitizeMethodName(string value);
}

public interface IGenerationPathSanitizer
{
    PathSanitizationResult SanitizeRelativePath(string? rawPath);
}

public interface IMethodSignatureValidator
{
    MethodSignatureValidationResult Validate(string? signature);
}

public interface IMethodSignatureNormalizer
{
    MethodSignatureNormalizationResult Normalize(string? signature, FileRole role, string? preferredMethodName = null);
}

public interface ITypeTokenExtractor
{
    IReadOnlyCollection<string> ExtractFromSignature(string signature);
}

public interface IUsingInferenceService
{
    IReadOnlyCollection<string> InferUsings(
        string rootNamespace,
        string relativePath,
        FileRole role,
        IReadOnlyList<ArbanMethodTask> methods);

    string? ResolveUsingForTypeToken(string token);
}

public interface IMethodBodySemanticGuard
{
    MethodBodySafetyResult Guard(string methodSignature, string methodBody);
}

public interface IBlueprintJsonSchemaValidator
{
    string SchemaJson { get; }
    SchemaValidationResult ValidateRawJson(string? rawJson);
}

public interface IBlueprintContractValidator
{
    BlueprintValidationResult ValidateAndNormalize(SwarmBlueprint blueprint);
}

public interface IGeneratedFileAstValidator
{
    GeneratedFileValidationResult ValidateFile(
        string relativePath,
        string code,
        FileRole role,
        string expectedNamespace,
        string expectedTypeName);
}

public interface IGenerationPathPolicy
{
    GenerationRunContext CreateRunContext(string outputRoot, string projectName);
}

public interface IGenerationCompileGate
{
    Task<CompileGateResult> ValidateAsync(string rawProjectRoot, CancellationToken ct = default);
}

public interface ICompileGateRepairService
{
    Task<bool> TryApplyRepairsAsync(
        string compileWorkspace,
        IReadOnlyList<BuildError> errors,
        CancellationToken ct = default);
}

public interface IGenerationValidationReportWriter
{
    Task WriteAsync(GenerationRunReport report, CancellationToken ct = default);
}

public interface IGenerationHealthReporter
{
    Task AppendAsync(GenerationRunReport report, CancellationToken ct = default);
}

public interface IGenerationMetricsService
{
    void RecordRun();
    void RecordValidationFail();
    void RecordCompileFail();
    void RecordPromoted();
    void RecordGoldenTemplateRoute(bool hit);
    void RecordTimeout(GenerationTimeoutStage stage);
    void RecordAutofixAttempt(bool success);
    void RecordTemplatePromotionAttempt(bool success, string? reasonCode = null);
    void RecordTemplatePromotionFormatFixResult(bool success);
    GenerationMetricsSnapshot GetSnapshot();
}

public interface IGenerationStageTimeoutPolicy
{
    TimeSpan Resolve(GenerationTimeoutStage stage);
}

public interface IGenerationPromotionService
{
    Task<GenerationPromotionResult> PromoteAsync(GenerationPromotionRequest request, CancellationToken ct = default);
}

public enum CompileGateFormatMode
{
    Strict,
    Advisory,
    Off
}

public sealed record TemplatePromotionFeatureProfile(
    bool RuntimePromotionEnabled,
    bool AutoActivateEnabled,
    CompileGateFormatMode FormatMode,
    bool RouterV2Enabled,
    double RouterMinConfidence);

public interface ITemplatePromotionFeatureProfileService
{
    TemplatePromotionFeatureProfile GetCurrent();
}

public sealed record TemplateCertificationSmokeScenario(
    string Id,
    string Description,
    bool Passed,
    string Details);

public sealed record TemplateCertificationReport(
    DateTimeOffset GeneratedAtUtc,
    string TemplateId,
    string Version,
    string TemplatePath,
    bool MetadataSchemaPassed,
    bool CompileGatePassed,
    bool ArtifactValidationPassed,
    bool SmokePassed,
    bool SafetyScanPassed,
    bool Passed,
    IReadOnlyList<string> Errors,
    IReadOnlyList<TemplateCertificationSmokeScenario> SmokeScenarios,
    string ReportPath,
    bool? PlaceholderScanPassed = null,
    IReadOnlyList<string>? PlaceholderFindings = null);

public sealed record TemplateCertificationGateReport(
    DateTimeOffset GeneratedAtUtc,
    bool Passed,
    string ReportPath,
    int CertifiedCount,
    int FailedCount,
    IReadOnlyList<string> Violations,
    IReadOnlyList<TemplateCertificationReport> Templates);

public interface ITemplateCertificationService
{
    Task<TemplateCertificationReport> CertifyAsync(
        string templateId,
        string version,
        string? reportPath = null,
        string? templatePath = null,
        CancellationToken ct = default);

    Task<TemplateCertificationGateReport> EvaluateGateAsync(
        string? reportPath = null,
        CancellationToken ct = default);
}

