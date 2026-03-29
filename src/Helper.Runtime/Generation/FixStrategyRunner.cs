using Helper.Runtime.Core;

namespace Helper.Runtime.Generation;

public sealed class FixStrategyRunner : IFixStrategyRunner
{
    private readonly IFixPlanner _planner;
    private readonly IFixVerifier _verifier;
    private readonly IFixAttemptLedger _ledger;
    private readonly IGenerationMetricsService _metrics;
    private readonly IReadOnlyDictionary<FixStrategyKind, IFixPatchApplier> _appliers;
    private readonly IFixSafetyPolicy _safetyPolicy;
    private readonly IFixInvariantEvaluator _invariantEvaluator;
    private readonly int _oscillationLimit;

    public FixStrategyRunner(
        IFixPlanner planner,
        IFixVerifier verifier,
        IFixAttemptLedger ledger,
        IGenerationMetricsService metrics,
        IEnumerable<IFixPatchApplier> appliers,
        IFixSafetyPolicy? safetyPolicy = null,
        IFixInvariantEvaluator? invariantEvaluator = null)
    {
        _planner = planner;
        _verifier = verifier;
        _ledger = ledger;
        _metrics = metrics;
        _safetyPolicy = safetyPolicy ?? new FixSafetyPolicy();
        _invariantEvaluator = invariantEvaluator ?? new FixInvariantEvaluator();
        _oscillationLimit = ReadInt("HELPER_FIX_OSCILLATION_LIMIT", 2, 1, 6);
        _appliers = appliers
            .GroupBy(x => x.Strategy)
            .ToDictionary(g => g.Key, g => g.First());
    }

