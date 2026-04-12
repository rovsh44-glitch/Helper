using System.Text.Json;
using Helper.Runtime.Core;
using Helper.Runtime.Generation;
using Helper.Runtime.Infrastructure;

namespace Helper.Runtime.Tests;

public sealed class TemplatePromotionEndToEndAndChaosTests
{
    [Fact]
    public async Task PromotionPipeline_E2E_RouteToActivation_PassesForConsoleSmoke()
    {
        using var env = new EnvScope(new Dictionary<string, string?>
        {
            ["HELPER_FF_TEMPLATE_RUNTIME_PROMOTION_V1"] = "true",
            ["HELPER_TEMPLATE_PROMOTION_AUTO_ACTIVATE"] = "true",
            ["HELPER_TEMPLATE_PROMOTION_POST_ACTIVATION_FULL_RECERTIFY"] = "false",
            ["HELPER_TEMPLATE_PROMOTION_FORMAT_MODE"] = "off",
            ["HELPER_TEMPLATE_CERTIFICATION_REQUIRE_SCHEMA_V2"] = "true"
        });
        using var temp = new TempDirectoryScope();

        var templatesRoot = Path.Combine(temp.Path, "templates");
        var generatedProject = Path.Combine(temp.Path, "generated");
        Directory.CreateDirectory(generatedProject);
        const string templateId = "Template_ConsoleTool";
        const string scenario = "console";
        await CreateScenarioProjectAsync(generatedProject, scenario);

        var routing = new StaticRoutingService(templateId);
        var generalizer = new CopyingTemplateGeneralizer(templatesRoot);
        var lifecycle = new TemplateLifecycleService(templatesRoot);
        var dotnet = new DotnetService();
        var compileGate = new GenerationCompileGate(dotnet, new CompileGateRepairService(new UsingInferenceService(new TypeTokenExtractor()), new MethodBodySemanticGuard()));
        var metrics = new GenerationMetricsService();
        var certification = BuildCertificationService(templatesRoot, temp.Path, dotnet, compileGate);
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

        var publishedRoot = Path.Combine(templatesRoot, templateId, result.Version!);
        Assert.True(Directory.Exists(publishedRoot));
        Assert.True(File.Exists(Path.Combine(publishedRoot, "template.json")));
        Assert.True(File.Exists(Path.Combine(publishedRoot, TemplateCertificationStatusStore.StatusFileName)));

        var status = TemplateCertificationStatusStore.TryRead(publishedRoot);
        Assert.NotNull(status);
        Assert.True(status!.Passed);

        var versions = await lifecycle.GetVersionsAsync(templateId);
        Assert.Contains(versions, x => x.IsActive && x.Version == result.Version);
    }

    [Theory]
    [InlineData("Template_EngineeringCalculator", "engineering")]
    [InlineData("Golden_Chess_v2", "chess")]
    [InlineData("Template_PdfEpubConverter", "pdfepub")]
    public async Task PromotionPipeline_E2E_RouteToActivation_PassesForStubbedGoldenSuite(string templateId, string scenario)
    {
        using var env = new EnvScope(new Dictionary<string, string?>
        {
            ["HELPER_FF_TEMPLATE_RUNTIME_PROMOTION_V1"] = "true",
            ["HELPER_TEMPLATE_PROMOTION_AUTO_ACTIVATE"] = "true",
            ["HELPER_TEMPLATE_PROMOTION_POST_ACTIVATION_FULL_RECERTIFY"] = "false",
            ["HELPER_TEMPLATE_PROMOTION_FORMAT_MODE"] = "off",
            ["HELPER_TEMPLATE_CERTIFICATION_REQUIRE_SCHEMA_V2"] = "true"
        });
        using var temp = new TempDirectoryScope("helper_template_e2e_");

        var templatesRoot = Path.Combine(temp.Path, "templates");
        var generatedProject = Path.Combine(temp.Path, "generated");
        Directory.CreateDirectory(generatedProject);
        await CreateScenarioProjectAsync(generatedProject, scenario);

        var routing = new StaticRoutingService(templateId);
        var generalizer = new CopyingTemplateGeneralizer(templatesRoot);
        var lifecycle = new TemplateLifecycleService(templatesRoot);
        var promotion = new GenerationTemplatePromotionService(
            routing,
            generalizer,
            lifecycle,
            new StaticCompileGate(true, Array.Empty<BuildError>()),
            new TemplatePromotionFeatureProfileService(),
            new GenerationMetricsService(),
            new StaticCertificationService(passed: true),
            templatesRoot);

        var result = await promotion.TryPromoteAsync(
            new GenerationRequest($"generate {scenario} golden template", generatedProject, SessionId: $"run_{scenario}"),
            new GenerationResult(true, new List<GeneratedFile>(), generatedProject, new List<BuildError>(), TimeSpan.FromSeconds(1)));

        Assert.True(result.Attempted);
        Assert.True(result.Success, string.Join(" | ", result.Errors));
        Assert.NotNull(result.Version);

        var publishedRoot = Path.Combine(templatesRoot, templateId, result.Version!);
        Assert.True(Directory.Exists(publishedRoot));
        Assert.True(File.Exists(Path.Combine(publishedRoot, "template.json")));
        Assert.True(File.Exists(Path.Combine(publishedRoot, TemplateCertificationStatusStore.StatusFileName)));
        var status = TemplateCertificationStatusStore.TryRead(publishedRoot);
        Assert.NotNull(status);
        Assert.True(status!.Passed);
        Assert.True(File.Exists(status.ReportPath));

        var versions = await lifecycle.GetVersionsAsync(templateId);
        Assert.Contains(versions, x => x.IsActive && x.Version == result.Version);
    }

