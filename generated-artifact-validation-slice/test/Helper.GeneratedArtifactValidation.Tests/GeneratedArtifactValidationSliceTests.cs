using Helper.GeneratedArtifactValidation.Cli;
using Helper.GeneratedArtifactValidation.Contracts;
using Helper.GeneratedArtifactValidation.Core;
using System.Text;

namespace Helper.GeneratedArtifactValidation.Tests;

public sealed class GeneratedArtifactValidationSliceTests
{
    [Fact]
    public void PathSanitizer_BlocksTraversalAndAbsolutePath()
    {
        var sanitizer = new GenerationPathSanitizer();

        var traversal = sanitizer.SanitizeRelativePath("../evil.cs");
        Assert.False(traversal.IsValid);

        var absolute = sanitizer.SanitizeRelativePath("C:/temp/evil.cs");
        Assert.False(absolute.IsValid);
    }

    [Fact]
    public void PathSanitizer_NormalizesProblematicFileNames()
    {
        var sanitizer = new GenerationPathSanitizer();

        var result = sanitizer.SanitizeRelativePath("Interfaces ILoggerService.cs");
        Assert.True(result.IsValid);
        Assert.Equal("Interfaces_ILoggerService.cs", result.SanitizedPath);

        var leadingSpace = sanitizer.SanitizeRelativePath("Configuration/ ConfigurationManager.cs");
        Assert.True(leadingSpace.IsValid);
        Assert.Equal("Configuration/ConfigurationManager.cs", leadingSpace.SanitizedPath);
    }

    [Fact]
    public void MethodSignatureValidator_RejectsInvalidSignatures()
    {
        var validator = new MethodSignatureValidator();
        var result = validator.Validate("public void Execute( {");

        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void MethodSignatureNormalizer_RemovesInterfaceAsyncAndModifiers()
    {
        var normalizer = new MethodSignatureNormalizer(new MethodSignatureValidator());
        var result = normalizer.Normalize("public async Task ExecuteAsync()", FileRole.Interface, "ExecuteAsync");

        Assert.True(result.IsValid);
        Assert.Equal("Task ExecuteAsync()", result.Signature);
    }

    [Fact]
    public async Task BlueprintContractValidator_NormalizesMalformedBlueprintFixture()
    {
        var blueprintPath = Path.Combine(ResolveSliceRoot(), "sample_fixtures", "blueprints", "malformed-blueprint.json");
        var blueprint = await ValidationJson.LoadBlueprintAsync(blueprintPath);
        Assert.NotNull(blueprint);

        var result = new BlueprintContractValidator().ValidateAndNormalize(blueprint!);
        Assert.True(result.IsValid);
        Assert.NotNull(result.Blueprint);

        Assert.Equal("Logging_Monitor", result.Blueprint!.ProjectName);
        Assert.Equal("Logging_Monitor.Root", result.Blueprint.RootNamespace);
        Assert.Contains(result.Blueprint.Files, x => x.Path == "Interfaces_ILoggerService.cs");
        Assert.Contains(result.Blueprint.Files, x => x.Path == "Configuration/ConfigurationManager.cs");
        Assert.Contains(result.Blueprint.Files, x => x.Path == "Services/My_Service.cs");

        var interfaceFile = result.Blueprint.Files.Single(x => x.Path == "Interfaces_ILoggerService.cs");
        Assert.Equal("Task LogAsync(string message)", interfaceFile.Methods![0].Signature);

        var configurationFile = result.Blueprint.Files.Single(x => x.Path == "Configuration/ConfigurationManager.cs");
        Assert.Equal("public void DeleteTodoCommand()", configurationFile.Methods![0].Signature);

        var serviceFile = result.Blueprint.Files.Single(x => x.Path == "Services/My_Service.cs");
        Assert.Equal("public void Execute()", serviceFile.Methods![0].Signature);
    }

    [Fact]
    public void GeneratedArtifactPlaceholderScanner_FlagsKnownMarkers()
    {
        var findings = GeneratedArtifactPlaceholderScanner.ScanContent(
            "BrokenService.cs",
            """
namespace Demo;

public sealed class BrokenService
{
    public void Execute()
    {
        // TODO: logic here
        throw new global::System.NotImplementedException();
    }
}
""");

        Assert.Contains(findings, x => x.RuleId == "todo-marker");
        Assert.Contains(findings, x => x.RuleId == "logic-here-marker");
        Assert.Contains(findings, x => x.RuleId == "not-implemented-marker");
    }

    [Fact]
    public void GeneratedFileAstValidator_BlocksDuplicateMethods()
    {
        var validator = new GeneratedFileAstValidator();
        var code = """
namespace Demo;

public sealed class Manager
{
    public void Execute() { }
    public void Execute() { }
}
""";

        var result = validator.ValidateFile("Services/Manager.cs", code, FileRole.Service, "Demo", "Manager");
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, x => x.Contains("Duplicate method signatures", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ArtifactValidationService_GoodFixture_Passes()
    {
        var root = Path.Combine(ResolveSliceRoot(), "sample_fixtures", "artifacts", "good");
        var report = await new ArtifactValidationService().ValidateFixtureDirectoryAsync(root);

        Assert.True(report.Success);
        Assert.Empty(report.Errors);
        Assert.Empty(report.PlaceholderFindings);
    }

    [Fact]
    public async Task ArtifactValidationService_BadFixture_Fails()
    {
        var root = Path.Combine(ResolveSliceRoot(), "sample_fixtures", "artifacts", "bad");
        var report = await new ArtifactValidationService().ValidateFixtureDirectoryAsync(root);

        Assert.False(report.Success);
        Assert.NotEmpty(report.Errors);
        Assert.NotEmpty(report.PlaceholderFindings);
    }

    [Fact]
    public async Task CompileGateValidator_GoodProject_Passes()
    {
        var root = Path.Combine(ResolveSliceRoot(), "sample_fixtures", "compile_gate", "good_project");
        var result = await new CompileGateValidator().ValidateAsync(root);

        try
        {
            Assert.True(result.Success, string.Join(Environment.NewLine, result.Errors.Select(x => $"{x.Code}: {x.Message}")));
            Assert.Empty(result.Errors);
        }
        finally
        {
            TryDeleteDirectory(result.CompileWorkspace);
        }
    }

    [Fact]
    public async Task CompileGateValidator_BadProject_Fails()
    {
        var root = Path.Combine(ResolveSliceRoot(), "sample_fixtures", "compile_gate", "bad_project");
        var result = await new CompileGateValidator().ValidateAsync(root);

        try
        {
            Assert.False(result.Success);
            Assert.NotEmpty(result.Errors);
        }
        finally
        {
            TryDeleteDirectory(result.CompileWorkspace);
        }
    }

    [Fact]
    public async Task ValidationCommandRunner_SamplesCommand_Passes()
    {
        var stdout = new StringWriter(new StringBuilder());
        var stderr = new StringWriter(new StringBuilder());

        var exitCode = await ValidationCommandRunner.RunAsync(
            ["samples", "--root", ResolveSliceRoot()],
            stdout,
            stderr);

        Assert.Equal(0, exitCode);
        Assert.Contains("Sample validation sweep passed.", stdout.ToString(), StringComparison.Ordinal);
    }

    private static string ResolveSliceRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "GeneratedArtifactValidationSlice.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Slice root was not found.");
    }

    private static void TryDeleteDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return;
        }

        try
        {
            Directory.Delete(path, true);
        }
        catch
        {
        }
    }
}
