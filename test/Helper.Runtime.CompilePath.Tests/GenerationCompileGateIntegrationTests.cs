using Helper.Runtime.Core;
using Helper.Runtime.Generation;
using Helper.Runtime.Infrastructure;

namespace Helper.Runtime.Tests;

public sealed class GenerationCompileGateIntegrationTests
{
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
            TryDeleteDirectory(validRoot);
            TryDeleteDirectory(invalidRoot);
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
            TryDeleteDirectory(root);
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
            TryDeleteDirectory(root);
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
            TryDeleteDirectory(root);
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
            TryDeleteDirectory(root);
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
            TryDeleteDirectory(root);
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
            TryDeleteDirectory(root);
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
            TryDeleteDirectory(root);
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
            TryDeleteDirectory(root);
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
            TryDeleteDirectory(root);
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
            TryDeleteDirectory(root);
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
            TryDeleteDirectory(root);
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
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task CompileGate_TemplateEngineeringCalculator_Passes()
    {
        Environment.SetEnvironmentVariable("HELPER_TEMPLATE_PROMOTION_FORMAT_MODE", "off");
        Environment.SetEnvironmentVariable("HELPER_COMPILE_GATE_MAX_REPAIRS", "2");
        var gate = BuildCompileGate();
        var templateRoot = TestWorkspaceRoot.ResolveFile("library", "forge_templates", "Template_EngineeringCalculator");

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
        var templateRoot = TestWorkspaceRoot.ResolveFile("library", "forge_templates", "Template_PdfEpubConverter");

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
            TryDeleteDirectory(root);
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
            TryDeleteDirectory(root);
        }
    }

    private static GenerationCompileGate BuildCompileGate()
    {
        var usingInference = new UsingInferenceService(new TypeTokenExtractor());
        var repair = new CompileGateRepairService(usingInference, new MethodBodySemanticGuard());
        return new GenerationCompileGate(new DotnetService(), repair);
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
