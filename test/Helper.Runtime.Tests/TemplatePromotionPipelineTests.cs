using System.Text.Json;
using Helper.Runtime.Core;
using Helper.Runtime.Generation;
using Helper.Runtime.Infrastructure;

namespace Helper.Runtime.Tests;

public sealed class TemplatePromotionPipelineTests
{
    [Fact]
    public async Task GenerationTemplatePromotionService_DisabledFlag_DoesNotAttemptPromotion()
    {
        var previous = Environment.GetEnvironmentVariable("HELPER_FF_TEMPLATE_RUNTIME_PROMOTION_V1");
        Environment.SetEnvironmentVariable("HELPER_FF_TEMPLATE_RUNTIME_PROMOTION_V1", "false");

        try
        {
            using var temp = new TempDirectoryScope();
            var service = BuildService(temp.Path, new StubTemplateRoutingService("Template_Test"), new StubCompileGate(success: true));
            var result = await service.TryPromoteAsync(
                new GenerationRequest("prompt", temp.Path),
                new GenerationResult(true, new List<GeneratedFile>(), temp.Path, new List<BuildError>(), TimeSpan.Zero));

            Assert.False(result.Attempted);
            Assert.False(result.Success);
        }
        finally
        {
            Environment.SetEnvironmentVariable("HELPER_FF_TEMPLATE_RUNTIME_PROMOTION_V1", previous);
        }
    }

    [Fact]
    public async Task GenerationTemplatePromotionService_PromotesVersion_AndActivatesTemplate()
    {
        var previousPromotion = Environment.GetEnvironmentVariable("HELPER_FF_TEMPLATE_RUNTIME_PROMOTION_V1");
        var previousActivate = Environment.GetEnvironmentVariable("HELPER_TEMPLATE_PROMOTION_AUTO_ACTIVATE");
        var previousRecertify = Environment.GetEnvironmentVariable("HELPER_TEMPLATE_PROMOTION_POST_ACTIVATION_FULL_RECERTIFY");
        Environment.SetEnvironmentVariable("HELPER_FF_TEMPLATE_RUNTIME_PROMOTION_V1", "true");
        Environment.SetEnvironmentVariable("HELPER_TEMPLATE_PROMOTION_AUTO_ACTIVATE", "true");
        Environment.SetEnvironmentVariable("HELPER_TEMPLATE_PROMOTION_POST_ACTIVATION_FULL_RECERTIFY", "false");

        try
        {
            using var temp = new TempDirectoryScope();
            var generatedProject = Path.Combine(temp.Path, "generated_project");
            Directory.CreateDirectory(generatedProject);
            await File.WriteAllTextAsync(Path.Combine(generatedProject, "Calculator.cs"), "namespace Demo; public class Calculator {}");

            var certification = new StubTemplateCertificationService();
            var service = BuildService(
                temp.Path,
                new StubTemplateRoutingService("Template_Calculator"),
                new StubCompileGate(success: true),
                certification);
            var outcome = await service.TryPromoteAsync(
                new GenerationRequest("сгенерируй инженерный калькулятор", generatedProject),
                new GenerationResult(true, new List<GeneratedFile>(), generatedProject, new List<BuildError>(), TimeSpan.FromSeconds(1)));

            Assert.True(outcome.Attempted);
            Assert.True(outcome.Success);
            Assert.Equal("Template_Calculator", outcome.TemplateId);
            Assert.Equal("1.0.0", outcome.Version);

            var metadataPath = Path.Combine(temp.Path, "Template_Calculator", "1.0.0", "template.json");
            Assert.True(File.Exists(metadataPath));
            var metadata = JsonSerializer.Deserialize<JsonElement>(await File.ReadAllTextAsync(metadataPath));
            Assert.Equal("Template_Calculator", metadata.GetProperty("Id").GetString());
            Assert.Equal("1.0.0", metadata.GetProperty("Version").GetString());
            var publishedRoot = Path.Combine(temp.Path, "Template_Calculator", "1.0.0");
            var status = TemplateCertificationStatusStore.TryRead(publishedRoot);
            Assert.NotNull(status);
            Assert.True(status!.Passed);
            Assert.True(File.Exists(status.ReportPath));
            Assert.Equal(1, certification.CertifyCallCount);
            Assert.Collection(
                certification.ObservedTemplatePaths,
                path => Assert.Contains(Path.Combine("Template_Calculator", "candidates", "1.0.0"), path, StringComparison.OrdinalIgnoreCase));

            var lifecycle = new TemplateLifecycleService(temp.Path);
            var versions = await lifecycle.GetVersionsAsync("Template_Calculator");
            Assert.Contains(versions, x => x.Version == "1.0.0" && x.IsActive);
        }
        finally
        {
            Environment.SetEnvironmentVariable("HELPER_FF_TEMPLATE_RUNTIME_PROMOTION_V1", previousPromotion);
            Environment.SetEnvironmentVariable("HELPER_TEMPLATE_PROMOTION_AUTO_ACTIVATE", previousActivate);
            Environment.SetEnvironmentVariable("HELPER_TEMPLATE_PROMOTION_POST_ACTIVATION_FULL_RECERTIFY", previousRecertify);
        }
    }

