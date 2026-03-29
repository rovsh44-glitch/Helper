using System.Text.Json;
using Helper.Runtime.Core;
using Helper.Runtime.Generation;
using Helper.Runtime.Infrastructure;

namespace Helper.Runtime.Tests;

public sealed class TemplateRoutingAndDiagnosticsTests
{
    [Fact]
    public async Task ProjectTemplateManager_LoadsTemplateJsonMetadata()
    {
        using var temp = new TempDirectoryScope();
        var templateDir = Path.Combine(temp.Path, "Template_EngineeringCalculator");
        Directory.CreateDirectory(templateDir);
        await File.WriteAllTextAsync(
            Path.Combine(templateDir, "template.json"),
            JsonSerializer.Serialize(new
            {
                Id = "Template_EngineeringCalculator",
                Name = "Engineering Calculator",
                Description = "Calculator template",
                Language = "csharp",
                Tags = new[] { "wpf", "engineering", "calculator" },
                Version = "1.0.0"
            }));

        var manager = new ProjectTemplateManager(temp.Path);
        var templates = await manager.GetAvailableTemplatesAsync();

        var template = Assert.Single(templates);
        Assert.Equal("Template_EngineeringCalculator", template.Id);
        Assert.Contains("engineering", template.Tags ?? Array.Empty<string>());
        Assert.Equal("1.0.0", template.Version);
    }

    [Fact]
    public async Task ProjectTemplateManager_CloneTemplate_SkipsBinAndObj()
    {
        using var temp = new TempDirectoryScope();
        var templateDir = Path.Combine(temp.Path, "Template_Test");
        Directory.CreateDirectory(Path.Combine(templateDir, "bin", "Debug"));
        Directory.CreateDirectory(Path.Combine(templateDir, "obj"));
        Directory.CreateDirectory(Path.Combine(templateDir, "src"));
        await File.WriteAllTextAsync(Path.Combine(templateDir, "src", "Main.cs"), "class Main {}");
        await File.WriteAllTextAsync(Path.Combine(templateDir, "bin", "Debug", "artifact.exe"), "x");
        await File.WriteAllTextAsync(Path.Combine(templateDir, "obj", "cache.bin"), "x");

        var manager = new ProjectTemplateManager(temp.Path);
        var target = Path.Combine(temp.Path, "out");
        await manager.CloneTemplateAsync("Template_Test", target);

        Assert.True(File.Exists(Path.Combine(target, "src", "Main.cs")));
        Assert.False(Directory.Exists(Path.Combine(target, "bin")));
        Assert.False(Directory.Exists(Path.Combine(target, "obj")));
    }

    [Fact]
    public async Task ProjectTemplateManager_GetTemplateById_PrefersLatestNonDeprecatedVersion()
    {
        using var temp = new TempDirectoryScope();
        var root = Path.Combine(temp.Path, "Template_Math");
        var v1 = Path.Combine(root, "v1");
        var v2 = Path.Combine(root, "v2");
        Directory.CreateDirectory(v1);
        Directory.CreateDirectory(v2);
        await File.WriteAllTextAsync(
            Path.Combine(v1, "template.json"),
            JsonSerializer.Serialize(new
            {
                Id = "Template_Math",
                Name = "Math v1",
                Description = "old",
                Language = "csharp",
                Version = "1.0.0",
                Deprecated = true
            }));
        await File.WriteAllTextAsync(
            Path.Combine(v2, "template.json"),
            JsonSerializer.Serialize(new
            {
                Id = "Template_Math",
                Name = "Math v2",
                Description = "new",
                Language = "csharp",
                Version = "2.0.0",
                Deprecated = false
            }));

        var manager = new ProjectTemplateManager(temp.Path);
        var resolved = await manager.GetTemplateByIdAsync("Template_Math");

        Assert.NotNull(resolved);
        Assert.Equal("2.0.0", resolved!.Version);
        Assert.False(resolved.Deprecated);
    }

