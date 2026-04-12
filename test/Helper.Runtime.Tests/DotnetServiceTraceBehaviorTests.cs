using System.Text.Json;
using Helper.Runtime.Core;
using Helper.Runtime.Infrastructure;

namespace Helper.Runtime.Tests;

[Collection("ProcessEnvironment")]
public sealed class DotnetServiceTraceBehaviorTests
{
    [Fact]
    public async Task BuildAsync_WritesStartedAndExitedTraceRecords()
    {
        using var temp = new TempDirectoryScope("helper_dotnet_trace_");
        var projectRoot = Path.Combine(temp.Path, "SampleBuild");
        var tracePath = Path.Combine(temp.Path, "certification_process_trace.jsonl");
        Directory.CreateDirectory(projectRoot);
        await CreateSimpleLibraryAsync(projectRoot);

        using var env = new EnvScope(new Dictionary<string, string?>
        {
            [DotnetProcessTraceWriter.TracePathEnvName] = tracePath,
            ["HELPER_DOTNET_BUILD_TIMEOUT_SEC"] = "60"
        });

        var errors = await new DotnetService().BuildAsync(projectRoot);

        Assert.Empty(errors);
        var events = ReadTraceEvents(tracePath);
        Assert.Contains(events, x => x.operation == "build" && x.eventType == "started");
        Assert.Contains(events, x => x.operation == "build" && x.eventType == "exited" && x.exitCode == 0);
    }