    [Fact]
    public async Task GenerationTemplatePromotionService_PostActivationFullRecertifyFlag_ReCertifiesAfterActivation()
    {
        var previousPromotion = Environment.GetEnvironmentVariable("HELPER_FF_TEMPLATE_RUNTIME_PROMOTION_V1");
        var previousActivate = Environment.GetEnvironmentVariable("HELPER_TEMPLATE_PROMOTION_AUTO_ACTIVATE");
        var previousRecertify = Environment.GetEnvironmentVariable("HELPER_TEMPLATE_PROMOTION_POST_ACTIVATION_FULL_RECERTIFY");
        Environment.SetEnvironmentVariable("HELPER_FF_TEMPLATE_RUNTIME_PROMOTION_V1", "true");
        Environment.SetEnvironmentVariable("HELPER_TEMPLATE_PROMOTION_AUTO_ACTIVATE", "true");
        Environment.SetEnvironmentVariable("HELPER_TEMPLATE_PROMOTION_POST_ACTIVATION_FULL_RECERTIFY", "true");

        try
        {
            using var temp = new TempDirectoryScope();
            var generatedProject = Path.Combine(temp.Path, "generated_project");
            Directory.CreateDirectory(generatedProject);
            await File.WriteAllTextAsync(Path.Combine(generatedProject, "Calculator.cs"), "namespace Demo; public class Calculator {}");

            var certification = new StubTemplateCertificationService();
            var service = BuildService(
                temp.Path,
                new StubTemplateRoutingService("Template_ReCertify"),
                new StubCompileGate(success: true),
                certification);

            var outcome = await service.TryPromoteAsync(
                new GenerationRequest("сгенерируй инженерный калькулятор", generatedProject),
                new GenerationResult(true, new List<GeneratedFile>(), generatedProject, new List<BuildError>(), TimeSpan.FromSeconds(1)));

            Assert.True(outcome.Attempted);
            Assert.True(outcome.Success, string.Join(" | ", outcome.Errors));
            Assert.Equal(2, certification.CertifyCallCount);
            Assert.Collection(
                certification.ObservedTemplatePaths,
                first => Assert.Contains(Path.Combine("Template_ReCertify", "candidates", "1.0.0"), first, StringComparison.OrdinalIgnoreCase),
                second => Assert.EndsWith(Path.Combine("Template_ReCertify", "1.0.0"), second, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Environment.SetEnvironmentVariable("HELPER_FF_TEMPLATE_RUNTIME_PROMOTION_V1", previousPromotion);
            Environment.SetEnvironmentVariable("HELPER_TEMPLATE_PROMOTION_AUTO_ACTIVATE", previousActivate);
            Environment.SetEnvironmentVariable("HELPER_TEMPLATE_PROMOTION_POST_ACTIVATION_FULL_RECERTIFY", previousRecertify);
        }
    }

    [Fact]
    public async Task GenerationTemplatePromotionService_RejectsPlaceholderArtifactsBeforeCertification()
    {
        var previousPromotion = Environment.GetEnvironmentVariable("HELPER_FF_TEMPLATE_RUNTIME_PROMOTION_V1");
        var previousActivate = Environment.GetEnvironmentVariable("HELPER_TEMPLATE_PROMOTION_AUTO_ACTIVATE");
        Environment.SetEnvironmentVariable("HELPER_FF_TEMPLATE_RUNTIME_PROMOTION_V1", "true");
        Environment.SetEnvironmentVariable("HELPER_TEMPLATE_PROMOTION_AUTO_ACTIVATE", "false");

        try
        {
            using var temp = new TempDirectoryScope();
            var generatedProject = Path.Combine(temp.Path, "generated_project");
            Directory.CreateDirectory(generatedProject);
            await File.WriteAllTextAsync(
                Path.Combine(generatedProject, "BrokenService.cs"),
                """
namespace Demo;

public sealed class BrokenService
{
    public void Execute()
    {
        throw new global::System.InvalidOperationException("GENERATION_FALLBACK: unable to synthesize method body for Execute.");
    }
}
""");

            var service = BuildService(temp.Path, new StubTemplateRoutingService("Template_Broken"), new StubCompileGate(success: true));
            var outcome = await service.TryPromoteAsync(
                new GenerationRequest("сгенерируй сломанный шаблон", generatedProject),
                new GenerationResult(true, new List<GeneratedFile>(), generatedProject, new List<BuildError>(), TimeSpan.FromSeconds(1)));

            Assert.True(outcome.Attempted);
            Assert.False(outcome.Success);
            Assert.Contains(outcome.Errors, x => x.Contains("PlaceholderScan", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Environment.SetEnvironmentVariable("HELPER_FF_TEMPLATE_RUNTIME_PROMOTION_V1", previousPromotion);
            Environment.SetEnvironmentVariable("HELPER_TEMPLATE_PROMOTION_AUTO_ACTIVATE", previousActivate);
        }
    }

    private static GenerationTemplatePromotionService BuildService(
        string templatesRoot,
        ITemplateRoutingService routing,
        IGenerationCompileGate compileGate,
        ITemplateCertificationService? certification = null)
    {
        var generalizer = new StubTemplateGeneralizer(templatesRoot);
        var lifecycle = new TemplateLifecycleService(templatesRoot);
        var profile = new TemplatePromotionFeatureProfileService();
        var metrics = new GenerationMetricsService();
        certification ??= new StubTemplateCertificationService();
        return new GenerationTemplatePromotionService(
            routing,
            generalizer,
            lifecycle,
            compileGate,
            profile,
            metrics,
            certification,
            templatesRoot);
    }

    private sealed class StubTemplateRoutingService : ITemplateRoutingService
    {
        private readonly string _templateId;

        public StubTemplateRoutingService(string templateId)
        {
            _templateId = templateId;
        }

        public Task<TemplateRoutingDecision> RouteAsync(string prompt, CancellationToken ct = default)
        {
            var decision = new TemplateRoutingDecision(
                Matched: true,
                TemplateId: _templateId,
                Confidence: 0.99,
                Candidates: new[] { _templateId },
                Reason: "stub");
            return Task.FromResult(decision);
        }
    }

    private sealed class StubTemplateGeneralizer : ITemplateGeneralizer
    {
        private readonly string _templatesRoot;

        public StubTemplateGeneralizer(string templatesRoot)
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

    private sealed class StubTemplateCertificationService : ITemplateCertificationService
    {
        public int CertifyCallCount { get; private set; }

        public List<string> ObservedTemplatePaths { get; } = new();

        public Task<TemplateCertificationReport> CertifyAsync(string templateId, string version, string? reportPath = null, string? templatePath = null, CancellationToken ct = default)
        {
            return CertifyCoreAsync(templateId, version, reportPath, templatePath, ct);
        }

        public Task<TemplateCertificationGateReport> EvaluateGateAsync(string? reportPath = null, CancellationToken ct = default)
        {
            return Task.FromResult(new TemplateCertificationGateReport(
                DateTimeOffset.UtcNow,
                Passed: true,
                ReportPath: reportPath ?? Path.Combine(Path.GetTempPath(), "gate.md"),
                CertifiedCount: 0,
                FailedCount: 0,
                Violations: Array.Empty<string>(),
                Templates: Array.Empty<TemplateCertificationReport>()));
        }

        private async Task<TemplateCertificationReport> CertifyCoreAsync(string templateId, string version, string? reportPath, string? templatePath, CancellationToken ct)
        {
            var path = Path.GetFullPath(templatePath ?? Path.Combine(Path.GetTempPath(), "cert_stub"));
            Directory.CreateDirectory(path);
            var resolvedReportPath = Path.GetFullPath(reportPath ?? Path.Combine(Path.GetTempPath(), $"report_{Guid.NewGuid():N}.md"));
            Directory.CreateDirectory(Path.GetDirectoryName(resolvedReportPath)!);
            await File.WriteAllTextAsync(resolvedReportPath, $"# Stub certification report for {templateId}:{version}", ct);
            await TemplateCertificationStatusStore.WriteAsync(
                path,
                new TemplateCertificationStatus(
                    DateTimeOffset.UtcNow,
                    Passed: true,
                    HasCriticalAlerts: false,
                    CriticalAlerts: Array.Empty<string>(),
                    ReportPath: resolvedReportPath),
                ct);

            CertifyCallCount++;
            ObservedTemplatePaths.Add(path);

            return new TemplateCertificationReport(
                DateTimeOffset.UtcNow,
                templateId,
                version,
                path,
                MetadataSchemaPassed: true,
                CompileGatePassed: true,
                ArtifactValidationPassed: true,
                SmokePassed: true,
                SafetyScanPassed: true,
                Passed: true,
                Errors: Array.Empty<string>(),
                SmokeScenarios: Array.Empty<TemplateCertificationSmokeScenario>(),
                ReportPath: resolvedReportPath);
        }
    }

    private sealed class StubCompileGate : IGenerationCompileGate
    {
        private readonly bool _success;

        public StubCompileGate(bool success)
        {
            _success = success;
        }

        public Task<CompileGateResult> ValidateAsync(string rawProjectRoot, CancellationToken ct = default)
        {
            var result = _success
                ? new CompileGateResult(true, Array.Empty<BuildError>(), Path.Combine(rawProjectRoot, ".compile_gate"))
                : new CompileGateResult(false, new[] { new BuildError("x", 0, "FAIL", "compile fail") }, Path.Combine(rawProjectRoot, ".compile_gate"));
            return Task.FromResult(result);
        }
    }

    private sealed class TempDirectoryScope : IDisposable
    {
        public TempDirectoryScope()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "helper_template_promotion_" + Guid.NewGuid().ToString("N"));
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

