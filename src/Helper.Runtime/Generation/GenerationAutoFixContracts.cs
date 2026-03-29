using Helper.Runtime.Core;

namespace Helper.Runtime.Generation;

public enum FixStrategyKind
{
    DeterministicCompileGate,
    RuntimeConfig,
    LlmAutoHealer,
    Regenerate
}

public enum FixSafetyTier
{
    L1SafeDeterministic,
    L2GuardedStructural,
    L3RiskySemantic
}

public sealed record FixPlan(
    IReadOnlyList<FixStrategyKind> Strategies,
    int MaxAttempts);

public sealed record FixPatchApplyContext(
    string RunId,
    GenerationRequest Request,
    GenerationResult CurrentResult,
    Action<string>? OnProgress);

public sealed record FixPatchApplyResult(
    bool Applied,
    bool Success,
    IReadOnlyList<BuildError> Errors,
    IReadOnlyList<string> ChangedFiles,
    string Notes);

public sealed record FixVerificationResult(
    bool Success,
    IReadOnlyList<BuildError> Errors,
    IReadOnlyList<GeneratedFile> Files,
    string ProjectPath,
    IReadOnlyList<string> ChangedFiles,
    bool CompilePassed = false,
    bool TestsPassed = true,
    bool InvariantsPassed = true,
    bool RegressionDetected = false,
    IReadOnlyList<string>? VerificationFlags = null);

public sealed record FixInvariantEvaluationResult(
    bool Passed,
    double IntentPreservationScore,
    bool RegressionDetected,
    bool TestsPassed,
    IReadOnlyList<string> Violations);

public sealed record FixAttemptRecord(
    int Attempt,
    FixStrategyKind Strategy,
    bool Applied,
    bool Success,
    int ErrorsBefore,
    int ErrorsAfter,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    string Notes,
    IReadOnlyList<string> ChangedFiles,
    string? ArtifactPath = null,
    FixSafetyTier SafetyTier = FixSafetyTier.L1SafeDeterministic,
    double IntentPreservationScore = 1.0,
    bool InvariantsPassed = true,
    bool TestsPassed = true,
    bool RegressionDetected = false,
    IReadOnlyList<string>? VerificationFlags = null);

public sealed record FixLoopResult(
    GenerationResult Result,
    int Attempts,
    IReadOnlyList<FixAttemptRecord> History);

public interface IFixPlanner
{
    FixPlan CreatePlan(GenerationRequest request, GenerationResult initialResult);
}

public interface IFixPatchApplier
{
    FixStrategyKind Strategy { get; }

    Task<FixPatchApplyResult> ApplyAsync(
        FixPatchApplyContext context,
        CancellationToken ct = default);
}

public interface IFixVerifier
{
    Task<FixVerificationResult> VerifyAsync(
        string projectPath,
        CancellationToken ct = default);
}

public interface IFixSafetyPolicy
{
    FixSafetyTier ResolveTier(FixStrategyKind strategy);

    bool IsAllowed(
        FixStrategyKind strategy,
        GenerationRequest request,
        GenerationResult current,
        out string reason);
}

public interface IFixInvariantEvaluator
{
    Task<FixInvariantEvaluationResult> EvaluateAsync(
        GenerationRequest request,
        GenerationResult baseline,
        FixStrategyKind strategy,
        FixVerificationResult verification,
        CancellationToken ct = default);
}

public interface IFixStrategyHistoryProvider
{
    IReadOnlyDictionary<FixStrategyKind, double> GetWinRates();
}

public interface IFixAttemptLedger
{
    Task<string?> RecordAsync(
        string runId,
        FixAttemptRecord attempt,
        CancellationToken ct = default);
}

public interface IFixStrategyRunner
{
    Task<FixLoopResult> RunAsync(
        string runId,
        GenerationRequest request,
        GenerationResult initialResult,
        Func<CancellationToken, Task<GenerationResult>> regenerateAsync,
        Action<string>? onProgress = null,
        CancellationToken ct = default);
}