    [Fact]
    public async Task BuildAsync_TimeoutReturnsStructuredFailureAndWritesTimeoutTrace()
    {
        using var temp = new TempDirectoryScope("helper_dotnet_timeout_");
        var projectRoot = Path.Combine(temp.Path, "SlowBuild");
        var tracePath = Path.Combine(temp.Path, "certification_process_trace.jsonl");
        Directory.CreateDirectory(projectRoot);
        await CreateSlowBuildProjectAsync(projectRoot);

        using var env = new EnvScope(new Dictionary<string, string?>
        {
            [DotnetProcessTraceWriter.TracePathEnvName] = tracePath,
            ["HELPER_DOTNET_BUILD_TIMEOUT_SEC"] = "1",
            ["HELPER_DOTNET_KILL_CONFIRM_TIMEOUT_SEC"] = "10"
        });

        var errors = await new DotnetService().BuildAsync(projectRoot);

        var timeout = Assert.Single(errors);
        Assert.Equal("GENERATION_STAGE_TIMEOUT", timeout.Code);
        Assert.Contains("killConfirmed", timeout.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("trace=certification_process_trace.jsonl", timeout.Message, StringComparison.OrdinalIgnoreCase);

        var events = ReadTraceEvents(tracePath);
        Assert.Contains(events, x => x.operation == "build" && x.eventType == "started");
        Assert.Contains(events, x => x.operation == "build" && x.eventType == "timeout_started");
        Assert.Contains(events, x => x.operation == "build" && x.eventType == "kill_attempted");
        Assert.Contains(events, x => x.operation == "build" && (x.eventType == "kill_confirmed" || x.eventType == "orphan_risk"));
    }

    [Fact]
    public void TargetResolver_PrefersRootProject_AndIgnoresCompileGateAndBuildArtifacts()
    {
        using var temp = new TempDirectoryScope("helper_dotnet_targets_");
        var root = Path.Combine(temp.Path, "workspace");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "Root.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        Directory.CreateDirectory(Path.Combine(root, ".compile_gate"));
        File.WriteAllText(Path.Combine(root, ".compile_gate", "GeneratedCompileGate.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        Directory.CreateDirectory(Path.Combine(root, "bin", "Debug"));
        File.WriteAllText(Path.Combine(root, "bin", "Debug", "Shadow.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");

        var resolution = DotnetBuildTargetResolver.Resolve(root, allowRecursiveDiscovery: true);

        Assert.True(resolution.Succeeded);
        Assert.Equal(Path.Combine(root, "Root.csproj"), resolution.TargetPath);
    }

    [Fact]
    public void TargetResolver_WithoutRecursiveDiscovery_Fails_WhenOnlyNestedProjectExists()
    {
        using var temp = new TempDirectoryScope("helper_dotnet_targets_");
        var root = Path.Combine(temp.Path, "workspace");
        var nested = Path.Combine(root, "src", "Nested");
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(nested, "Nested.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");

        var resolution = DotnetBuildTargetResolver.Resolve(root, allowRecursiveDiscovery: false);

        Assert.False(resolution.Succeeded);
        Assert.Equal("DOTNET_TARGET_NOT_FOUND", resolution.ErrorCode);
    }

    [Fact]
    public void TargetResolver_WithRecursiveDiscovery_FindsSingleNestedProjectOutsideIgnoredPaths()
    {
        using var temp = new TempDirectoryScope("helper_dotnet_targets_");
        var root = Path.Combine(temp.Path, "workspace");
        var nested = Path.Combine(root, "src", "Nested");
        Directory.CreateDirectory(nested);
        var expected = Path.Combine(nested, "Nested.csproj");
        File.WriteAllText(expected, "<Project Sdk=\"Microsoft.NET.Sdk\" />");

        var resolution = DotnetBuildTargetResolver.Resolve(root, allowRecursiveDiscovery: true);

        Assert.True(resolution.Succeeded);
        Assert.Equal(expected, resolution.TargetPath);
    }

    [Fact]
    public void TargetResolver_ReturnsStructuredAmbiguity_ForMultipleTopLevelProjects()
    {
        using var temp = new TempDirectoryScope("helper_dotnet_targets_");
        var root = Path.Combine(temp.Path, "workspace");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "A.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        File.WriteAllText(Path.Combine(root, "B.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");

        var resolution = DotnetBuildTargetResolver.Resolve(root, allowRecursiveDiscovery: true);

        Assert.False(resolution.Succeeded);
        Assert.Equal("DOTNET_TARGET_AMBIGUOUS", resolution.ErrorCode);
        Assert.Contains("A.csproj", resolution.ErrorMessage, StringComparison.Ordinal);
        Assert.Contains("B.csproj", resolution.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void TargetResolver_RejectsExplicitTargetOutsideRoot_AndIgnoredPath()
    {
        using var temp = new TempDirectoryScope("helper_dotnet_targets_");
        var root = Path.Combine(temp.Path, "workspace");
        var sibling = Path.Combine(temp.Path, "outside");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(sibling);
        var outsideProject = Path.Combine(sibling, "Outside.csproj");
        File.WriteAllText(outsideProject, "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        Directory.CreateDirectory(Path.Combine(root, ".compile_gate"));
        var ignoredProject = Path.Combine(root, ".compile_gate", "GeneratedCompileGate.csproj");
        File.WriteAllText(ignoredProject, "<Project Sdk=\"Microsoft.NET.Sdk\" />");

        var outside = DotnetBuildTargetResolver.ResolveExplicit(root, outsideProject);
        var ignored = DotnetBuildTargetResolver.ResolveExplicit(root, ignoredProject);

        Assert.False(outside.Succeeded);
        Assert.Equal("DOTNET_TARGET_OUTSIDE_ROOT", outside.ErrorCode);
        Assert.False(ignored.Succeeded);
        Assert.Equal("DOTNET_TARGET_IGNORED_PATH", ignored.ErrorCode);
    }

    private static async Task CreateSimpleLibraryAsync(string projectRoot)
    {
        await File.WriteAllTextAsync(
            Path.Combine(projectRoot, "SampleBuild.csproj"),
            """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>
""");
        await File.WriteAllTextAsync(
            Path.Combine(projectRoot, "Class1.cs"),
            """
namespace SampleBuild;

public sealed class Class1
{
    public int Value() => 1;
}
""");
    }

    private static async Task CreateSlowBuildProjectAsync(string projectRoot)
    {
        var windowsPowerShell = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");
        await File.WriteAllTextAsync(
            Path.Combine(projectRoot, "SlowBuild.csproj"),
            $$"""
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <SlowCommand Condition="'$(OS)' == 'Windows_NT'">&quot;{{windowsPowerShell}}&quot; -NoProfile -Command &quot;Start-Sleep -Seconds 5&quot;</SlowCommand>
    <SlowCommand Condition="'$(OS)' != 'Windows_NT'">/bin/sh -c &quot;sleep 5&quot;</SlowCommand>
  </PropertyGroup>
  <Target Name="SlowBeforeBuild" BeforeTargets="BeforeBuild">
    <Exec Command="$(SlowCommand)" />
  </Target>
</Project>
""");
        await File.WriteAllTextAsync(
            Path.Combine(projectRoot, "Class1.cs"),
            """
namespace SlowBuild;

public sealed class Class1
{
    public int Value() => 1;
}
""");
    }

    private static IReadOnlyList<(string eventType, string operation, int? exitCode)> ReadTraceEvents(string tracePath)
    {
        Assert.True(File.Exists(tracePath), $"Trace file not found: {tracePath}");
        var rows = new List<(string eventType, string operation, int? exitCode)>();
        foreach (var line in File.ReadLines(tracePath))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            rows.Add((
                root.GetProperty("eventType").GetString() ?? string.Empty,
                root.GetProperty("operation").GetString() ?? string.Empty,
                root.TryGetProperty("exitCode", out var exitCodeElement) && exitCodeElement.ValueKind != JsonValueKind.Null
                    ? exitCodeElement.GetInt32()
                    : null));
        }

        return rows;
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

}