    public async Task<FixLoopResult> RunAsync(
        string runId,
        GenerationRequest request,
        GenerationResult initialResult,
        Func<CancellationToken, Task<GenerationResult>> regenerateAsync,
        Action<string>? onProgress = null,
        CancellationToken ct = default)
    {
        var current = initialResult;
        var baseline = initialResult;
        if (current.Errors.Count == 0)
        {
            return new FixLoopResult(current, 0, Array.Empty<FixAttemptRecord>());
        }

        var plan = _planner.CreatePlan(request, initialResult);
        if (plan.MaxAttempts <= 0 || plan.Strategies.Count == 0)
        {
            return new FixLoopResult(current, 0, Array.Empty<FixAttemptRecord>());
        }

        var history = new List<FixAttemptRecord>();
        var fingerprintRepeats = new Dictionary<string, int>(StringComparer.Ordinal);
        var attempt = 0;
        while (attempt < plan.MaxAttempts && current.Errors.Count > 0)
        {
            var strategy = plan.Strategies[attempt % plan.Strategies.Count];
            attempt++;
            var safetyTier = _safetyPolicy.ResolveTier(strategy);

            if (!_safetyPolicy.IsAllowed(strategy, request, current, out var blockReason))
            {
                var blockedRecord = new FixAttemptRecord(
                    attempt,
                    strategy,
                    Applied: false,
                    Success: false,
                    ErrorsBefore: current.Errors.Count,
                    ErrorsAfter: current.Errors.Count,
                    StartedAtUtc: DateTimeOffset.UtcNow,
                    CompletedAtUtc: DateTimeOffset.UtcNow,
                    Notes: blockReason,
                    ChangedFiles: Array.Empty<string>(),
                    SafetyTier: safetyTier,
                    IntentPreservationScore: 1.0,
                    InvariantsPassed: false,
                    TestsPassed: false,
                    RegressionDetected: false,
                    VerificationFlags: new[] { "policy_blocked_l3" });
                _metrics.RecordAutofixAttempt(success: false);
                var blockedLedgerPath = await _ledger.RecordAsync(runId, blockedRecord, ct);
                history.Add(blockedLedgerPath is null ? blockedRecord : blockedRecord with { ArtifactPath = blockedLedgerPath });
                continue;
            }

            onProgress?.Invoke($"🩹 [AutoFix] Attempt {attempt}/{plan.MaxAttempts}: {strategy}");

            var started = DateTimeOffset.UtcNow;
            var errorsBefore = current.Errors.Count;
            FixAttemptRecord record;
            GenerationResult? regenerated = null;
            FixVerificationResult verification;
            if (strategy == FixStrategyKind.Regenerate)
            {
                (record, regenerated) = await ExecuteRegenerateAttemptAsync(
                    strategy,
                    attempt,
                    errorsBefore,
                    regenerateAsync,
                    current,
                    started,
                    ct);
                current = regenerated ?? current;
                current = current with { HealAttempts = current.HealAttempts + 1 };
                verification = new FixVerificationResult(
                    Success: record.Success,
                    Errors: current.Errors.ToList(),
                    Files: current.Files.ToList(),
                    ProjectPath: current.ProjectPath,
                    ChangedFiles: record.ChangedFiles,
                    CompilePassed: current.Success || current.Errors.Count == 0,
                    TestsPassed: true,
                    InvariantsPassed: true,
                    RegressionDetected: false,
                    VerificationFlags: Array.Empty<string>());
            }
            else
            {
                var patch = await ExecutePatchAttemptAsync(strategy, attempt, errorsBefore, request, current, runId, onProgress, started, ct);
                verification = await _verifier.VerifyAsync(current.ProjectPath, ct);
                current = current with
                {
                    Success = verification.Success,
                    Errors = verification.Errors.ToList(),
                    Files = verification.Files.ToList(),
                    ProjectPath = verification.ProjectPath,
                    HealAttempts = current.HealAttempts + 1,
                    FailureEnvelopes = verification.Success ? null : current.FailureEnvelopes
                };
                record = patch with
                {
                    Success = verification.Success || verification.Errors.Count == 0,
                    ErrorsAfter = verification.Errors.Count,
                    CompletedAtUtc = DateTimeOffset.UtcNow,
                    ChangedFiles = patch.ChangedFiles
                        .Concat(verification.ChangedFiles)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                        .ToList()
                };
            }

            var invariant = await _invariantEvaluator.EvaluateAsync(request, baseline, strategy, verification, ct);
            var verificationFlags = new List<string>();
            if (verification.VerificationFlags is { Count: > 0 })
            {
                verificationFlags.AddRange(verification.VerificationFlags);
            }
            if (invariant.Violations.Count > 0)
            {
                verificationFlags.AddRange(invariant.Violations);
            }

            var compileClean = verification.CompilePassed && verification.Errors.Count == 0;
            if (compileClean && !invariant.Passed)
            {
                var withInvariantError = verification.Errors.ToList();
                withInvariantError.Add(new BuildError(
                    "FixInvariant",
                    0,
                    "INVARIANT_FAIL",
                    string.Join("; ", invariant.Violations)));
                verification = verification with
                {
                    Success = false,
                    Errors = withInvariantError,
                    InvariantsPassed = false
                };
            }

            current = current with
            {
                Success = verification.Success && invariant.Passed,
                Errors = verification.Errors.ToList(),
                Files = verification.Files.ToList(),
                ProjectPath = verification.ProjectPath,
                FailureEnvelopes = (verification.Success && invariant.Passed) ? null : current.FailureEnvelopes
            };
            record = record with
            {
                Success = (verification.Success || verification.Errors.Count == 0) && invariant.Passed,
                ErrorsAfter = verification.Errors.Count,
                CompletedAtUtc = DateTimeOffset.UtcNow,
                ChangedFiles = record.ChangedFiles
                    .Concat(verification.ChangedFiles)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                SafetyTier = safetyTier,
                IntentPreservationScore = invariant.IntentPreservationScore,
                InvariantsPassed = invariant.Passed,
                TestsPassed = invariant.TestsPassed && verification.TestsPassed,
                RegressionDetected = invariant.RegressionDetected || verification.RegressionDetected,
                VerificationFlags = verificationFlags
            };

            var better = record.Success || record.ErrorsAfter < record.ErrorsBefore;
            _metrics.RecordAutofixAttempt(better);

            var ledgerPath = await _ledger.RecordAsync(runId, record, ct);
            var storedRecord = ledgerPath is null ? record : record with { ArtifactPath = ledgerPath };
            history.Add(storedRecord);

            if (current.Errors.Count == 0)
            {
                break;
            }

            var fingerprint = BuildErrorFingerprint(current.Errors);
            if (!string.IsNullOrWhiteSpace(fingerprint))
            {
                var repeatCount = fingerprintRepeats.TryGetValue(fingerprint, out var seen) ? seen + 1 : 1;
                fingerprintRepeats[fingerprint] = repeatCount;
                if (repeatCount >= _oscillationLimit)
                {
                    onProgress?.Invoke($"🛑 [AutoFix] Stopped due to oscillation: repeating error fingerprint '{fingerprint}'.");
                    break;
                }
            }
        }

        return new FixLoopResult(current, attempt, history);
    }

