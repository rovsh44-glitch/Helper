using Helper.Runtime.Core;
using Helper.Runtime.Generation;
using Helper.Runtime.Infrastructure;

namespace Helper.Runtime.Tests;

public sealed class FixLoopOrchestratorTests
{
    [Fact]
    public void GenerationFixPlanner_SmokeProfile_UsesOnlyDeterministicSingleAttempt()
    {
        var previous = Environment.GetEnvironmentVariable("HELPER_SMOKE_PROFILE");
        Environment.SetEnvironmentVariable("HELPER_SMOKE_PROFILE", "true");

        try
        {
            var planner = new GenerationFixPlanner();
            var request = new GenerationRequest("test", "out");
            var initial = new GenerationResult(
                Success: false,
                Files: new List<GeneratedFile>(),
                ProjectPath: "out",
                Errors: new List<BuildError> { new("a.cs", 1, "CS0001", "error") },
                Duration: TimeSpan.FromSeconds(1));

            var plan = planner.CreatePlan(request, initial);
            Assert.Equal(1, plan.MaxAttempts);
            Assert.Single(plan.Strategies);
            Assert.Equal(FixStrategyKind.DeterministicCompileGate, plan.Strategies[0]);
        }
        finally
        {
            Environment.SetEnvironmentVariable("HELPER_SMOKE_PROFILE", previous);
        }
    }

    [Fact]
    public async Task FixStrategyRunner_AppliesDeterministicAttempt_AndRecordsLedger()
    {
        var planner = new StubFixPlanner(new FixPlan(new[] { FixStrategyKind.DeterministicCompileGate }, 1));
        var applier = new StubFixPatchApplier(
            FixStrategyKind.DeterministicCompileGate,
            new FixPatchApplyResult(
                Applied: true,
                Success: true,
                Errors: Array.Empty<BuildError>(),
                ChangedFiles: new[] { "Calculator.cs" },
                Notes: "fixed"));
        var verifier = new StubFixVerifier(new FixVerificationResult(
            Success: true,
            Errors: Array.Empty<BuildError>(),
            Files: new[] { new GeneratedFile("Calculator.cs", "namespace Demo;") },
            ProjectPath: "out",
            ChangedFiles: new[] { "Calculator.cs" }));
        var ledger = new StubFixLedger();
        var metrics = new GenerationMetricsService();
        var runner = new FixStrategyRunner(planner, verifier, ledger, metrics, new[] { applier });

        var initial = new GenerationResult(
            Success: false,
            Files: new List<GeneratedFile>(),
            ProjectPath: "out",
            Errors: new List<BuildError> { new("Calculator.cs", 1, "CS0161", "missing return") },
            Duration: TimeSpan.FromSeconds(1));
        var request = new GenerationRequest("gen", "out");

        var result = await runner.RunAsync(
            "run_1",
            request,
            initial,
            _ => Task.FromResult(initial));

        Assert.True(result.Result.Success);
        Assert.Empty(result.Result.Errors);
        Assert.Equal(1, result.Attempts);
        Assert.Single(result.History);
        Assert.Single(ledger.Entries);
        Assert.Equal(FixStrategyKind.DeterministicCompileGate, ledger.Entries[0].Strategy);
    }

