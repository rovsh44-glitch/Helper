using Helper.Runtime.Core;
using Helper.Runtime.Generation;
using Helper.Runtime.Infrastructure;

namespace Helper.Runtime.Tests;

public sealed class TemplateCertificationCompileSmokeTests
{
    [Fact]
    public async Task CertifyAsync_Passes_ForValidClassLibraryTemplate_Smoke()
    {
        Environment.SetEnvironmentVariable("HELPER_TEMPLATE_PROMOTION_FORMAT_MODE", "off");
        Environment.SetEnvironmentVariable("HELPER_TEMPLATE_CERTIFICATION_REQUIRE_SCHEMA_V2", "false");
        using var temp = new TempDirectoryScope("helper_template_cert_test_");
        var templatesRoot = Path.Combine(temp.Path, "templates");
        var templateVersionRoot = Path.Combine(templatesRoot, "Template_Simple", "1.0.0");
        Directory.CreateDirectory(templateVersionRoot);

        await File.WriteAllTextAsync(
            Path.Combine(templateVersionRoot, "template.json"),
            """
{
  "Id": "Template_Simple",
  "Name": "Simple Template",
  "Description": "Simple certification template",
  "Language": "csharp",
  "Version": "1.0.0",
  "Tags": ["library"]
}
""");
        await File.WriteAllTextAsync(
            Path.Combine(templateVersionRoot, "Project.csproj"),
            """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <OutputType>Library</OutputType>
  </PropertyGroup>
</Project>
""");
        await File.WriteAllTextAsync(
            Path.Combine(templateVersionRoot, "Class1.cs"),
            """
namespace Demo;

public sealed class Class1
{
    public int Value() => 1;
}
""");

        var lifecycle = new TemplateLifecycleService(templatesRoot);
        await lifecycle.ActivateVersionAsync("Template_Simple", "1.0.0");

        var service = BuildSmokeService(templatesRoot, temp.Path);
        var report = await service.CertifyAsync("Template_Simple", "1.0.0");

        Assert.True(report.Passed, string.Join(" | ", report.Errors));
        Assert.True(File.Exists(report.ReportPath));
    }

    private static TemplateCertificationService BuildSmokeService(string templatesRoot, string workspaceRoot)
    {
        var templateManager = new ProjectTemplateManager(templatesRoot);
        var lifecycle = new TemplateLifecycleService(templatesRoot);
        var dotnet = new DotnetService();
        var compileGate = new GenerationCompileGate(
            dotnet,
            new CompileGateRepairService(new UsingInferenceService(new TypeTokenExtractor()), new MethodBodySemanticGuard()));
        var buildValidator = new MultiLanguageValidator(new LocalBuildExecutor(dotnet));
        var artifactValidator = new ForgeArtifactValidator();
        return new TemplateCertificationService(
            templateManager,
            lifecycle,
            compileGate,
            buildValidator,
            artifactValidator,
            templatesRoot,
            workspaceRoot);
    }
}