    [Fact]
    public async Task TemplateRoutingService_MatchesEngineeringCalculatorPrompt()
    {
        using var temp = new TempDirectoryScope();
        var originalFlag = Environment.GetEnvironmentVariable("HELPER_FF_TEMPLATE_ROUTER_V2");
        try
        {
            Environment.SetEnvironmentVariable("HELPER_FF_TEMPLATE_ROUTER_V2", "true");
            var engDir = Path.Combine(temp.Path, "Template_EngineeringCalculator");
            Directory.CreateDirectory(engDir);
            await File.WriteAllTextAsync(
                Path.Combine(engDir, "template.json"),
                JsonSerializer.Serialize(new
                {
                    Id = "Template_EngineeringCalculator",
                    Name = "Scientific Calculator GOLD",
                    Description = "WPF engineering calculator",
                    Language = "csharp",
                    Tags = new[] { "wpf", "engineering", "calculator" }
                }));

            var chessDir = Path.Combine(temp.Path, "Golden_Chess_v2");
            Directory.CreateDirectory(chessDir);
            await File.WriteAllTextAsync(
                Path.Combine(chessDir, "template.json"),
                JsonSerializer.Serialize(new
                {
                    Id = "Golden_Chess_v2",
                    Name = "Chess template",
                    Description = "WPF chess",
                    Language = "csharp",
                    Tags = new[] { "wpf", "chess" }
                }));

            var manager = new ProjectTemplateManager(temp.Path);
            var router = new TemplateRoutingService(manager);
            var decision = await router.RouteAsync("сгенерируй инженерный калькулятор WPF");

            Assert.True(decision.Matched);
            Assert.Equal("Template_EngineeringCalculator", decision.TemplateId);
            Assert.NotEmpty(decision.Candidates);
        }
        finally
        {
            Environment.SetEnvironmentVariable("HELPER_FF_TEMPLATE_ROUTER_V2", originalFlag);
        }
    }