    private async Task<FixAttemptRecord> ExecutePatchAttemptAsync(
        FixStrategyKind strategy,
        int attempt,
        int errorsBefore,
        GenerationRequest request,
        GenerationResult current,
        string runId,
        Action<string>? onProgress,
        DateTimeOffset started,
        CancellationToken ct)
    {
        if (!_appliers.TryGetValue(strategy, out var applier))
        {
            return new FixAttemptRecord(
                attempt,
                strategy,
                Applied: false,
                Success: false,
                ErrorsBefore: errorsBefore,
                ErrorsAfter: errorsBefore,
                StartedAtUtc: started,
                CompletedAtUtc: DateTimeOffset.UtcNow,
                Notes: $"Patch applier for strategy '{strategy}' is not registered.",
                ChangedFiles: Array.Empty<string>());
        }

        var context = new FixPatchApplyContext(runId, request, current, onProgress);
        var applied = await applier.ApplyAsync(context, ct);

        return new FixAttemptRecord(
            attempt,
            strategy,
            Applied: applied.Applied,
            Success: applied.Success,
            ErrorsBefore: errorsBefore,
            ErrorsAfter: errorsBefore,
            StartedAtUtc: started,
            CompletedAtUtc: DateTimeOffset.UtcNow,
            Notes: applied.Notes,
            ChangedFiles: applied.ChangedFiles);
    }

    private static async Task<(FixAttemptRecord Record, GenerationResult Result)> ExecuteRegenerateAttemptAsync(
        FixStrategyKind strategy,
        int attempt,
        int errorsBefore,
        Func<CancellationToken, Task<GenerationResult>> regenerateAsync,
        GenerationResult current,
        DateTimeOffset started,
        CancellationToken ct)
    {
        var regenerated = await regenerateAsync(ct);
        var errorsAfter = regenerated.Errors.Count;
        var success = regenerated.Success || errorsAfter == 0;
        var notes = success
            ? "Regeneration strategy produced a build-clean result."
            : $"Regeneration strategy finished with {errorsAfter} error(s).";

        var record = new FixAttemptRecord(
            attempt,
            strategy,
            Applied: true,
            Success: success,
            ErrorsBefore: errorsBefore,
            ErrorsAfter: errorsAfter,
            StartedAtUtc: started,
            CompletedAtUtc: DateTimeOffset.UtcNow,
            Notes: notes,
            ChangedFiles: ChangedFiles(current, regenerated));
        return (record, regenerated);
    }

    private static IReadOnlyList<string> ChangedFiles(GenerationResult before, GenerationResult after)
    {
        var beforeSet = before.Files.Select(x => x.RelativePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var afterSet = after.Files.Select(x => x.RelativePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        beforeSet.SymmetricExceptWith(afterSet);
        var changed = beforeSet.ToList();
        changed.Sort(StringComparer.OrdinalIgnoreCase);
        return changed;
    }

    private static string BuildErrorFingerprint(IReadOnlyList<BuildError> errors)
    {
        if (errors.Count == 0)
        {
            return string.Empty;
        }

        var compact = errors
            .Select(x => $"{x.Code}:{x.File}:{x.Line}")
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase);
        return string.Join("|", compact);
    }

    private static int ReadInt(string envName, int fallback, int min, int max)
    {
        var raw = Environment.GetEnvironmentVariable(envName);
        if (!int.TryParse(raw, out var parsed))
        {
            parsed = fallback;
        }

        return Math.Clamp(parsed, min, max);
    }
}

