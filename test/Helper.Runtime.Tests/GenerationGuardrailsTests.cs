using Helper.Runtime.Core;
using Helper.Runtime.Generation;
using Helper.Runtime.Infrastructure;

namespace Helper.Runtime.Tests;

public class GenerationGuardrailsTests
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
    public void UsingInferenceService_InfersUsingsFromMethodTypeTokens()
    {
        var inference = new UsingInferenceService(new TypeTokenExtractor());
        var methods = new List<ArbanMethodTask>
        {
            new("Load", "public Task<ObservableCollection<string>> LoadAsync(ICommand command)", "Load data", string.Empty)
        };

        var usings = inference.InferUsings("Demo.Root", "ViewModels/TodoViewModel.cs", FileRole.ViewModel, methods);
        Assert.Contains("System.Threading.Tasks", usings);
        Assert.Contains("System.Collections.ObjectModel", usings);
        Assert.Contains("System.Windows.Input", usings);
    }

    [Fact]
    public void MethodBodySemanticGuard_FallsBackOnUnknownSymbol()
    {
        var guard = new MethodBodySemanticGuard();
        var result = guard.Guard("public string BuildTitle()", "return UnknownSymbol.ToString();");

        Assert.True(result.UsedFallback);
        Assert.Equal("return default!;", result.Body);
        Assert.Contains(result.Diagnostics, x => x.Contains("CS0103", StringComparison.Ordinal));
    }

    [Fact]
    public void MethodBodySemanticGuard_FallsBackOnUnknownType()
    {
        var guard = new MethodBodySemanticGuard();
        var body = "var items = new List<YourDataType>(); return default!;";
        var result = guard.Guard("public object Build()", body);

        Assert.True(result.UsedFallback);
        Assert.Equal("return default!;", result.Body);
        Assert.Contains(result.Diagnostics, x => x.Contains("CS0246", StringComparison.Ordinal));
    }

    [Fact]
    public void GeneratedArtifactPlaceholderScanner_FlagsTodoLogicHereAndNotImplementedMarkers()
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

        Assert.Contains(findings, x => string.Equals(x.RuleId, "todo-marker", StringComparison.Ordinal));
        Assert.Contains(findings, x => string.Equals(x.RuleId, "logic-here-marker", StringComparison.Ordinal));
        Assert.Contains(findings, x => string.Equals(x.RuleId, "not-implemented-marker", StringComparison.Ordinal));
    }

    [Fact]
    public void BlueprintContractValidator_RepairsMalformedMethodSignature()
    {
        var validator = BuildBlueprintValidator();
        var blueprint = new SwarmBlueprint(
            "TestProject",
            "Test.Project",
            new List<SwarmFileDefinition>
            {
                new(
                    "Services/My Service.cs",
                    "Bad method signature sample",
                    FileRole.Service,
                    new List<string>(),
                    new List<ArbanMethodTask>
                    {
                        new("Execute", "public void Execute( {", "invalid", string.Empty)
                    })
            },
            new List<string>());

        var result = validator.ValidateAndNormalize(blueprint);
        Assert.True(result.IsValid);
        Assert.NotNull(result.Blueprint);
        Assert.Equal("public void Execute()", result.Blueprint!.Files[0].Methods![0].Signature);
    }

    [Fact]
    public void BlueprintContractValidator_SanitizesKnownRegressionPaths()
    {
        var validator = BuildBlueprintValidator();
        var blueprint = new SwarmBlueprint(
            "Logging Monitor",
            "Logging Monitor.Root",
            new List<SwarmFileDefinition>
            {
                new(
                    "Interfaces ILoggerService.cs",
                    "Interface contract",
                    FileRole.Interface,
                    new List<string>(),
                    new List<ArbanMethodTask>
                    {
                        new("Log", "public void Log(string message)", "Log message", string.Empty)
                    }),
                new(
                    "Configuration/ ConfigurationManager.cs",
                    "Config manager",
                    FileRole.Configuration,
                    new List<string>(),
                    new List<ArbanMethodTask>
                    {
                        new("Load", "public void Load()", "Load config", string.Empty)
                    })
            },
            new List<string>());

        var result = validator.ValidateAndNormalize(blueprint);
        Assert.True(result.IsValid);
        Assert.NotNull(result.Blueprint);

        var paths = result.Blueprint!.Files.Select(x => x.Path).ToList();
        Assert.Contains("Interfaces_ILoggerService.cs", paths);
        Assert.Contains("Configuration/ConfigurationManager.cs", paths);
    }

    [Fact]
    public void BlueprintContractValidator_RepairsInvalidPropertyLikeSignature()
    {
        var validator = BuildBlueprintValidator();
        var blueprint = new SwarmBlueprint(
            "TodoApp",
            "Todo.App",
            new List<SwarmFileDefinition>
            {
                new(
                    "ViewModels/TodoViewModel.cs",
                    "View model",
                    FileRole.ViewModel,
                    new List<string>(),
                    new List<ArbanMethodTask>
                    {
                        new("DeleteTodoCommand", "public ICommand DeleteTodoCommand { get; }", "command", string.Empty)
                    })
            },
            new List<string>());

        var result = validator.ValidateAndNormalize(blueprint);
        Assert.True(result.IsValid);
        Assert.NotNull(result.Blueprint);

        var signature = result.Blueprint!.Files[0].Methods![0].Signature;
        Assert.Equal("public void DeleteTodoCommand()", signature);
    }

    [Fact]
    public void GeneratedFileAstValidator_BlocksDuplicateMethods()
    {
        var validator = new GeneratedFileAstValidator();
        var code = @"namespace Demo
{
    public partial class Manager
    {
        public void Execute() { }
        public void Execute() { }
    }
}";

        var result = validator.ValidateFile("Services/Manager.cs", code, FileRole.Service, "Demo", "Manager");
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, x => x.Contains("Duplicate method signatures", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GoldenFixtures_BadGenerationCases_AreBlockedOrSanitized()
    {
        var fixtureRoot = Path.Combine(AppContext.BaseDirectory, "Fixtures", "GenerationBadCases");
        Assert.True(Directory.Exists(fixtureRoot));

        var astValidator = new GeneratedFileAstValidator();
        var invalidCode = await File.ReadAllTextAsync(Path.Combine(fixtureRoot, "invalid_service_configuration.cs"));
        var astResult = astValidator.ValidateFile(
            "Configurations/ServiceConfiguration.cs",
            invalidCode,
            FileRole.Configuration,
            "GymManagementSystem",
            "ServiceConfiguration");
        Assert.False(astResult.IsValid);

        var pathSanitizer = new GenerationPathSanitizer();
        var paths = await File.ReadAllLinesAsync(Path.Combine(fixtureRoot, "bad_paths.txt"));
        var results = paths
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(pathSanitizer.SanitizeRelativePath)
            .ToList();

        Assert.Contains(results, x => !x.IsValid); // traversal/absolute
        Assert.Contains(results, x => x.IsValid && x.SanitizedPath == "Configuration/ConfigurationManager.cs");
    }

    private static BlueprintContractValidator BuildBlueprintValidator()
    {
        var validator = new MethodSignatureValidator();
        var normalizer = new MethodSignatureNormalizer(validator);
        return new BlueprintContractValidator(
            new GenerationPathSanitizer(),
            new IdentifierSanitizer(),
            normalizer);
    }
}