    [Fact]
    public async Task DeterministicCompileGatePatchApplier_PatchesProjectFiles_FromCompileWorkspace()
    {
        Environment.SetEnvironmentVariable("HELPER_REQUIRE_GENERATION_FORMAT", "false");
        Environment.SetEnvironmentVariable("HELPER_TEMPLATE_PROMOTION_FORMAT_MODE", "off");
        Environment.SetEnvironmentVariable("HELPER_COMPILE_GATE_MAX_REPAIRS", "3");
        var root = Path.Combine(Path.GetTempPath(), "helper_fix_applier_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(root, "Calculator.cs"),
                @"namespace Demo;
public class Calculator
{
    public int Evaluate(int x)
    {
        if (x > 0) return x;
    }
}");

            var compileGate = new GenerationCompileGate(
                new DotnetService(),
                new CompileGateRepairService(new UsingInferenceService(new TypeTokenExtractor()), new MethodBodySemanticGuard()));
            var applier = new DeterministicCompileGatePatchApplier(compileGate);
            var initial = new GenerationResult(
                Success: false,
                Files: new List<GeneratedFile>(),
                ProjectPath: root,
                Errors: new List<BuildError> { new("Calculator.cs", 6, "CS0161", "not all code paths return") },
                Duration: TimeSpan.Zero);
            var context = new FixPatchApplyContext("run", new GenerationRequest("calc", root), initial, null);

            var applied = await applier.ApplyAsync(context);

            Assert.True(applied.Applied);
            Assert.Contains("Calculator.cs", applied.ChangedFiles, StringComparer.OrdinalIgnoreCase);
            var patched = await File.ReadAllTextAsync(Path.Combine(root, "Calculator.cs"));
            Assert.Contains("return", patched, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task RuntimeConfigPatchApplier_RewritesConflictingPorts_InLaunchSettings()
    {
        var root = Path.Combine(Path.GetTempPath(), "helper_runtime_config_fix_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Properties"));
        var launchSettingsPath = Path.Combine(root, "Properties", "launchSettings.json");
        await File.WriteAllTextAsync(
            launchSettingsPath,
            """
{
  "profiles": {
    "Demo": {
      "commandName": "Project",
      "applicationUrl": "http://localhost:5000;https://localhost:5001"
    }
  }
}
""");

        try
        {
            var applier = new RuntimeConfigPatchApplier();
            var initial = new GenerationResult(
                Success: false,
                Files: new List<GeneratedFile>(),
                ProjectPath: root,
                Errors: new List<BuildError>
                {
                    new("Runtime", 0, "ADDR_IN_USE", "Failed to bind to address http://127.0.0.1:5000: address already in use.")
                },
                Duration: TimeSpan.Zero);
            var context = new FixPatchApplyContext("run", new GenerationRequest("fix ports", root), initial, null);

            var result = await applier.ApplyAsync(context);

            Assert.True(result.Applied);
            Assert.Contains("Properties/launchSettings.json", result.ChangedFiles, StringComparer.OrdinalIgnoreCase);
            var rewritten = await File.ReadAllTextAsync(launchSettingsPath);
            Assert.DoesNotContain("localhost:5000", rewritten, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task RuntimeConfigPatchApplier_CreatesMissingDirectories_FromErrorMessages()
    {
        var root = Path.Combine(Path.GetTempPath(), "helper_runtime_dir_fix_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var missingFilePath = Path.Combine(root, "logs", "pipeline", "trace.log");
        var missingDirectory = Path.GetDirectoryName(missingFilePath)!;

        try
        {
            var applier = new RuntimeConfigPatchApplier();
            var initial = new GenerationResult(
                Success: false,
                Files: new List<GeneratedFile>(),
                ProjectPath: root,
                Errors: new List<BuildError>
                {
                    new("Runtime", 0, "PATH_NOT_FOUND", $"Could not find a part of the path '{missingFilePath}'.")
                },
                Duration: TimeSpan.Zero);
            var context = new FixPatchApplyContext("run", new GenerationRequest("fix path", root), initial, null);

            var result = await applier.ApplyAsync(context);

            Assert.True(result.Applied);
            Assert.True(Directory.Exists(missingDirectory));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private sealed class StubFixPlanner : IFixPlanner
    {
        private readonly FixPlan _plan;

        public StubFixPlanner(FixPlan plan)
        {
            _plan = plan;
        }

        public FixPlan CreatePlan(GenerationRequest request, GenerationResult initialResult) => _plan;
    }

    private sealed class StubFixPatchApplier : IFixPatchApplier
    {
        private readonly FixPatchApplyResult _result;

        public StubFixPatchApplier(FixStrategyKind strategy, FixPatchApplyResult result)
        {
            Strategy = strategy;
            _result = result;
        }

        public FixStrategyKind Strategy { get; }

        public Task<FixPatchApplyResult> ApplyAsync(FixPatchApplyContext context, CancellationToken ct = default)
            => Task.FromResult(_result);
    }

    private sealed class StubFixVerifier : IFixVerifier
    {
        private readonly FixVerificationResult _result;

        public StubFixVerifier(FixVerificationResult result)
        {
            _result = result;
        }

        public Task<FixVerificationResult> VerifyAsync(string projectPath, CancellationToken ct = default)
            => Task.FromResult(_result);
    }

    private sealed class StubFixLedger : IFixAttemptLedger
    {
        public List<FixAttemptRecord> Entries { get; } = new();

        public Task<string?> RecordAsync(string runId, FixAttemptRecord attempt, CancellationToken ct = default)
        {
            Entries.Add(attempt);
            return Task.FromResult<string?>($"logs/{runId}/attempt_{attempt.Attempt}.json");
        }
    }
}

