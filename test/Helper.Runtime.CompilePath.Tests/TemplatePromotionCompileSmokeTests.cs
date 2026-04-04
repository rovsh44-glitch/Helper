using System.Text.Json;
using Helper.Runtime.Core;
using Helper.Runtime.Generation;
using Helper.Runtime.Infrastructure;

namespace Helper.Runtime.Tests;

public sealed class TemplatePromotionCompileSmokeTests
{
    [Fact]
    public async Task PromotionPipeline_E2E_RouteToActivation_PassesForConsoleSmoke()
    {
        using var env = new EnvScope(new Dictionary<string, string?>
        {
            ["HELPER_FF_TEMPLATE_RUNTIME_PROMOTION_V1"] = "true",
            ["HELPER_TEMPLATE_PROMOTION_AUTO_ACTIVATE"] = "true",
            ["HELPER_TEMPLATE_PROMOTION_FORMAT_MODE"] = "off",
            ["HELPER_TEMPLATE_CERTIFICATION_REQUIRE_SCHEMA_V2"] = "true"
        });
        using var temp = new TempDirectoryScope("helper_template_e2e_");

        var templatesRoot = Path.Combine(temp.Path, "templates");
        var generatedProject = Path.Combine(temp.Path, "generated");
        Directory.CreateDirectory(generatedProject);
        await CreateConsoleScenarioProjectAsync(generatedProject);

        var routing = new StaticRoutingService("Template_ConsoleTool");
        var generalizer = new CopyingTemplateGeneralizer(templatesRoot);
        var lifecycle = new TemplateLifecycleService(templatesRoot);
        var compileGate = new GenerationCompileGate(
            new DotnetService(),
            new CompileGateRepairService(new UsingInferenceService(new TypeTokenExtractor()), new MethodBodySemanticGuard()));
        var metrics = new GenerationMetricsService();
        var certification = BuildCertificationService(templatesRoot, temp.Path, compileGate);
        var promotion = new GenerationTemplatePromotionService(
            routing,
            generalizer,
            lifecycle,
            compileGate,
            new TemplatePromotionFeatureProfileService(),
            metrics,
            certification,
            templatesRoot);

        var result = await promotion.TryPromoteAsync(
            new GenerationRequest("generate console golden template", generatedProject, SessionId: "run_console"),
            new GenerationResult(true, new List<GeneratedFile>(), generatedProject, new List<BuildError>(), TimeSpan.FromSeconds(1)));

        Assert.True(result.Attempted);
        Assert.True(result.Success, string.Join(" | ", result.Errors));
        Assert.NotNull(result.Version);

        var publishedRoot = Path.Combine(templatesRoot, "Template_ConsoleTool", result.Version!);
        Assert.True(Directory.Exists(publishedRoot));
        Assert.True(File.Exists(Path.Combine(publishedRoot, "template.json")));
        Assert.True(File.Exists(Path.Combine(publishedRoot, TemplateCertificationStatusStore.StatusFileName)));

        var status = TemplateCertificationStatusStore.TryRead(publishedRoot);
        Assert.NotNull(status);
        Assert.True(status!.Passed);

        var versions = await lifecycle.GetVersionsAsync("Template_ConsoleTool");
        Assert.Contains(versions, x => x.IsActive && x.Version == result.Version);
    }

    private static TemplateCertificationService BuildCertificationService(
        string templatesRoot,
        string workspaceRoot,
        IGenerationCompileGate compileGate)
    {
        var manager = new ProjectTemplateManager(templatesRoot);
        var lifecycle = new TemplateLifecycleService(templatesRoot);
        var buildValidator = new MultiLanguageValidator(new LocalBuildExecutor(new DotnetService()));
        var artifactValidator = new ForgeArtifactValidator();
        return new TemplateCertificationService(
            manager,
            lifecycle,
            compileGate,
            buildValidator,
            artifactValidator,
            templatesRoot,
            workspaceRoot);
    }

    private static async Task CreateConsoleScenarioProjectAsync(string projectRoot)
    {
        await File.WriteAllTextAsync(
            Path.Combine(projectRoot, "ConsoleTool.csproj"),
            """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <OutputType>Exe</OutputType>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>
""");
        await File.WriteAllTextAsync(
            Path.Combine(projectRoot, "Program.cs"),
            """
namespace ConsoleTool;
internal static class Program
{
    private static void Main()
    {
        System.Console.WriteLine("ok");
    }
}
""");
    }

    private sealed class StaticRoutingService : ITemplateRoutingService
    {
        private readonly string _templateId;

        public StaticRoutingService(string templateId)
        {
            _templateId = templateId;
        }

        public Task<TemplateRoutingDecision> RouteAsync(string prompt, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new TemplateRoutingDecision(
                true,
                _templateId,
                0.95,
                new[] { _templateId },
                "compile-path smoke route"));
        }
    }

    private sealed class CopyingTemplateGeneralizer : ITemplateGeneralizer
    {
        private readonly string _templatesRoot;

        public CopyingTemplateGeneralizer(string templatesRoot)
        {
            _templatesRoot = templatesRoot;
        }

        public async Task<ProjectTemplate?> GeneralizeProjectAsync(
            string projectPath,
            string targetTemplateId,
            CancellationToken cancellationToken = default)
        {
            var targetRoot = Path.Combine(_templatesRoot, targetTemplateId.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(targetRoot);
            CopyDirectory(projectPath, targetRoot);

            var manifestPath = Path.Combine(targetRoot, "template.json");
            var manifest = new
            {
                Id = targetTemplateId,
                Name = targetTemplateId,
                Description = "compile-path promotion smoke template",
                Language = "csharp",
                Version = "1.0.0",
                Tags = new[] { "smoke" }
            };
            await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);

            return new ProjectTemplate(
                Id: targetTemplateId,
                Name: targetTemplateId,
                Description: "compile-path promotion smoke template",
                Language: "csharp",
                RootPath: targetRoot,
                Tags: new[] { "smoke" },
                Version: "1.0.0");
        }

        private static void CopyDirectory(string sourceRoot, string targetRoot)
        {
            Directory.CreateDirectory(targetRoot);
            foreach (var directory in Directory.GetDirectories(sourceRoot, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(sourceRoot, directory);
                Directory.CreateDirectory(Path.Combine(targetRoot, relative));
            }

            foreach (var file in Directory.GetFiles(sourceRoot, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(sourceRoot, file);
                var destination = Path.Combine(targetRoot, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(file, destination, true);
            }
        }
    }
}
