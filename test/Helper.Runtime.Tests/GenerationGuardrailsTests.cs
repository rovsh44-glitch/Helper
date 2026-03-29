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

    [Fact]
    public async Task CompileGate_AllowsValidProjectAndBlocksInvalidProject()
    {
        Environment.SetEnvironmentVariable("HELPER_REQUIRE_GENERATION_FORMAT", "false");
        Environment.SetEnvironmentVariable("HELPER_TEMPLATE_PROMOTION_FORMAT_MODE", "off");
        Environment.SetEnvironmentVariable("HELPER_COMPILE_GATE_MAX_REPAIRS", "2");
        var gate = BuildCompileGate();

        var validRoot = Path.Combine(Path.GetTempPath(), "helper_compile_gate_valid_" + Guid.NewGuid().ToString("N"));
        var invalidRoot = Path.Combine(Path.GetTempPath(), "helper_compile_gate_invalid_" + Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(validRoot);
            await File.WriteAllTextAsync(Path.Combine(validRoot, "Valid.cs"),
                "namespace Demo { public class Valid { public void Execute() { } } }");

            Directory.CreateDirectory(invalidRoot);
            await File.WriteAllTextAsync(Path.Combine(invalidRoot, "Invalid.cs"),
                "namespace Demo { public class Broken { public void Execute( { } }");

            var ok = await gate.ValidateAsync(validRoot);
            Assert.True(ok.Success);

            var fail = await gate.ValidateAsync(invalidRoot);
            Assert.False(fail.Success);
            Assert.NotEmpty(fail.Errors);
        }
        finally
        {
            if (Directory.Exists(validRoot))
            {
                Directory.Delete(validRoot, recursive: true);
            }

            if (Directory.Exists(invalidRoot))
            {
                Directory.Delete(invalidRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task CompileGate_RepairLoop_FixesCommonSmokeErrors()
    {
        Environment.SetEnvironmentVariable("HELPER_REQUIRE_GENERATION_FORMAT", "false");
        Environment.SetEnvironmentVariable("HELPER_TEMPLATE_PROMOTION_FORMAT_MODE", "off");
        Environment.SetEnvironmentVariable("HELPER_COMPILE_GATE_MAX_REPAIRS", "2");
        var gate = BuildCompileGate();
        var root = Path.Combine(Path.GetTempPath(), "helper_compile_gate_repair_" + Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(Path.Combine(root, "Contracts"));
            Directory.CreateDirectory(Path.Combine(root, "Services"));

            await File.WriteAllTextAsync(
                Path.Combine(root, "Contracts", "ITodoService.cs"),
                @"namespace Demo;
public interface ITodoService
{
    public async Task ExecuteAsync();
}");

            await File.WriteAllTextAsync(
                Path.Combine(root, "Services", "TodoService.cs"),
                @"namespace Demo;
public partial class TodoService : ITodoService
{
    public Task ExecuteAsync()
    {
        var items = new ObservableCollection<string>();
        return Task.CompletedTask;
    }
}");

            var result = await gate.ValidateAsync(root);
            Assert.True(
                result.Success,
                "CompileGate errors: " + string.Join(" | ", result.Errors.Select(e => $"[{e.Code}] {e.Message}")));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task CompileGate_RepairLoop_FixesMissingReturnPathErrors()
    {
        Environment.SetEnvironmentVariable("HELPER_REQUIRE_GENERATION_FORMAT", "false");
        Environment.SetEnvironmentVariable("HELPER_TEMPLATE_PROMOTION_FORMAT_MODE", "off");
        Environment.SetEnvironmentVariable("HELPER_COMPILE_GATE_MAX_REPAIRS", "3");
        var gate = BuildCompileGate();
        var root = Path.Combine(Path.GetTempPath(), "helper_compile_gate_return_fix_" + Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(
                Path.Combine(root, "Calculator.cs"),
                @"namespace Demo;
public class Calculator
{
    public int Evaluate(int x)
    {
        if (x > 0)
        {
            return x;
        }
    }
}");

            var result = await gate.ValidateAsync(root);
            Assert.True(
                result.Success,
                "CompileGate errors: " + string.Join(" | ", result.Errors.Select(e => $"[{e.Code}] {e.Message}")));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task CompileGate_RepairLoop_FixesMissingTypeAndInvalidOverride()
    {
        Environment.SetEnvironmentVariable("HELPER_REQUIRE_GENERATION_FORMAT", "false");
        Environment.SetEnvironmentVariable("HELPER_TEMPLATE_PROMOTION_FORMAT_MODE", "off");
        Environment.SetEnvironmentVariable("HELPER_COMPILE_GATE_MAX_REPAIRS", "3");
        var gate = BuildCompileGate();
        var root = Path.Combine(Path.GetTempPath(), "helper_compile_gate_type_override_fix_" + Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(
                Path.Combine(root, "Repository.cs"),
                """
using System.Collections.Generic;

namespace Demo;

public class Repository
{
    public List<Person> GetAll()
    {
        return new List<Person>();
    }
}
""");
            await File.WriteAllTextAsync(
                Path.Combine(root, "App.cs"),
                """
namespace Demo;

public class App
{
    protected override void OnStartup(global::System.EventArgs e)
    {
        return;
    }
}
""");

            var result = await gate.ValidateAsync(root);
            Assert.True(
                result.Success,
                "CompileGate errors: " + string.Join(" | ", result.Errors.Select(e => $"[{e.Code}] {e.Message}")));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task CompileGate_RepairLoop_FixesMissingInterfaceMemberImplementations()
    {
        Environment.SetEnvironmentVariable("HELPER_REQUIRE_GENERATION_FORMAT", "false");
        Environment.SetEnvironmentVariable("HELPER_TEMPLATE_PROMOTION_FORMAT_MODE", "off");
        Environment.SetEnvironmentVariable("HELPER_COMPILE_GATE_MAX_REPAIRS", "3");
        var gate = BuildCompileGate();
        var root = Path.Combine(Path.GetTempPath(), "helper_compile_gate_interface_fix_" + Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(
                Path.Combine(root, "Contracts.cs"),
                @"namespace Demo;
public interface ICalculator
{
    int Evaluate(int value);
}");
            await File.WriteAllTextAsync(
                Path.Combine(root, "Calculator.cs"),
                @"namespace Demo;
public class Calculator : ICalculator
{
}");

            var result = await gate.ValidateAsync(root);
            Assert.True(
                result.Success,
                "CompileGate errors: " + string.Join(" | ", result.Errors.Select(e => $"[{e.Code}] {e.Message}")));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task CompileGate_InheritsFrameworkReferences_FromSourceProjects()
    {
        Environment.SetEnvironmentVariable("HELPER_REQUIRE_GENERATION_FORMAT", "false");
        Environment.SetEnvironmentVariable("HELPER_TEMPLATE_PROMOTION_FORMAT_MODE", "off");
        Environment.SetEnvironmentVariable("HELPER_COMPILE_GATE_MAX_REPAIRS", "1");
        var gate = BuildCompileGate();
        var root = Path.Combine(Path.GetTempPath(), "helper_compile_gate_framework_refs_" + Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(
                Path.Combine(root, "Demo.csproj"),
                """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <EnableWindowsTargeting>true</EnableWindowsTargeting>
  </PropertyGroup>
  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>
</Project>
""");
            await File.WriteAllTextAsync(
                Path.Combine(root, "HostBuilder.cs"),
                """
using Microsoft.AspNetCore.Builder;

namespace Demo;

public class HostBuilder
{
    public WebApplication Build(string[] args)
    {
        return WebApplication.CreateBuilder(args).Build();
    }
}
""");

            var result = await gate.ValidateAsync(root);
            Assert.True(
                result.Success,
                "CompileGate errors: " + string.Join(" | ", result.Errors.Select(e => $"[{e.Code}] {e.Message}")));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task CompileGate_RepairLoop_FixesNonNullableInitializationErrors()
    {
        Environment.SetEnvironmentVariable("HELPER_REQUIRE_GENERATION_FORMAT", "false");
        Environment.SetEnvironmentVariable("HELPER_TEMPLATE_PROMOTION_FORMAT_MODE", "off");
        Environment.SetEnvironmentVariable("HELPER_COMPILE_GATE_MAX_REPAIRS", "3");
        var gate = BuildCompileGate();
        var root = Path.Combine(Path.GetTempPath(), "helper_compile_gate_nullable_fix_" + Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(
                Path.Combine(root, "Person.cs"),
                """
#nullable enable
#pragma warning error CS8618

namespace Demo;

public class Person
{
    public string Name { get; set; }
}
""");

            var result = await gate.ValidateAsync(root);
            Assert.True(
                result.Success,
                "CompileGate errors: " + string.Join(" | ", result.Errors.Select(e => $"[{e.Code}] {e.Message}")));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task CompileGate_RepairLoop_GeneratesMissingXamlEventHandlers()
    {
        Environment.SetEnvironmentVariable("HELPER_REQUIRE_GENERATION_FORMAT", "false");
        Environment.SetEnvironmentVariable("HELPER_TEMPLATE_PROMOTION_FORMAT_MODE", "off");
        Environment.SetEnvironmentVariable("HELPER_COMPILE_GATE_MAX_REPAIRS", "3");
        var gate = BuildCompileGate();
        var root = Path.Combine(Path.GetTempPath(), "helper_compile_gate_xaml_handlers_" + Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(
                Path.Combine(root, "MainWindow.xaml"),
                """
<Window x:Class="Demo.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Grid>
        <Button Content="Run" Click="OnRunClick" />
    </Grid>
</Window>
""");
            await File.WriteAllTextAsync(
                Path.Combine(root, "MainWindow.xaml.cs"),
                """
namespace Demo;

public partial class MainWindow : global::System.Windows.Window
{
}
""");

            var result = await gate.ValidateAsync(root);
            Assert.True(
                result.Success,
                "CompileGate errors: " + string.Join(" | ", result.Errors.Select(e => $"[{e.Code}] {e.Message}")));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task CompileGate_WpfAppProfile_WithAppXaml_PassesWithoutMc1002()
    {
        Environment.SetEnvironmentVariable("HELPER_TEMPLATE_PROMOTION_FORMAT_MODE", "off");
        Environment.SetEnvironmentVariable("HELPER_COMPILE_GATE_MAX_REPAIRS", "2");
        var gate = BuildCompileGate();
        var root = Path.Combine(Path.GetTempPath(), "helper_compile_gate_wpf_app_profile_" + Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(
                Path.Combine(root, "App.xaml"),
                """
<Application x:Class="Demo.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             StartupUri="MainWindow.xaml">
</Application>
""");
            await File.WriteAllTextAsync(
                Path.Combine(root, "App.xaml.cs"),
                """
namespace Demo;

public partial class App : global::System.Windows.Application
{
}
""");
            await File.WriteAllTextAsync(
                Path.Combine(root, "MainWindow.xaml"),
                """
<Window x:Class="Demo.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="MainWindow" Height="200" Width="300">
    <Grid />
</Window>
""");
            await File.WriteAllTextAsync(
                Path.Combine(root, "MainWindow.xaml.cs"),
                """
namespace Demo;

public partial class MainWindow : global::System.Windows.Window
{
    public MainWindow()
    {
        InitializeComponent();
    }
}
""");

            var result = await gate.ValidateAsync(root);
            Assert.True(
                result.Success,
                "CompileGate errors: " + string.Join(" | ", result.Errors.Select(e => $"[{e.Code}] {e.Message}")));
            Assert.DoesNotContain(result.Errors, e => e.Message.Contains("MC1002", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(result.Errors, e => e.Message.Contains("BG1003", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task CompileGate_RepairLoop_AddsKnownPackageReference_ForSystemManagement()
    {
        Environment.SetEnvironmentVariable("HELPER_REQUIRE_GENERATION_FORMAT", "false");
        Environment.SetEnvironmentVariable("HELPER_TEMPLATE_PROMOTION_FORMAT_MODE", "off");
        Environment.SetEnvironmentVariable("HELPER_COMPILE_GATE_MAX_REPAIRS", "3");
        var gate = BuildCompileGate();
        var root = Path.Combine(Path.GetTempPath(), "helper_compile_gate_pkg_ref_" + Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(
                Path.Combine(root, "WmiProbe.cs"),
                """
namespace Demo;

public sealed class WmiProbe
{
    public string Read()
    {
        var searcher = new global::System.Management.ManagementObjectSearcher("SELECT * FROM Win32_OperatingSystem");
        return searcher.Query.Language;
    }
}
""");

            var result = await gate.ValidateAsync(root);
            Assert.True(
                result.Success,
                "CompileGate errors: " + string.Join(" | ", result.Errors.Select(e => $"[{e.Code}] {e.Message}")));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task CompileGate_RepairLoop_FixesMissingXamlCodeBehindControlSymbols()
    {
        Environment.SetEnvironmentVariable("HELPER_REQUIRE_GENERATION_FORMAT", "false");
        Environment.SetEnvironmentVariable("HELPER_TEMPLATE_PROMOTION_FORMAT_MODE", "off");
        Environment.SetEnvironmentVariable("HELPER_COMPILE_GATE_MAX_REPAIRS", "3");
        var gate = BuildCompileGate();
        var root = Path.Combine(Path.GetTempPath(), "helper_compile_gate_xaml_binding_fix_" + Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(
                Path.Combine(root, "MainWindow.xaml.cs"),
                """
namespace Demo;

public partial class MainWindow : global::System.Windows.Window
{
    public void Render()
    {
        Display.Text = "ready";
    }
}
""");

            var result = await gate.ValidateAsync(root);
            Assert.True(
                result.Success,
                "CompileGate errors: " + string.Join(" | ", result.Errors.Select(e => $"[{e.Code}] {e.Message}")));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task CompileGate_RepairLoop_FixesInvalidExplicitInterfaceSpecifier()
    {
        Environment.SetEnvironmentVariable("HELPER_REQUIRE_GENERATION_FORMAT", "false");
        Environment.SetEnvironmentVariable("HELPER_TEMPLATE_PROMOTION_FORMAT_MODE", "off");
        Environment.SetEnvironmentVariable("HELPER_COMPILE_GATE_MAX_REPAIRS", "3");
        var gate = BuildCompileGate();
        var root = Path.Combine(Path.GetTempPath(), "helper_compile_gate_cs0538_fix_" + Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(
                Path.Combine(root, "BrokenExplicitInterface.cs"),
                """
namespace Demo;

public class AddTodoCommand
{
}

public class Runner
{
    void AddTodoCommand.Reset()
    {
    }
}
""");

            var result = await gate.ValidateAsync(root);
            Assert.True(
                result.Success,
                "CompileGate errors: " + string.Join(" | ", result.Errors.Select(e => $"[{e.Code}] {e.Message}")));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task CompileGate_RepairLoop_FixesMethodGroupAssignment()
    {
        Environment.SetEnvironmentVariable("HELPER_REQUIRE_GENERATION_FORMAT", "false");
        Environment.SetEnvironmentVariable("HELPER_TEMPLATE_PROMOTION_FORMAT_MODE", "off");
        Environment.SetEnvironmentVariable("HELPER_COMPILE_GATE_MAX_REPAIRS", "3");
        var gate = BuildCompileGate();
        var root = Path.Combine(Path.GetTempPath(), "helper_compile_gate_cs1656_fix_" + Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(
                Path.Combine(root, "MethodGroupAssignment.cs"),
                """
namespace Demo;

public class Item
{
    public bool IsComplete()
    {
        return false;
    }

    public void Mark()
    {
        IsComplete = true;
    }
}
""");

            var result = await gate.ValidateAsync(root);
            Assert.True(
                result.Success,
                "CompileGate errors: " + string.Join(" | ", result.Errors.Select(e => $"[{e.Code}] {e.Message}")));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task CompileGate_TemplateEngineeringCalculator_Passes()
    {
        Environment.SetEnvironmentVariable("HELPER_TEMPLATE_PROMOTION_FORMAT_MODE", "off");
        Environment.SetEnvironmentVariable("HELPER_COMPILE_GATE_MAX_REPAIRS", "2");
        var gate = BuildCompileGate();
        var templateRoot = ResolveWorkspaceFile("library", "forge_templates", "Template_EngineeringCalculator");

        if (!Directory.Exists(templateRoot))
        {
            return;
        }

        var probeRoot = Path.Combine(Path.GetTempPath(), "helper_compile_gate_calculator_" + Guid.NewGuid().ToString("N"));
        CopyDirectorySkippingBuildArtifacts(templateRoot, probeRoot);
        var result = await gate.ValidateAsync(probeRoot);
        try
        {
            Assert.True(
                result.Success,
                "CompileGate errors: " + string.Join(" | ", result.Errors.Select(e => $"[{e.Code}] {e.Message}")));
        }
        finally
        {
            TryDeleteDirectory(result.CompileWorkspace);
            TryDeleteDirectory(probeRoot);
        }
    }

    [Fact]
    public async Task CompileGate_TemplatePdfEpubConverter_Passes()
    {
        Environment.SetEnvironmentVariable("HELPER_TEMPLATE_PROMOTION_FORMAT_MODE", "off");
        Environment.SetEnvironmentVariable("HELPER_COMPILE_GATE_MAX_REPAIRS", "2");
        var gate = BuildCompileGate();
        var templateRoot = ResolveWorkspaceFile("library", "forge_templates", "Template_PdfEpubConverter");

        if (!Directory.Exists(templateRoot))
        {
            return;
        }

        var probeRoot = Path.Combine(Path.GetTempPath(), "helper_compile_gate_pdfepub_" + Guid.NewGuid().ToString("N"));
        CopyDirectorySkippingBuildArtifacts(templateRoot, probeRoot);
        var result = await gate.ValidateAsync(probeRoot);
        try
        {
            Assert.True(
                result.Success,
                "CompileGate errors: " + string.Join(" | ", result.Errors.Select(e => $"[{e.Code}] {e.Message}")));
        }
        finally
        {
            TryDeleteDirectory(result.CompileWorkspace);
            TryDeleteDirectory(probeRoot);
        }
    }

    [Fact]
    public async Task CompileGate_PreservesUseWindowsFormsProfile()
    {
        Environment.SetEnvironmentVariable("HELPER_TEMPLATE_PROMOTION_FORMAT_MODE", "off");
        Environment.SetEnvironmentVariable("HELPER_COMPILE_GATE_MAX_REPAIRS", "1");
        var gate = BuildCompileGate();
        var root = Path.Combine(Path.GetTempPath(), "helper_compile_gate_winforms_profile_" + Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(
                Path.Combine(root, "Project.csproj"),
                """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0-windows</TargetFramework>
    <OutputType>WinExe</OutputType>
    <UseWPF>true</UseWPF>
    <UseWindowsForms>true</UseWindowsForms>
  </PropertyGroup>
</Project>
""");
            await File.WriteAllTextAsync(
                Path.Combine(root, "MainViewModel.cs"),
                """
using Forms = System.Windows.Forms;

namespace Demo;

public sealed class MainViewModel
{
    public string PickFolder()
    {
        using var dialog = new Forms.FolderBrowserDialog();
        return dialog.SelectedPath ?? string.Empty;
    }
}
""");

            var result = await gate.ValidateAsync(root);
            Assert.True(
                result.Success,
                "CompileGate errors: " + string.Join(" | ", result.Errors.Select(e => $"[{e.Code}] {e.Message}")));
            TryDeleteDirectory(result.CompileWorkspace);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task CompileGate_DeclaredUnsupportedProjectType_ReturnsStructuredReason()
    {
        Environment.SetEnvironmentVariable("HELPER_TEMPLATE_PROMOTION_FORMAT_MODE", "off");
        Environment.SetEnvironmentVariable("HELPER_COMPILE_GATE_MAX_REPAIRS", "1");
        var gate = BuildCompileGate();
        var root = Path.Combine(Path.GetTempPath(), "helper_compile_gate_project_type_" + Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(
                Path.Combine(root, "template.json"),
                """
{
  "Id": "Template_InvalidType",
  "Name": "Invalid Type",
  "Description": "Unsupported project type metadata",
  "Language": "csharp",
  "Version": "1.0.0",
  "ProjectType": "unknown-super-app"
}
""");
            await File.WriteAllTextAsync(
                Path.Combine(root, "Main.cs"),
                """
namespace Demo;
public static class MainEntry
{
    public static void Run() { }
}
""");

            var result = await gate.ValidateAsync(root);
            Assert.False(result.Success);
            Assert.Contains(result.Errors, e => e.Code == "PROJECT_TYPE_UNSUPPORTED");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
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

    private static GenerationCompileGate BuildCompileGate()
    {
        var usingInference = new UsingInferenceService(new TypeTokenExtractor());
        var repair = new CompileGateRepairService(usingInference, new MethodBodySemanticGuard());
        return new GenerationCompileGate(new DotnetService(), repair);
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

    private static void TryDeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch
        {
            // keep compile workspace for diagnostics when cleanup fails
        }
    }

    private static void CopyDirectorySkippingBuildArtifacts(string sourceRoot, string targetRoot)
    {
        Directory.CreateDirectory(targetRoot);
        foreach (var directory in Directory.GetDirectories(sourceRoot, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceRoot, directory);
            if (ShouldSkipRelativePath(relative))
            {
                continue;
            }

            Directory.CreateDirectory(Path.Combine(targetRoot, relative));
        }

        foreach (var file in Directory.GetFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceRoot, file);
            if (ShouldSkipRelativePath(relative))
            {
                continue;
            }

            var destination = Path.Combine(targetRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, true);
        }
    }

    private static bool ShouldSkipRelativePath(string relativePath)
    {
        var parts = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Any(part =>
            string.Equals(part, "bin", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(part, "obj", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(part, ".compile_gate", StringComparison.OrdinalIgnoreCase));
    }
}

