using Helper.Runtime.Core;
using Helper.Runtime.Generation;
using Helper.Runtime.Infrastructure;

namespace Helper.Runtime.Tests;

public sealed class FixLoopCompileGateSmokeTests
{
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
}