    [Fact]
    public async Task PromotionPipeline_StrictMode_FormatFailure_BlocksPromotionAndIncrementsMetric()
    {
        using var env = new EnvScope(new Dictionary<string, string?>
        {
            ["HELPER_FF_TEMPLATE_RUNTIME_PROMOTION_V1"] = "true",
            ["HELPER_TEMPLATE_PROMOTION_AUTO_ACTIVATE"] = "false",
            ["HELPER_TEMPLATE_PROMOTION_FORMAT_MODE"] = "strict"
        });
        using var temp = new TempDirectoryScope();

        var templatesRoot = Path.Combine(temp.Path, "templates");
        var generatedProject = Path.Combine(temp.Path, "generated");
        Directory.CreateDirectory(generatedProject);
        await File.WriteAllTextAsync(Path.Combine(generatedProject, "Program.cs"), "namespace Demo; public static class Program { }");

        var metrics = new GenerationMetricsService();
        var service = new GenerationTemplatePromotionService(
            new StaticRoutingService("Template_FormatStrict"),
            new CopyingTemplateGeneralizer(templatesRoot),
            new TemplateLifecycleService(templatesRoot),
            new StaticCompileGate(false, new[] { new BuildError("CompileGate", 0, "FORMAT", "format drift") }),
            new TemplatePromotionFeatureProfileService(),
            metrics,
            new StaticCertificationService(passed: true),
            templatesRoot);

        var outcome = await service.TryPromoteAsync(
            new GenerationRequest("generate strict formatting", generatedProject),
            new GenerationResult(true, new List<GeneratedFile>(), generatedProject, new List<BuildError>(), TimeSpan.FromSeconds(1)));

        Assert.True(outcome.Attempted);
        Assert.False(outcome.Success);
        Assert.Contains(outcome.Errors, x => x.Contains("FORMAT", StringComparison.OrdinalIgnoreCase));

        var snapshot = metrics.GetSnapshot();
        Assert.Equal(1, snapshot.TemplatePromotionFormatStillFailingTotal);
    }

