namespace Helper.Runtime.Tests;

public sealed class ArchitectureFitnessTestsPublicMainRuntimeLanes
{
    [Fact]
    public void RuntimeTests_Project_Excludes_CompilePath_Tests()
    {
        var runtimeTestsProject = ResolveWorkspaceFile("test", "Helper.Runtime.Tests", "Helper.Runtime.Tests.csproj");
        var projectText = File.ReadAllText(runtimeTestsProject);

        Assert.Contains("<Compile Remove=\"EvalHarnessTests.cs\" />", projectText, StringComparison.Ordinal);
        Assert.Contains("<Compile Remove=\"TemplateCertificationServiceTests.cs\" />", projectText, StringComparison.Ordinal);
        Assert.Contains("<Compile Remove=\"TemplatePromotionEndToEndAndChaosTests.cs\" />", projectText, StringComparison.Ordinal);
    }

    [Fact]
    public void CompilePath_Project_Owns_Heavy_Template_Test_Surface_And_Is_Not_In_Solution()
    {
        var compilePathProject = ResolveWorkspaceFile("test", "Helper.Runtime.CompilePath.Tests", "Helper.Runtime.CompilePath.Tests.csproj");
        var projectText = File.ReadAllText(compilePathProject);
        var solutionText = File.ReadAllText(ResolveWorkspaceFile("Helper.sln"));

        Assert.Contains("TemplateCertificationServiceTests.cs", projectText, StringComparison.Ordinal);
        Assert.Contains("TemplatePromotionEndToEndAndChaosTests.cs", projectText, StringComparison.Ordinal);
        Assert.True(File.Exists(ResolveWorkspaceFile("test", "Helper.Runtime.CompilePath.Tests", "GenerationCompileGateIntegrationTests.cs")));
        Assert.True(File.Exists(ResolveWorkspaceFile("test", "Helper.Runtime.CompilePath.Tests", "FixLoopCompileGateSmokeTests.cs")));
        Assert.DoesNotContain("Helper.Runtime.CompilePath.Tests", solutionText, StringComparison.Ordinal);
    }

    [Fact]
    public void Public_CompilePath_Runner_Exists()
    {
        var runnerPath = ResolveWorkspaceFile("scripts", "run_compile_path_tests.ps1");
        Assert.True(File.Exists(runnerPath));
    }

    [Fact]
    public void Eval_Project_Owns_Heavy_EvalHarness_Surface_And_Is_Not_In_Solution()
    {
        var evalProject = ResolveWorkspaceFile("test", "Helper.Runtime.Eval.Tests", "Helper.Runtime.Eval.Tests.csproj");
        var projectText = File.ReadAllText(evalProject);
        var solutionText = File.ReadAllText(ResolveWorkspaceFile("Helper.sln"));

        Assert.Contains("EvalHarnessTests.cs", projectText, StringComparison.Ordinal);
        Assert.DoesNotContain("Helper.Runtime.Eval.Tests", solutionText, StringComparison.Ordinal);
    }

    [Fact]
    public void Public_Eval_Runner_Exists()
    {
        var runnerPath = ResolveWorkspaceFile("scripts", "run_eval_harness_tests.ps1");
        Assert.True(File.Exists(runnerPath));
    }

    [Fact]
    public void ArchitectureFitness_Uses_Shared_Workspace_Root_Resolver()
    {
        var filePath = ResolveWorkspaceFile("test", "Helper.Runtime.Tests", "ArchitectureFitnessTests.cs");
        var text = File.ReadAllText(filePath);

        Assert.Contains("TestWorkspaceRoot.ResolveFile", text, StringComparison.Ordinal);
        Assert.DoesNotContain("var current = new DirectoryInfo(AppContext.BaseDirectory)", text, StringComparison.Ordinal);
    }

    private static string ResolveWorkspaceFile(params string[] segments)
    {
        var root = TestWorkspaceRoot.ResolveRoot();
        return Path.Combine(new[] { root }.Concat(segments).ToArray());
    }
}
