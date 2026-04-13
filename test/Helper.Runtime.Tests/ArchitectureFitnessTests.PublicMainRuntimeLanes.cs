namespace Helper.Runtime.Tests;

public sealed class ArchitectureFitnessTestsPublicMainRuntimeLanes
{
    [Fact]
    public void RuntimeTests_Project_Uses_Manifest_Based_RuntimeLane_Layout()
    {
        var runtimeTestsProject = ResolveWorkspaceFile("test", "Helper.Runtime.Tests", "Helper.Runtime.Tests.csproj");
        var projectText = File.ReadAllText(runtimeTestsProject);

        Assert.Contains("Helper.Runtime.Tests.RuntimeLane.props", projectText, StringComparison.Ordinal);
        Assert.Contains("Helper.Runtime.Tests.ApiLane.props", projectText, StringComparison.Ordinal);
        Assert.Contains("<Compile Remove=\"@(RuntimeApiLaneCompile)\" />", projectText, StringComparison.Ordinal);
    }

    [Fact]
    public void CertificationCompile_And_CompilePath_Projects_Are_Both_Present_In_CanonicalSolution()
    {
        var certificationCompileProject = ResolveWorkspaceFile("test", "Helper.Runtime.Certification.Compile.Tests", "Helper.Runtime.Certification.Compile.Tests.csproj");
        var projectText = File.ReadAllText(certificationCompileProject);
        var solutionText = File.ReadAllText(ResolveWorkspaceFile("Helper.sln"));

        Assert.Contains("TemplateCertificationServiceTests.cs", projectText, StringComparison.Ordinal);
        Assert.Contains("TemplatePromotionEndToEndAndChaosTests.cs", projectText, StringComparison.Ordinal);
        Assert.Contains("DotnetServiceTraceBehaviorTests.cs", projectText, StringComparison.Ordinal);
        Assert.Contains("Helper.Runtime.CompilePath.Tests", solutionText, StringComparison.Ordinal);
        Assert.Contains("Helper.Runtime.Certification.Compile.Tests", solutionText, StringComparison.Ordinal);
    }

    [Fact]
    public void Public_CertificationCompile_Runner_Exists_And_Hardens_Cleanup()
    {
        var runnerPath = ResolveWorkspaceFile("scripts", "run_certification_compile_tests.ps1");
        Assert.True(File.Exists(runnerPath));

        var runnerText = File.ReadAllText(runnerPath);
        Assert.Contains("Acquire-LaneLock", runnerText, StringComparison.Ordinal);
        Assert.Contains("Release-LaneLock", runnerText, StringComparison.Ordinal);
        Assert.Contains("taskkill /PID", runnerText, StringComparison.Ordinal);
        Assert.Contains("helper_template_e2e_", runnerText, StringComparison.Ordinal);
        Assert.Contains("helper_template_cert_test_", runnerText, StringComparison.Ordinal);
        Assert.Contains("Waiting for active certification compile lane to release lock", runnerText, StringComparison.Ordinal);
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
