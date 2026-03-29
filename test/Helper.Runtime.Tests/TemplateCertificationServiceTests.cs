using Helper.Runtime.Generation;
using Helper.Runtime.Core;
using Helper.Runtime.Infrastructure;

namespace Helper.Runtime.Tests;

public sealed class TemplateCertificationServiceTests
{
    [Fact]
    public async Task CertifyAsync_Passes_ForValidClassLibraryTemplate()
    {
        Environment.SetEnvironmentVariable("HELPER_TEMPLATE_PROMOTION_FORMAT_MODE", "off");
        Environment.SetEnvironmentVariable("HELPER_TEMPLATE_CERTIFICATION_REQUIRE_SCHEMA_V2", "false");
        using var temp = new TempDirectoryScope();
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

        var service = BuildService(templatesRoot, temp.Path);
        var report = await service.CertifyAsync("Template_Simple", "1.0.0");

        Assert.True(report.Passed, string.Join(" | ", report.Errors));
        Assert.True(File.Exists(report.ReportPath));
    }

    [Fact]
    public async Task EvaluateGateAsync_Fails_WhenTemplateMetadataInvalid()
    {
        Environment.SetEnvironmentVariable("HELPER_TEMPLATE_PROMOTION_FORMAT_MODE", "off");
        Environment.SetEnvironmentVariable("HELPER_TEMPLATE_CERTIFICATION_REQUIRE_SCHEMA_V2", "false");
        using var temp = new TempDirectoryScope();
        var templatesRoot = Path.Combine(temp.Path, "templates");
        var templateVersionRoot = Path.Combine(templatesRoot, "Template_Bad", "1.0.0");
        Directory.CreateDirectory(templateVersionRoot);

        await File.WriteAllTextAsync(
            Path.Combine(templateVersionRoot, "template.json"),
            """
{
  "Id": "Template_Bad",
  "Description": "Missing required name field",
  "Language": "csharp",
  "Version": "1.0.0"
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
}
""");

        var lifecycle = new TemplateLifecycleService(templatesRoot);
        await lifecycle.ActivateVersionAsync("Template_Bad", "1.0.0");

        var service = BuildService(templatesRoot, temp.Path);
        var gate = await service.EvaluateGateAsync();

        Assert.False(gate.Passed);
        Assert.Contains(gate.Violations, v => v.Contains("Template_Bad", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CertifyAsync_Fails_WhenTemplateJsonIsCorrupted()
    {
        Environment.SetEnvironmentVariable("HELPER_TEMPLATE_PROMOTION_FORMAT_MODE", "off");
        Environment.SetEnvironmentVariable("HELPER_TEMPLATE_CERTIFICATION_REQUIRE_SCHEMA_V2", "false");
        using var temp = new TempDirectoryScope();
        var templatesRoot = Path.Combine(temp.Path, "templates");
        var templateVersionRoot = Path.Combine(templatesRoot, "Template_Corrupt", "1.0.0");
        Directory.CreateDirectory(templateVersionRoot);

        await File.WriteAllTextAsync(Path.Combine(templateVersionRoot, "template.json"), "{ invalid json");
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
        await File.WriteAllTextAsync(Path.Combine(templateVersionRoot, "Class1.cs"), "namespace Demo; public sealed class Class1 { }");

        var service = BuildService(templatesRoot, temp.Path);
        var report = await service.CertifyAsync("Template_Corrupt", "1.0.0");

        Assert.False(report.Passed);
        Assert.Contains(report.Errors, x => x.Contains("template.json", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CertifyAsync_Fails_WhenArtifactsMissingEvenWithoutBuildErrors()
    {
        Environment.SetEnvironmentVariable("HELPER_TEMPLATE_PROMOTION_FORMAT_MODE", "off");
        Environment.SetEnvironmentVariable("HELPER_TEMPLATE_CERTIFICATION_REQUIRE_SCHEMA_V2", "false");
        using var temp = new TempDirectoryScope();
        var templatesRoot = Path.Combine(temp.Path, "templates");
        var templateVersionRoot = Path.Combine(templatesRoot, "Template_NoArtifacts", "1.0.0");
        Directory.CreateDirectory(templateVersionRoot);

        await File.WriteAllTextAsync(
            Path.Combine(templateVersionRoot, "template.json"),
            """
{
  "Id": "Template_NoArtifacts",
  "Name": "No Artifacts",
  "Description": "Missing bin output",
  "Language": "csharp",
  "Version": "1.0.0"
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
        await File.WriteAllTextAsync(Path.Combine(templateVersionRoot, "Class1.cs"), "namespace Demo; public sealed class Class1 { }");

        var service = BuildService(
            templatesRoot,
            temp.Path,
            buildValidator: new EmptyBuildValidator());
        var report = await service.CertifyAsync("Template_NoArtifacts", "1.0.0");

        Assert.False(report.Passed);
        Assert.Contains(report.Errors, x => x.Contains("Build output folder is missing", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CertifyAsync_Fails_WhenSafetyScanDetectsSecret()
    {
        Environment.SetEnvironmentVariable("HELPER_TEMPLATE_PROMOTION_FORMAT_MODE", "off");
        Environment.SetEnvironmentVariable("HELPER_TEMPLATE_CERTIFICATION_REQUIRE_SCHEMA_V2", "false");
        using var temp = new TempDirectoryScope();
        var templatesRoot = Path.Combine(temp.Path, "templates");
        var templateVersionRoot = Path.Combine(templatesRoot, "Template_Unsafe", "1.0.0");
        Directory.CreateDirectory(templateVersionRoot);

        await File.WriteAllTextAsync(
            Path.Combine(templateVersionRoot, "template.json"),
            """
{
  "Id": "Template_Unsafe",
  "Name": "Unsafe Template",
  "Description": "Contains leaked secret",
  "Language": "csharp",
  "Version": "1.0.0"
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
            Path.Combine(templateVersionRoot, "Secrets.cs"),
            """
namespace Demo;
public static class Secrets
{
    public const string ApiKey = "sk-ABCDEF12345678901234567890";
}
""");

        var lifecycle = new TemplateLifecycleService(templatesRoot);
        await lifecycle.ActivateVersionAsync("Template_Unsafe", "1.0.0");

        var service = BuildService(templatesRoot, temp.Path);
        var report = await service.CertifyAsync("Template_Unsafe", "1.0.0");

        Assert.False(report.Passed);
        Assert.False(report.SafetyScanPassed);
        Assert.Contains(report.Errors, x => x.Contains("SafetyScan", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CertifyAsync_Fails_WhenPlaceholderArtifactsRemain()
    {
        Environment.SetEnvironmentVariable("HELPER_TEMPLATE_PROMOTION_FORMAT_MODE", "off");
        Environment.SetEnvironmentVariable("HELPER_TEMPLATE_CERTIFICATION_REQUIRE_SCHEMA_V2", "false");
        using var temp = new TempDirectoryScope();
        var templatesRoot = Path.Combine(temp.Path, "templates");
        var templateVersionRoot = Path.Combine(templatesRoot, "Template_Placeholder", "1.0.0");
        Directory.CreateDirectory(templateVersionRoot);

        await File.WriteAllTextAsync(
            Path.Combine(templateVersionRoot, "template.json"),
            """
{
  "Id": "Template_Placeholder",
  "Name": "Placeholder Template",
  "Description": "Contains generation fallback markers",
  "Language": "csharp",
  "Version": "1.0.0"
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
            Path.Combine(templateVersionRoot, "BrokenService.cs"),
            """
namespace Demo;

public sealed class BrokenService
{
    public void Execute()
    {
        throw new global::System.InvalidOperationException("GENERATION_FALLBACK: file failed validation and requires regeneration.");
    }
}
""");

        var lifecycle = new TemplateLifecycleService(templatesRoot);
        await lifecycle.ActivateVersionAsync("Template_Placeholder", "1.0.0");

        var service = BuildService(templatesRoot, temp.Path);
        var report = await service.CertifyAsync("Template_Placeholder", "1.0.0");

        Assert.False(report.Passed);
        Assert.False(report.PlaceholderScanPassed ?? true);
        Assert.Contains(report.Errors, x => x.Contains("PlaceholderScan", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(report.PlaceholderFindings ?? Array.Empty<string>(), x => x.Contains("GENERATION_FALLBACK", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CertifyAsync_AllowsWorkspaceTemplateWithoutExplicitVersion()
    {
        Environment.SetEnvironmentVariable("HELPER_TEMPLATE_PROMOTION_FORMAT_MODE", "off");
        Environment.SetEnvironmentVariable("HELPER_TEMPLATE_CERTIFICATION_REQUIRE_SCHEMA_V2", "false");
        using var temp = new TempDirectoryScope();
        var templatesRoot = Path.Combine(temp.Path, "templates");
        var templateRoot = Path.Combine(templatesRoot, "Template_WorkspaceRoot");
        Directory.CreateDirectory(templateRoot);

        await File.WriteAllTextAsync(
            Path.Combine(templateRoot, "template.json"),
            """
{
  "Id": "Template_WorkspaceRoot",
  "Name": "Workspace Root Template",
  "Description": "Root template without explicit version metadata",
  "Language": "csharp",
  "Tags": ["library"]
}
""");
        await File.WriteAllTextAsync(
            Path.Combine(templateRoot, "Project.csproj"),
            """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <OutputType>Library</OutputType>
  </PropertyGroup>
</Project>
""");
        await File.WriteAllTextAsync(
            Path.Combine(templateRoot, "Class1.cs"),
            """
namespace Demo;

public sealed class Class1
{
    public int Value() => 42;
}
""");

        var service = BuildService(templatesRoot, temp.Path);
        var report = await service.CertifyAsync("Template_WorkspaceRoot", "workspace", templatePath: templateRoot);

        Assert.True(report.Passed, string.Join(" | ", report.Errors));
        Assert.True(File.Exists(Path.Combine(templateRoot, TemplateCertificationStatusStore.StatusFileName)));
    }

    [Fact]
    public async Task CertifyAsync_PdfEpubTemplate_RunsRoundtripSmoke_WhenCalibreAvailable()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var templateRoot = ResolveWorkspaceFile("library", "forge_templates", "Template_PdfEpubConverter");
        if (!Directory.Exists(templateRoot) || !IsCalibreAvailable())
        {
            return;
        }

        Environment.SetEnvironmentVariable("HELPER_TEMPLATE_PROMOTION_FORMAT_MODE", "off");
        Environment.SetEnvironmentVariable("HELPER_TEMPLATE_CERTIFICATION_REQUIRE_SCHEMA_V2", "false");
        using var temp = new TempDirectoryScope();
        var service = BuildService(Path.Combine(temp.Path, "templates"), temp.Path);
        var probeRoot = Path.Combine(temp.Path, "Template_PdfEpubConverter");
        CopyDirectory(templateRoot, probeRoot);

        var report = await service.CertifyAsync("Template_PdfEpubConverter", "workspace", templatePath: probeRoot);

        var e2eScenario = Assert.Single(report.SmokeScenarios, x => string.Equals(x.Id, "pdf-epub-roundtrip-e2e", StringComparison.OrdinalIgnoreCase));
        Assert.True(e2eScenario.Passed, e2eScenario.Details);
    }

    [Fact]
    public void PdfEpubSmokeHarnessProgram_IsSelfContained_AndDoesNotReferenceHostCommandResolver()
    {
        var method = typeof(PdfEpubSmokeScenarioRunner).GetMethod(
            "BuildSmokeHarnessProgram",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);

        Assert.NotNull(method);
        var program = Assert.IsType<string>(method!.Invoke(null, null));

        Assert.DoesNotContain("HostCommandResolver", program, StringComparison.Ordinal);
        Assert.DoesNotContain("probeScriptPath", program, StringComparison.Ordinal);
        Assert.DoesNotContain("powerShellExecutable", program, StringComparison.Ordinal);
        Assert.DoesNotContain("HarnessProbePlan", program, StringComparison.Ordinal);
        Assert.Contains("if (args.Length < 1)", program, StringComparison.Ordinal);
        Assert.Contains("startInfo.UseShellExecute = false;", program, StringComparison.Ordinal);
        Assert.Contains("process.Kill(entireProcessTree: true);", program, StringComparison.Ordinal);
    }

    private static TemplateCertificationService BuildService(
        string templatesRoot,
        string workspaceRoot,
        IBuildValidator? buildValidator = null,
        IForgeArtifactValidator? artifactValidator = null)
    {
        var templateManager = new ProjectTemplateManager(templatesRoot);
        var lifecycle = new TemplateLifecycleService(templatesRoot);
        var dotnet = new DotnetService();
        var compileGate = new GenerationCompileGate(dotnet, new CompileGateRepairService(new UsingInferenceService(new TypeTokenExtractor()), new MethodBodySemanticGuard()));
        buildValidator ??= new MultiLanguageValidator(new LocalBuildExecutor(dotnet));
        artifactValidator ??= new ForgeArtifactValidator();
        return new TemplateCertificationService(
            templateManager,
            lifecycle,
            compileGate,
            buildValidator,
            artifactValidator,
            templatesRoot,
            workspaceRoot);
    }

    private static string ResolveWorkspaceFile(params string[] segments)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var marker = Path.Combine(current.FullName, "Helper.sln");
            if (File.Exists(marker))
            {
                if (segments.Length > 0 && string.Equals(segments[0], "library", StringComparison.OrdinalIgnoreCase))
                {
                    var libraryRoot = HelperWorkspacePathResolver.ResolveLibraryRoot(null, current.FullName);
                    var dataRootCandidate = Path.Combine(new[] { libraryRoot }.Concat(segments.Skip(1)).ToArray());
                    if (Directory.Exists(dataRootCandidate) || File.Exists(dataRootCandidate))
                    {
                        return dataRootCandidate;
                    }
                }

                return Path.Combine(new[] { current.FullName }.Concat(segments).ToArray());
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Workspace root was not found.");
    }

    private static bool IsCalibreAvailable()
    {
        return CalibreAvailabilityProbe.GetCurrent().IsOperational;
    }

    private static void CopyDirectory(string sourceRoot, string targetRoot)
    {
        Directory.CreateDirectory(targetRoot);
        foreach (var directory in Directory.GetDirectories(sourceRoot, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceRoot, directory);
            if (ShouldSkip(relative))
            {
                continue;
            }

            Directory.CreateDirectory(Path.Combine(targetRoot, relative));
        }

        foreach (var file in Directory.GetFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceRoot, file);
            if (ShouldSkip(relative))
            {
                continue;
            }

            var destination = Path.Combine(targetRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, true);
        }
    }

    private static bool ShouldSkip(string relativePath)
    {
        var parts = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Any(part =>
            string.Equals(part, "bin", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(part, "obj", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(part, ".compile_gate", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class EmptyBuildValidator : IBuildValidator
    {
        public Task<List<BuildError>> ValidateAsync(string projectPath, CancellationToken ct = default)
        {
            return Task.FromResult(new List<BuildError>());
        }
    }

    private sealed class TempDirectoryScope : IDisposable
    {
        public TempDirectoryScope()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "helper_template_cert_test_" + Guid.NewGuid().ToString("N"));
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
                // best effort
            }
        }
    }
}

