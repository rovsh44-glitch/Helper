using Helper.Runtime.Core;
using Helper.Runtime.Generation;

namespace Helper.Runtime.Tests;

public sealed class FixLoopSemanticSafetyTests
{
    [Fact]
    public async Task FixStrategyRunner_BlocksL3Strategy_WhenPolicyDisabled()
    {
        var previousL3 = Environment.GetEnvironmentVariable("HELPER_FIX_ALLOW_L3");
        Environment.SetEnvironmentVariable("HELPER_FIX_ALLOW_L3", "false");
        try
        {
            var planner = new StubPlanner(new FixPlan(new[] { FixStrategyKind.LlmAutoHealer }, 1));
            var verifier = new StubVerifier(new FixVerificationResult(
                Success: true,
                Errors: Array.Empty<BuildError>(),
                Files: Array.Empty<GeneratedFile>(),
                ProjectPath: "out",
                ChangedFiles: Array.Empty<string>(),
                CompilePassed: true));
            var ledger = new StubLedger();
            var metrics = new GenerationMetricsService();
            var runner = new FixStrategyRunner(
                planner,
                verifier,
                ledger,
                metrics,
                new[] { new StubApplier(FixStrategyKind.LlmAutoHealer) },
                new FixSafetyPolicy(),
                new StubInvariantEvaluator(new FixInvariantEvaluationResult(
                    Passed: true,
                    IntentPreservationScore: 1.0,
                    RegressionDetected: false,
                    TestsPassed: true,
                    Violations: Array.Empty<string>())));

            var initial = new GenerationResult(
                Success: false,
                Files: new List<GeneratedFile>(),
                ProjectPath: "out",
                Errors: new List<BuildError> { new("x.cs", 1, "CS0161", "missing return") },
                Duration: TimeSpan.Zero);

            var result = await runner.RunAsync("run_l3_block", new GenerationRequest("fix project", "out"), initial, _ => Task.FromResult(initial));

            Assert.Equal(1, result.Attempts);
            Assert.Single(result.History);
            Assert.False(result.History[0].Applied);
            Assert.Equal(FixSafetyTier.L3RiskySemantic, result.History[0].SafetyTier);
            Assert.Contains("policy_blocked_l3", result.History[0].VerificationFlags ?? Array.Empty<string>());
            Assert.NotEmpty(result.Result.Errors);
        }
        finally
        {
            Environment.SetEnvironmentVariable("HELPER_FIX_ALLOW_L3", previousL3);
        }
    }

    [Fact]
    public async Task FixStrategyRunner_FailsWhenCompilePasses_ButInvariantFails()
    {
        var planner = new StubPlanner(new FixPlan(new[] { FixStrategyKind.DeterministicCompileGate }, 1));
        var applier = new StubApplier(FixStrategyKind.DeterministicCompileGate);
        var verifier = new StubVerifier(new FixVerificationResult(
            Success: true,
            Errors: Array.Empty<BuildError>(),
            Files: new[]
            {
                new GeneratedFile("Main.cs", "public class OtherService { public void Run() { } }")
            },
            ProjectPath: "out",
            ChangedFiles: new[] { "Main.cs" },
            CompilePassed: true));
        var ledger = new StubLedger();
        var metrics = new GenerationMetricsService();
        var runner = new FixStrategyRunner(
            planner,
            verifier,
            ledger,
            metrics,
            new[] { applier },
            new AllowAllPolicy(),
            new StubInvariantEvaluator(new FixInvariantEvaluationResult(
                Passed: false,
                IntentPreservationScore: 0.12,
                RegressionDetected: false,
                TestsPassed: true,
                Violations: new[] { "invariant.intent_drift score=0.12" })));

        var initial = new GenerationResult(
            Success: false,
            Files: new List<GeneratedFile>(),
            ProjectPath: "out",
            Errors: new List<BuildError> { new("x.cs", 1, "CS0161", "missing return") },
            Duration: TimeSpan.Zero);

        var result = await runner.RunAsync("run_invariant_fail", new GenerationRequest("engineering calculator", "out"), initial, _ => Task.FromResult(initial));

        Assert.False(result.Result.Success);
        Assert.Contains(result.Result.Errors, x => x.Code == "INVARIANT_FAIL");
        Assert.Single(result.History);
        Assert.False(result.History[0].InvariantsPassed);
        Assert.True(result.History[0].IntentPreservationScore < 0.2);
    }