    [Fact]
    public async Task TemplateRoutingService_WritesExplainabilityTelemetry_WithTopKAndDecisiveFeatures()
    {
        using var temp = new TempDirectoryScope();
        var previousRouter = Environment.GetEnvironmentVariable("HELPER_FF_TEMPLATE_ROUTER_V2");
        var previousTelemetryPath = Environment.GetEnvironmentVariable("HELPER_TEMPLATE_ROUTING_TELEMETRY_PATH");
        var telemetryPath = Path.Combine(temp.Path, "routing_telemetry.jsonl");

        try
        {
            Environment.SetEnvironmentVariable("HELPER_FF_TEMPLATE_ROUTER_V2", "true");
            Environment.SetEnvironmentVariable("HELPER_TEMPLATE_ROUTING_TELEMETRY_PATH", telemetryPath);

            var engDir = Path.Combine(temp.Path, "Template_EngineeringCalculator");
            Directory.CreateDirectory(engDir);
            await File.WriteAllTextAsync(
                Path.Combine(engDir, "template.json"),
                JsonSerializer.Serialize(new
                {
                    Id = "Template_EngineeringCalculator",
                    Name = "Engineering Calculator",
                    Description = "WPF engineering calculator",
                    Language = "csharp",
                    Tags = new[] { "wpf", "engineering", "calculator" },
                    ProjectType = "wpf-app",
                    Platform = "windows"
                }));
            await TemplateCertificationStatusStore.WriteAsync(
                engDir,
                new TemplateCertificationStatus(DateTimeOffset.UtcNow, Passed: true, HasCriticalAlerts: false, CriticalAlerts: Array.Empty<string>(), ReportPath: null));

            var consoleDir = Path.Combine(temp.Path, "Template_Console");
            Directory.CreateDirectory(consoleDir);
            await File.WriteAllTextAsync(
                Path.Combine(consoleDir, "template.json"),
                JsonSerializer.Serialize(new
                {
                    Id = "Template_Console",
                    Name = "Console utility",
                    Description = "Command line template",
                    Language = "csharp",
                    Tags = new[] { "console" },
                    ProjectType = "console",
                    Platform = "cross-platform"
                }));
            await TemplateCertificationStatusStore.WriteAsync(
                consoleDir,
                new TemplateCertificationStatus(DateTimeOffset.UtcNow, Passed: true, HasCriticalAlerts: false, CriticalAlerts: Array.Empty<string>(), ReportPath: null));

            var manager = new ProjectTemplateManager(temp.Path);
            var routeTelemetry = new RouteTelemetryService();
            var router = new TemplateRoutingService(manager, routeTelemetry);
            var decision = await router.RouteAsync("создай WPF приложение для инженерных расчетов под windows");

            Assert.True(decision.Matched);
            Assert.True(File.Exists(telemetryPath));

            var last = File.ReadLines(telemetryPath).Last();
            Assert.Contains("\"topK\":", last, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("\"decisiveFeatures\":", last, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("\"confidence\":", last, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(routeTelemetry.GetSnapshot().Recent, entry =>
                entry.OperationKind == RouteTelemetryOperationKinds.TemplateRouting &&
                entry.RouteKey == "template_engineeringcalculator");
        }
        finally
        {
            Environment.SetEnvironmentVariable("HELPER_FF_TEMPLATE_ROUTER_V2", previousRouter);
            Environment.SetEnvironmentVariable("HELPER_TEMPLATE_ROUTING_TELEMETRY_PATH", previousTelemetryPath);
        }
    }

    [Fact]
    public async Task TemplateRoutingService_SmokeTodoPrompt_UsesDeterministicSmokeTemplate()
    {
        using var temp = new TempDirectoryScope();
        var previousRouter = Environment.GetEnvironmentVariable("HELPER_FF_TEMPLATE_ROUTER_V2");
        var previousSmokeProfile = Environment.GetEnvironmentVariable("HELPER_SMOKE_PROFILE");

        try
        {
            Environment.SetEnvironmentVariable("HELPER_FF_TEMPLATE_ROUTER_V2", "true");
            Environment.SetEnvironmentVariable("HELPER_SMOKE_PROFILE", "true");

            var autoTemplateDir = Path.Combine(temp.Path, "Template_Auto_Создать_самодостаточный_компилируемый_и");
            Directory.CreateDirectory(autoTemplateDir);
            await File.WriteAllTextAsync(
                Path.Combine(autoTemplateDir, "template.json"),
                JsonSerializer.Serialize(new
                {
                    Id = "Template_Auto_Создать_самодостаточный_компилируемый_и",
                    Name = "Smoke Todo template",
                    Description = "Deterministic smoke fallback template",
                    Language = "csharp",
                    Tags = new[] { "wpf", "todo", "compile" }
                }));

            var manager = new ProjectTemplateManager(temp.Path);
            var router = new TemplateRoutingService(manager);
            var decision = await router.RouteAsync("Generate a minimal C# WPF TODO app with model, interface and service. Keep blueprint compact and compile-oriented.");

            Assert.True(decision.Matched);
            Assert.Equal("Template_Auto_Создать_самодостаточный_компилируемый_и", decision.TemplateId);
            Assert.Equal(0.99, decision.Confidence);
        }
        finally
        {
            Environment.SetEnvironmentVariable("HELPER_FF_TEMPLATE_ROUTER_V2", previousRouter);
            Environment.SetEnvironmentVariable("HELPER_SMOKE_PROFILE", previousSmokeProfile);
        }
    }

    [Fact]
    public async Task ProjectTemplateManager_WhenRequireCertified_ExcludesUncertifiedVersions()
    {
        using var temp = new TempDirectoryScope();
        var previous = Environment.GetEnvironmentVariable("HELPER_TEMPLATE_ROUTING_REQUIRE_CERTIFIED");
        Environment.SetEnvironmentVariable("HELPER_TEMPLATE_ROUTING_REQUIRE_CERTIFIED", "true");

        try
        {
            var root = Path.Combine(temp.Path, "Template_Certified");
            var v1 = Path.Combine(root, "v1");
            var v2 = Path.Combine(root, "v2");
            Directory.CreateDirectory(v1);
            Directory.CreateDirectory(v2);

            await File.WriteAllTextAsync(
                Path.Combine(v1, "template.json"),
                JsonSerializer.Serialize(new { Id = "Template_Certified", Name = "v1", Description = "d", Language = "csharp", Version = "1.0.0" }));
            await File.WriteAllTextAsync(
                Path.Combine(v2, "template.json"),
                JsonSerializer.Serialize(new { Id = "Template_Certified", Name = "v2", Description = "d", Language = "csharp", Version = "2.0.0" }));

            await TemplateCertificationStatusStore.WriteAsync(
                v1,
                new TemplateCertificationStatus(DateTimeOffset.UtcNow, Passed: false, HasCriticalAlerts: true, CriticalAlerts: new[] { "fail" }, ReportPath: null));
            await TemplateCertificationStatusStore.WriteAsync(
                v2,
                new TemplateCertificationStatus(DateTimeOffset.UtcNow, Passed: true, HasCriticalAlerts: false, CriticalAlerts: Array.Empty<string>(), ReportPath: null));

            var manager = new ProjectTemplateManager(temp.Path);
            var resolved = await manager.GetTemplateByIdAsync("Template_Certified");

            Assert.NotNull(resolved);
            Assert.Equal("2.0.0", resolved!.Version);
            Assert.True(resolved.Certified);
            Assert.False(resolved.HasCriticalAlerts);
        }
        finally
        {
            Environment.SetEnvironmentVariable("HELPER_TEMPLATE_ROUTING_REQUIRE_CERTIFIED", previous);
        }
    }

    [Fact]
    public async Task ProjectTemplateManager_ExcludesCriticalAlertVersions_ByDefault()
    {
        using var temp = new TempDirectoryScope();
        var root = Path.Combine(temp.Path, "Template_Alerts");
        var v1 = Path.Combine(root, "v1");
        var v2 = Path.Combine(root, "v2");
        Directory.CreateDirectory(v1);
        Directory.CreateDirectory(v2);

        await File.WriteAllTextAsync(
            Path.Combine(v1, "template.json"),
            JsonSerializer.Serialize(new { Id = "Template_Alerts", Name = "v1", Description = "d", Language = "csharp", Version = "1.0.0" }));
        await File.WriteAllTextAsync(
            Path.Combine(v2, "template.json"),
            JsonSerializer.Serialize(new { Id = "Template_Alerts", Name = "v2", Description = "d", Language = "csharp", Version = "2.0.0" }));

        await TemplateCertificationStatusStore.WriteAsync(
            v1,
            new TemplateCertificationStatus(DateTimeOffset.UtcNow, Passed: true, HasCriticalAlerts: true, CriticalAlerts: new[] { "critical" }, ReportPath: null));
        await TemplateCertificationStatusStore.WriteAsync(
            v2,
            new TemplateCertificationStatus(DateTimeOffset.UtcNow, Passed: true, HasCriticalAlerts: false, CriticalAlerts: Array.Empty<string>(), ReportPath: null));

        var manager = new ProjectTemplateManager(temp.Path);
        var resolved = await manager.GetTemplateByIdAsync("Template_Alerts");

        Assert.NotNull(resolved);
        Assert.Equal("2.0.0", resolved!.Version);
        Assert.False(resolved.HasCriticalAlerts);
    }

    [Fact]
    public async Task ProjectTemplateManager_ResolveTemplateAvailability_ReportsCriticalAlertBlock()
    {
        using var temp = new TempDirectoryScope();
        var root = Path.Combine(temp.Path, "Template_PdfEpubConverter");
        Directory.CreateDirectory(root);

        await File.WriteAllTextAsync(
            Path.Combine(root, "template.json"),
            JsonSerializer.Serialize(new
            {
                Id = "Template_PdfEpubConverter",
                Name = "PDF EPUB Converter",
                Description = "Converter template",
                Language = "csharp"
            }));
        await TemplateCertificationStatusStore.WriteAsync(
            root,
            new TemplateCertificationStatus(
                DateTimeOffset.UtcNow,
                Passed: false,
                HasCriticalAlerts: true,
                CriticalAlerts: new[] { "Smoke[compile]: Compile gate failed." },
                ReportPath: "doc/report.md"));

        var manager = new ProjectTemplateManager(temp.Path);
        var resolution = await manager.ResolveTemplateAvailabilityAsync("Template_PdfEpubConverter");

        Assert.Null(resolution.Template);
        Assert.True(resolution.ExistsOnDisk);
        Assert.Equal(TemplateAvailabilityState.BlockedByCriticalAlerts, resolution.State);
        Assert.Equal("doc/report.md", resolution.CertificationReportPath);
        Assert.Contains("critical alerts", resolution.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProjectTemplateManager_IgnoresStaleCriticalAlertStatus_WhenCertificationIsNotRequired()
    {
        using var temp = new TempDirectoryScope();
        var root = Path.Combine(temp.Path, "Template_PdfEpubConverter");
        Directory.CreateDirectory(root);

        var templatePath = Path.Combine(root, "template.json");
        await File.WriteAllTextAsync(
            templatePath,
            JsonSerializer.Serialize(new
            {
                Id = "Template_PdfEpubConverter",
                Name = "PDF EPUB Converter",
                Description = "Converter template",
                Language = "csharp"
            }));
        await TemplateCertificationStatusStore.WriteAsync(
            root,
            new TemplateCertificationStatus(
                DateTimeOffset.UtcNow.AddDays(-2),
                Passed: false,
                HasCriticalAlerts: true,
                CriticalAlerts: new[] { "Smoke[compile]: stale alert" },
                ReportPath: "doc/report.md"));

        File.SetLastWriteTimeUtc(templatePath, DateTime.UtcNow.AddHours(1));

        var manager = new ProjectTemplateManager(temp.Path);
        var resolved = await manager.GetTemplateByIdAsync("Template_PdfEpubConverter");

        Assert.NotNull(resolved);
        Assert.Equal("Template_PdfEpubConverter", resolved!.Id);
        Assert.False(resolved.HasCriticalAlerts);
        Assert.False(resolved.Certified);
    }

    [Fact]
    public async Task ForgeArtifactValidator_AcceptsLibraryArtifact()
    {
        using var temp = new TempDirectoryScope();
        var projectDir = Path.Combine(temp.Path, "TemplateLibrary");
        Directory.CreateDirectory(projectDir);
        await File.WriteAllTextAsync(
            Path.Combine(projectDir, "TemplateLibrary.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
                <OutputType>Library</OutputType>
              </PropertyGroup>
            </Project>
            """);
        var artifactDir = Path.Combine(projectDir, "bin", "Debug", "net8.0");
        Directory.CreateDirectory(artifactDir);
        await File.WriteAllTextAsync(Path.Combine(artifactDir, "TemplateLibrary.dll"), "x");

        var validator = new ForgeArtifactValidator();
        var result = await validator.ValidateAsync(projectDir, Array.Empty<BuildError>());

        Assert.True(result.Success);
    }

    [Fact]
    public void FailureEnvelopeFactory_NormalizesCanceledTaskMessage()
    {
        var factory = new FailureEnvelopeFactory();
        var envelopes = factory.FromBuildErrors(
            FailureStage.Synthesis,
            "Helper",
            new[] { new BuildError("Build", 0, "FAIL", "A task was canceled.") });

        var envelope = Assert.Single(envelopes);
        Assert.Equal("Operation was canceled due to timeout or external cancellation.", envelope.Evidence);
        Assert.True(envelope.Retryable);
    }

    [Fact]
    public void FailureEnvelopeFactory_MapsMissingTypeToDependency()
    {
        var factory = new FailureEnvelopeFactory();
        var envelope = factory.FromBuildErrors(
            FailureStage.Synthesis,
            "Helper",
            new[] { new BuildError("Build", 0, "CS0246", "The type or namespace name 'ObservableObject' could not be found.") })
            .Single();

        Assert.Equal(RootCauseClass.Dependency, envelope.RootCauseClass);
    }

    [Fact]
    public void FailureEnvelopeFactory_MapsNetworkErrorsToExternalService()
    {
        var factory = new FailureEnvelopeFactory();
        var envelope = factory.FromBuildErrors(
            FailureStage.Tooling,
            "Helper",
            new[] { new BuildError("Build", 0, "FAIL", "Qdrant endpoint unavailable: connection refused.") })
            .Single();

        Assert.Equal(RootCauseClass.ExternalService, envelope.RootCauseClass);
        Assert.True(envelope.Retryable);
    }

    private sealed class TempDirectoryScope : IDisposable
    {
        public TempDirectoryScope()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "helper_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, true);
                }
            }
            catch
            {
                // best effort cleanup
            }
        }
    }
}