    [Fact]
    public async Task PromotionPipeline_ActivationFailure_TriggersRollbackAttempt()
    {
        using var env = new EnvScope(new Dictionary<string, string?>
        {
            ["HELPER_FF_TEMPLATE_RUNTIME_PROMOTION_V1"] = "true",
            ["HELPER_TEMPLATE_PROMOTION_AUTO_ACTIVATE"] = "true",
            ["HELPER_TEMPLATE_PROMOTION_FORMAT_MODE"] = "off"
        });
        using var temp = new TempDirectoryScope();

        var templatesRoot = Path.Combine(temp.Path, "templates");
        var generatedProject = Path.Combine(temp.Path, "generated");
        Directory.CreateDirectory(generatedProject);
        await File.WriteAllTextAsync(Path.Combine(generatedProject, "Program.cs"), "namespace Demo; public static class Program { }");

        var lifecycle = new FailingActivationLifecycleService();
        var service = new GenerationTemplatePromotionService(
            new StaticRoutingService("Template_Rollback"),
            new CopyingTemplateGeneralizer(templatesRoot),
            lifecycle,
            new StaticCompileGate(true, Array.Empty<BuildError>()),
            new TemplatePromotionFeatureProfileService(),
            new GenerationMetricsService(),
            new StaticCertificationService(passed: true),
            templatesRoot);

        var outcome = await service.TryPromoteAsync(
            new GenerationRequest("generate with rollback", generatedProject),
            new GenerationResult(true, new List<GeneratedFile>(), generatedProject, new List<BuildError>(), TimeSpan.FromSeconds(1)));

        Assert.True(outcome.Attempted);
        Assert.False(outcome.Success);
        Assert.Contains(lifecycle.ActivateCalls, x => x.TemplateId == "Template_Rollback" && !string.Equals(x.Version, "0.9.0", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(lifecycle.ActivateCalls, x => x.TemplateId == "Template_Rollback" && x.Version == "0.9.0");
    }

    [Fact]
    public async Task PromotionPipeline_BuildTimeout_IsReportedAsStructuredCompileFailure()
    {
        using var env = new EnvScope(new Dictionary<string, string?>
        {
            ["HELPER_FF_TEMPLATE_RUNTIME_PROMOTION_V1"] = "true",
            ["HELPER_TEMPLATE_PROMOTION_AUTO_ACTIVATE"] = "false",
            ["HELPER_TEMPLATE_PROMOTION_FORMAT_MODE"] = "off"
        });
        using var temp = new TempDirectoryScope();

        var templatesRoot = Path.Combine(temp.Path, "templates");
        var generatedProject = Path.Combine(temp.Path, "generated");
        Directory.CreateDirectory(generatedProject);
        await File.WriteAllTextAsync(Path.Combine(generatedProject, "Program.cs"), "namespace Demo; public static class Program { }");

        var service = new GenerationTemplatePromotionService(
            new StaticRoutingService("Template_Timeout"),
            new CopyingTemplateGeneralizer(templatesRoot),
            new TemplateLifecycleService(templatesRoot),
            new StaticCompileGate(false, new[] { new BuildError("CompileGate", 0, "GENERATION_STAGE_TIMEOUT", "build stage timed out") }),
            new TemplatePromotionFeatureProfileService(),
            new GenerationMetricsService(),
            new StaticCertificationService(passed: true),
            templatesRoot);

        var outcome = await service.TryPromoteAsync(
            new GenerationRequest("generate timeout case", generatedProject),
            new GenerationResult(true, new List<GeneratedFile>(), generatedProject, new List<BuildError>(), TimeSpan.FromSeconds(1)));

        Assert.True(outcome.Attempted);
        Assert.False(outcome.Success);
        Assert.Contains(outcome.Errors, x => x.Contains("GENERATION_STAGE_TIMEOUT", StringComparison.OrdinalIgnoreCase));
    }

    private static TemplateCertificationService BuildCertificationService(
        string templatesRoot,
        string workspaceRoot,
        IDotnetService dotnet,
        IGenerationCompileGate compileGate)
    {
        var manager = new ProjectTemplateManager(templatesRoot);
        var lifecycle = new TemplateLifecycleService(templatesRoot);
        var buildValidator = new MultiLanguageValidator(new LocalBuildExecutor(dotnet));
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

    private static async Task CreateScenarioProjectAsync(string projectRoot, string scenario)
    {
        switch (scenario.ToLowerInvariant())
        {
            case "engineering":
                await CreateWpfProjectAsync(projectRoot, "EngineeringCalc");
                await File.WriteAllTextAsync(Path.Combine(projectRoot, "CalculatorService.cs"), "namespace EngineeringCalc; public sealed class CalculatorService { public double Sum(double a,double b)=>a+b; }");
                return;
            case "chess":
                await CreateWpfProjectAsync(projectRoot, "GoldenChess");
                await File.WriteAllTextAsync(Path.Combine(projectRoot, "ChessEngine.cs"), "namespace GoldenChess; public sealed class ChessEngine { public string BestMove()=>\"e2e4\"; }");
                return;
            case "pdfepub":
                await CreateWpfProjectAsync(projectRoot, "PdfEpub");
                await File.WriteAllTextAsync(Path.Combine(projectRoot, "ConverterService.cs"), "namespace PdfEpub; public sealed class ConverterService { public string Convert()=>\"ok\"; }");
                return;
            case "console":
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
                return;
            default:
                throw new InvalidOperationException($"Unknown scenario: {scenario}");
        }
    }

    private static async Task CreateWpfProjectAsync(string projectRoot, string ns)
    {
        await File.WriteAllTextAsync(
            Path.Combine(projectRoot, $"{ns}.csproj"),
            """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <EnableWindowsTargeting>true</EnableWindowsTargeting>
    <OutputType>WinExe</OutputType>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>
""");
        await File.WriteAllTextAsync(
            Path.Combine(projectRoot, "App.xaml"),
            $"""
<Application x:Class="{ns}.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             StartupUri="MainWindow.xaml">
</Application>
""");
        await File.WriteAllTextAsync(
            Path.Combine(projectRoot, "App.xaml.cs"),
            $@"namespace {ns};
public partial class App : global::System.Windows.Application
{{
}}");
        await File.WriteAllTextAsync(
            Path.Combine(projectRoot, "MainWindow.xaml"),
            $"""
<Window x:Class="{ns}.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="MainWindow" Height="220" Width="320">
    <Grid>
        <TextBlock Text="Ready" />
    </Grid>
</Window>
""");
        await File.WriteAllTextAsync(
            Path.Combine(projectRoot, "MainWindow.xaml.cs"),
            $@"namespace {ns};
public partial class MainWindow : global::System.Windows.Window
{{
    public MainWindow()
    {{
        InitializeComponent();
    }}
}}");
    }

    private sealed class StaticRoutingService : ITemplateRoutingService
    {
        private readonly string _templateId;

        public StaticRoutingService(string templateId)
        {
            _templateId = templateId;
        }

        public Task<TemplateRoutingDecision> RouteAsync(string prompt, CancellationToken ct = default)
        {
            return Task.FromResult(new TemplateRoutingDecision(
                Matched: true,
                TemplateId: _templateId,
                Confidence: 0.99,
                Candidates: new[] { _templateId },
                Reason: "test"));
        }
    }

    private sealed class CopyingTemplateGeneralizer : ITemplateGeneralizer
    {
        private readonly string _templatesRoot;

        public CopyingTemplateGeneralizer(string templatesRoot)
        {
            _templatesRoot = templatesRoot;
        }

        public async Task<ProjectTemplate?> GeneralizeProjectAsync(string projectPath, string targetTemplateId, CancellationToken ct = default)
        {
            var targetRoot = Path.Combine(_templatesRoot, targetTemplateId.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(targetRoot);

            foreach (var file in Directory.EnumerateFiles(projectPath, "*.*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(projectPath, file);
                var destination = Path.Combine(targetRoot, relative);
                var destinationDirectory = Path.GetDirectoryName(destination);
                if (!string.IsNullOrWhiteSpace(destinationDirectory))
                {
                    Directory.CreateDirectory(destinationDirectory);
                }

                await File.WriteAllBytesAsync(destination, await File.ReadAllBytesAsync(file, ct), ct);
            }

            await File.WriteAllTextAsync(Path.Combine(targetRoot, "template.json"), JsonSerializer.Serialize(new
            {
                Id = targetTemplateId,
                Name = "stub",
                Description = "stub",
                Language = "csharp",
                Tags = new[] { "stub" }
            }), ct);

            return new ProjectTemplate(targetTemplateId, "stub", "stub", "csharp", targetRoot);
        }
    }

    private sealed class StaticCompileGate : IGenerationCompileGate
    {
        private readonly bool _success;
        private readonly IReadOnlyList<BuildError> _errors;

        public StaticCompileGate(bool success, IReadOnlyList<BuildError> errors)
        {
            _success = success;
            _errors = errors;
        }

        public Task<CompileGateResult> ValidateAsync(string rawProjectRoot, CancellationToken ct = default)
        {
            return Task.FromResult(new CompileGateResult(
                Success: _success,
                Errors: _errors,
                CompileWorkspace: Path.Combine(rawProjectRoot, ".compile_gate")));
        }
    }

    private sealed class StaticCertificationService : ITemplateCertificationService
    {
        private readonly bool _passed;

        public StaticCertificationService(bool passed)
        {
            _passed = passed;
        }

        public Task<TemplateCertificationReport> CertifyAsync(string templateId, string version, string? reportPath = null, string? templatePath = null, CancellationToken ct = default)
        {
            return CertifyCoreAsync(templateId, version, reportPath, templatePath, ct);
        }

        public Task<TemplateCertificationGateReport> EvaluateGateAsync(string? reportPath = null, CancellationToken ct = default)
        {
            return Task.FromResult(new TemplateCertificationGateReport(
                GeneratedAtUtc: DateTimeOffset.UtcNow,
                Passed: _passed,
                ReportPath: reportPath ?? Path.Combine(Path.GetTempPath(), "gate.md"),
                CertifiedCount: _passed ? 1 : 0,
                FailedCount: _passed ? 0 : 1,
                Violations: _passed ? Array.Empty<string>() : new[] { "stub-failed" },
                Templates: Array.Empty<TemplateCertificationReport>()));
        }

        private async Task<TemplateCertificationReport> CertifyCoreAsync(string templateId, string version, string? reportPath, string? templatePath, CancellationToken ct)
        {
            var resolvedTemplatePath = Path.GetFullPath(templatePath ?? Path.Combine(Path.GetTempPath(), "certification_stub"));
            Directory.CreateDirectory(resolvedTemplatePath);
            var resolvedReportPath = Path.GetFullPath(reportPath ?? Path.Combine(Path.GetTempPath(), $"report_{Guid.NewGuid():N}.md"));
            Directory.CreateDirectory(Path.GetDirectoryName(resolvedReportPath)!);
            await File.WriteAllTextAsync(resolvedReportPath, $"# Static certification report for {templateId}:{version}", ct);
            await TemplateCertificationStatusStore.WriteAsync(
                resolvedTemplatePath,
                new TemplateCertificationStatus(
                    DateTimeOffset.UtcNow,
                    Passed: _passed,
                    HasCriticalAlerts: !_passed,
                    CriticalAlerts: _passed ? Array.Empty<string>() : new[] { "stub-failed" },
                    ReportPath: resolvedReportPath),
                ct);

            return new TemplateCertificationReport(
                GeneratedAtUtc: DateTimeOffset.UtcNow,
                TemplateId: templateId,
                Version: version,
                TemplatePath: resolvedTemplatePath,
                MetadataSchemaPassed: _passed,
                CompileGatePassed: _passed,
                ArtifactValidationPassed: _passed,
                SmokePassed: _passed,
                SafetyScanPassed: _passed,
                Passed: _passed,
                Errors: _passed ? Array.Empty<string>() : new[] { "stub-failed" },
                SmokeScenarios: Array.Empty<TemplateCertificationSmokeScenario>(),
                ReportPath: resolvedReportPath);
        }
    }

    private sealed class FailingActivationLifecycleService : ITemplateLifecycleService
    {
        private readonly List<TemplateVersionInfo> _versions = new()
        {
            new("0.9.0", Deprecated: false, IsActive: true, Path: "stub")
        };

        public List<(string TemplateId, string Version)> ActivateCalls { get; } = new();

        public Task<IReadOnlyList<TemplateVersionInfo>> GetVersionsAsync(string templateId, CancellationToken ct = default)
        {
            return Task.FromResult<IReadOnlyList<TemplateVersionInfo>>(_versions.ToList());
        }

        public Task<TemplateVersionActivationResult> ActivateVersionAsync(string templateId, string version, CancellationToken ct = default)
        {
            ActivateCalls.Add((templateId, version));
            if (!string.Equals(version, "0.9.0", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(new TemplateVersionActivationResult(false, templateId, "0.9.0", "activation failed"));
            }

            for (var i = 0; i < _versions.Count; i++)
            {
                _versions[i] = _versions[i] with { IsActive = string.Equals(_versions[i].Version, version, StringComparison.OrdinalIgnoreCase) };
            }

            return Task.FromResult(new TemplateVersionActivationResult(true, templateId, version, "ok"));
        }

        public Task<TemplateVersionActivationResult> RollbackAsync(string templateId, CancellationToken ct = default)
        {
            return Task.FromResult(new TemplateVersionActivationResult(true, templateId, "0.9.0", "rolled back"));
        }
    }

    private sealed class EnvScope : IDisposable
    {
        private readonly Dictionary<string, string?> _previous = new(StringComparer.OrdinalIgnoreCase);

        public EnvScope(IReadOnlyDictionary<string, string?> values)
        {
            foreach (var item in values)
            {
                _previous[item.Key] = Environment.GetEnvironmentVariable(item.Key);
                Environment.SetEnvironmentVariable(item.Key, item.Value);
            }
        }

        public void Dispose()
        {
            foreach (var item in _previous)
            {
                Environment.SetEnvironmentVariable(item.Key, item.Value);
            }
        }
    }

    private sealed class TempDirectoryScope : IDisposable
    {
        public TempDirectoryScope()
            : this("helper_template_e2e_")
        {
        }

        public TempDirectoryScope(string prefix)
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }
            }
            catch
            {
                // no-op
            }
        }
    }
}