    [Fact]
    public void GenerationFixPlanner_PrioritizesByHistoricalWinRate_AfterDeterministic()
    {
        var previousSmoke = Environment.GetEnvironmentVariable("HELPER_SMOKE_PROFILE");
        var previousLlm = Environment.GetEnvironmentVariable("HELPER_ENABLE_LLM_FIX_STRATEGY");
        var previousRegen = Environment.GetEnvironmentVariable("HELPER_ENABLE_REGENERATE_FIX_STRATEGY");
        var previousRuntime = Environment.GetEnvironmentVariable("HELPER_ENABLE_RUNTIME_CONFIG_FIX_STRATEGY");

        Environment.SetEnvironmentVariable("HELPER_SMOKE_PROFILE", "false");
        Environment.SetEnvironmentVariable("HELPER_ENABLE_LLM_FIX_STRATEGY", "true");
        Environment.SetEnvironmentVariable("HELPER_ENABLE_REGENERATE_FIX_STRATEGY", "true");
        Environment.SetEnvironmentVariable("HELPER_ENABLE_RUNTIME_CONFIG_FIX_STRATEGY", "true");
        try
        {
            var history = new StubHistoryProvider(new Dictionary<FixStrategyKind, double>
            {
                [FixStrategyKind.DeterministicCompileGate] = 0.6,
                [FixStrategyKind.LlmAutoHealer] = 0.9,
                [FixStrategyKind.Regenerate] = 0.5,
                [FixStrategyKind.RuntimeConfig] = 0.2
            });
            var planner = new GenerationFixPlanner(history);
            var initial = new GenerationResult(
                Success: false,
                Files: new List<GeneratedFile>(),
                ProjectPath: "out",
                Errors: new List<BuildError> { new("x.cs", 1, "CS0161", "missing return") },
                Duration: TimeSpan.Zero);

            var plan = planner.CreatePlan(new GenerationRequest("fix", "out"), initial);

            Assert.Equal(FixStrategyKind.DeterministicCompileGate, plan.Strategies[0]);
            Assert.Equal(FixStrategyKind.LlmAutoHealer, plan.Strategies[1]);
            Assert.Equal(FixStrategyKind.Regenerate, plan.Strategies[2]);
            Assert.Equal(FixStrategyKind.RuntimeConfig, plan.Strategies[3]);
        }
        finally
        {
            Environment.SetEnvironmentVariable("HELPER_SMOKE_PROFILE", previousSmoke);
            Environment.SetEnvironmentVariable("HELPER_ENABLE_LLM_FIX_STRATEGY", previousLlm);
            Environment.SetEnvironmentVariable("HELPER_ENABLE_REGENERATE_FIX_STRATEGY", previousRegen);
            Environment.SetEnvironmentVariable("HELPER_ENABLE_RUNTIME_CONFIG_FIX_STRATEGY", previousRuntime);
        }
    }

    private sealed class StubPlanner : IFixPlanner
    {
        private readonly FixPlan _plan;

        public StubPlanner(FixPlan plan)
        {
            _plan = plan;
        }

        public FixPlan CreatePlan(GenerationRequest request, GenerationResult initialResult) => _plan;
    }

    private sealed class StubApplier : IFixPatchApplier
    {
        public StubApplier(FixStrategyKind strategy)
        {
            Strategy = strategy;
        }

        public FixStrategyKind Strategy { get; }

        public Task<FixPatchApplyResult> ApplyAsync(FixPatchApplyContext context, CancellationToken ct = default)
        {
            return Task.FromResult(new FixPatchApplyResult(
                Applied: true,
                Success: true,
                Errors: Array.Empty<BuildError>(),
                ChangedFiles: new[] { "Main.cs" },
                Notes: "patched"));
        }
    }

    private sealed class StubVerifier : IFixVerifier
    {
        private readonly FixVerificationResult _result;

        public StubVerifier(FixVerificationResult result)
        {
            _result = result;
        }

        public Task<FixVerificationResult> VerifyAsync(string projectPath, CancellationToken ct = default)
            => Task.FromResult(_result);
    }

    private sealed class StubLedger : IFixAttemptLedger
    {
        public Task<string?> RecordAsync(string runId, FixAttemptRecord attempt, CancellationToken ct = default)
            => Task.FromResult<string?>($"logs/{runId}/attempt_{attempt.Attempt:00}.json");
    }

    private sealed class StubInvariantEvaluator : IFixInvariantEvaluator
    {
        private readonly FixInvariantEvaluationResult _result;

        public StubInvariantEvaluator(FixInvariantEvaluationResult result)
        {
            _result = result;
        }

        public Task<FixInvariantEvaluationResult> EvaluateAsync(
            GenerationRequest request,
            GenerationResult baseline,
            FixStrategyKind strategy,
            FixVerificationResult verification,
            CancellationToken ct = default)
            => Task.FromResult(_result);
    }

    private sealed class AllowAllPolicy : IFixSafetyPolicy
    {
        public FixSafetyTier ResolveTier(FixStrategyKind strategy) => FixSafetyTier.L1SafeDeterministic;

        public bool IsAllowed(FixStrategyKind strategy, GenerationRequest request, GenerationResult current, out string reason)
        {
            reason = "allowed";
            return true;
        }
    }

    private sealed class StubHistoryProvider : IFixStrategyHistoryProvider
    {
        private readonly IReadOnlyDictionary<FixStrategyKind, double> _winRates;

        public StubHistoryProvider(IReadOnlyDictionary<FixStrategyKind, double> winRates)
        {
            _winRates = winRates;
        }

        public IReadOnlyDictionary<FixStrategyKind, double> GetWinRates() => _winRates;
    }
}


