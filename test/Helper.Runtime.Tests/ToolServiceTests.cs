using Helper.Runtime.Core;
using Helper.Runtime.Infrastructure;

namespace Helper.Runtime.Tests;

public sealed class ToolServiceTests
{
    private static readonly object EnvSync = new();
    private static readonly object WorkspaceSync = new();

    [Fact]
    public async Task GetAvailableToolsAsync_LogsSingleOptionalMcpBootstrapSummary_WhenProvidersAreUnavailable()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"helper-toolservice-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(tempRoot, "mcp_config"));
        await File.WriteAllTextAsync(
            Path.Combine(tempRoot, "mcp_config", "servers.json"),
            """
            {
              "mcpServers": {
                "sqlite": {
                  "command": "uvx",
                  "args": ["mcp-server-sqlite"]
                },
                "github": {
                  "command": "missing-helper-cmd",
                  "args": ["--test"]
                }
              }
            }
            """);

        var output = new StringWriter();
        var proxy = new RecordingMcpProxy();

        await RunInWorkspaceAsync(tempRoot, async () =>
        {
            lock (WorkspaceSync)
            {
                var originalOut = Console.Out;
                try
                {
                    Console.SetOut(output);

                    var service = new ToolService(
                        proxy,
                        new AllowAllProcessGuard(),
                        new EmptyGoalManager(),
                        new PermissiveFileSystemGuard(),
                        extensionRegistry: new ExtensionRegistry());

                    var tools = service.GetAvailableToolsAsync().GetAwaiter().GetResult();

                    Assert.NotEmpty(tools);
                    Assert.Equal(0, proxy.DiscoverCalls);

                    var log = output.ToString();
                    Assert.Contains("Optional MCP bootstrap summary", log, StringComparison.Ordinal);
                    Assert.Contains("sqlite: command 'uvx' blocked by policy", log, StringComparison.Ordinal);
                    Assert.Contains("github:", log, StringComparison.Ordinal);
                    Assert.DoesNotContain("Failed to load MCP tools", log, StringComparison.Ordinal);
                }
                finally
                {
                    Console.SetOut(originalOut);
                }
            }

            await Task.CompletedTask;
        });
    }

    [Fact]
    public async Task GetAvailableToolsAsync_RegistersDotnetTestTool()
    {
        var service = new ToolService(
            new RecordingMcpProxy(),
            new AllowAllProcessGuard(),
            new EmptyGoalManager(),
            new PermissiveFileSystemGuard(),
            extensionRegistry: new StubExtensionRegistry());

        var tools = await service.GetAvailableToolsAsync();

        Assert.Contains(tools, tool => string.Equals(tool.Name, "dotnet_test", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteToolAsync_ReturnsPermitDenial_WithoutInvokingHandler()
    {
        var service = new ToolService(
            new RecordingMcpProxy(),
            new AllowAllProcessGuard(),
            new EmptyGoalManager(),
            new PermissiveFileSystemGuard(),
            permit: new DenyAllToolPermitService(),
            extensionRegistry: new StubExtensionRegistry());

        var result = await service.ExecuteToolAsync("read_file", new Dictionary<string, object>
        {
            ["path"] = @"C:\forbidden.txt"
        });

        Assert.False(result.Success);
        Assert.Equal("Denied by test permit.", result.Error);
    }

    [Fact]
    public async Task ExecuteToolAsync_RecordsPermitDenialAudit()
    {
        var audit = new RecordingToolAuditService();
        var service = new ToolService(
            new RecordingMcpProxy(),
            new AllowAllProcessGuard(),
            new EmptyGoalManager(),
            new PermissiveFileSystemGuard(),
            permit: new DenyAllToolPermitService(),
            audit: audit,
            extensionRegistry: new StubExtensionRegistry());

        var result = await service.ExecuteToolAsync("read_file", new Dictionary<string, object>
        {
            ["path"] = @"C:\forbidden.txt"
        });

        Assert.False(result.Success);
        var entry = Assert.Single(audit.Entries);
        Assert.Equal("read_file", entry.ToolName);
        Assert.Equal("PERMIT_DENY", entry.Operation);
        Assert.False(entry.Success);
        Assert.Equal("tool_service", entry.Source);
    }

    [Fact]
    public async Task GetAvailableToolsAsync_RegistersAndExecutesDiscoveredMcpTool_WhenManifestPassesPolicy()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"helper-toolservice-mcp-{Guid.NewGuid():N}");
        var extensionRoot = Path.Combine(tempRoot, "mcp_config", "extensions");
        var toolRoot = Path.Combine(tempRoot, "bin");
        Directory.CreateDirectory(extensionRoot);
        Directory.CreateDirectory(toolRoot);

        await File.WriteAllTextAsync(
            Path.Combine(extensionRoot, "demo.external.json"),
            """
            {
              "schemaVersion": "helper.extension.manifest/v1",
              "id": "demo",
              "displayName": "Demo MCP",
              "category": "external",
              "providerType": "mcp",
              "transport": "stdio",
              "description": "Demo MCP provider.",
              "command": "powershell",
              "args": ["-NoLogo"],
              "requiredEnv": [],
              "capabilities": ["demo"],
              "trustLevel": "trusted_external",
              "defaultEnabled": true,
              "disabledInCertificationMode": false,
              "quietWhenUnavailable": false
            }
            """);
        await File.WriteAllTextAsync(Path.Combine(toolRoot, "powershell.cmd"), "@echo off\r\n");

        var proxy = new RecordingMcpProxy
        {
            DiscoveredTools = [new McpTool("lookup", "Lookup demo data.", new { })]
        };
        var audit = new RecordingToolAuditService();

        await RunWithEnvAsync(
            new Dictionary<string, string?>
            {
                ["PATH"] = string.Join(Path.PathSeparator, toolRoot, Environment.GetEnvironmentVariable("PATH"))
            },
            async () =>
            {
                await RunInWorkspaceAsync(tempRoot, async () =>
                {
                    var registry = new ExtensionRegistry();
                    var service = new ToolService(
                        proxy,
                        new AllowAllProcessGuard(),
                        new EmptyGoalManager(),
                        new PermissiveFileSystemGuard(),
                        audit: audit,
                        extensionRegistry: registry);

                    var tools = await service.GetAvailableToolsAsync();
                    Assert.Contains(tools, tool => string.Equals(tool.Name, "demo_lookup", StringComparison.Ordinal));
                    Assert.Equal(1, proxy.DiscoverCalls);

                    var result = await service.ExecuteToolAsync("demo_lookup", new Dictionary<string, object>
                    {
                        ["query"] = "status"
                    });

                    Assert.True(result.Success);
                    Assert.Equal(1, proxy.CallCalls);
                    Assert.Contains(audit.Entries, entry =>
                        string.Equals(entry.ToolName, "demo_lookup", StringComparison.Ordinal) &&
                        string.Equals(entry.Operation, "MCP_CALL", StringComparison.Ordinal) &&
                        string.Equals(entry.Source, "tool_service", StringComparison.Ordinal));
                });
            });
    }

    private sealed class RecordingMcpProxy : IMcpProxyService
    {
        public int DiscoverCalls { get; private set; }
        public int CallCalls { get; private set; }
        public List<McpTool> DiscoveredTools { get; init; } = [];

        public Task<List<McpTool>> DiscoverExternalToolsAsync(string serverPath, string arguments, CancellationToken ct = default)
        {
            DiscoverCalls++;
            return Task.FromResult(DiscoveredTools.ToList());
        }

        public Task<List<McpTool>> DiscoverExternalToolsAsync(string serverPath, IReadOnlyList<string> arguments, CancellationToken ct = default)
            => DiscoverExternalToolsAsync(serverPath, string.Join(" ", arguments), ct);

        public Task<ToolExecutionResult> CallExternalToolAsync(string serverPath, string arguments, string toolName, Dictionary<string, object> args, CancellationToken ct = default)
        {
            CallCalls++;
            return Task.FromResult(new ToolExecutionResult(true, $"{{\"tool\":\"{toolName}\"}}"));
        }

        public Task<ToolExecutionResult> CallExternalToolAsync(string serverPath, IReadOnlyList<string> arguments, string toolName, Dictionary<string, object> args, CancellationToken ct = default)
            => CallExternalToolAsync(serverPath, string.Join(" ", arguments), toolName, args, ct);
    }

    private sealed class StubExtensionRegistry : IExtensionRegistry
    {
        public ExtensionRegistrySnapshot GetSnapshot()
            => new([], [], []);

        public IReadOnlyList<ExtensionManifest> GetByCategory(ExtensionCategory category)
            => [];

        public bool TryGetManifest(string extensionId, out ExtensionManifest manifest)
        {
            manifest = null!;
            return false;
        }
    }

    private sealed class DenyAllToolPermitService : IToolPermitService
    {
        public Task<ToolPermitDecision> DecideAsync(string toolName, Dictionary<string, object> arguments, CancellationToken ct = default)
            => Task.FromResult(new ToolPermitDecision(false, "Denied by test permit."));
    }

    private sealed class AllowAllProcessGuard : IProcessGuard
    {
        public void EnsureSafeCommand(string command, string? workingDir = null, List<Goal>? activeGoals = null)
        {
        }

        public Task EnsureSafeCommandAsync(string command, string? workingDir = null, List<Goal>? activeGoals = null, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class EmptyGoalManager : IGoalManager
    {
        public Task<List<Goal>> GetGoalsAsync(bool includeCompleted = true, CancellationToken ct = default)
            => Task.FromResult(new List<Goal>());

        public Task<List<Goal>> GetActiveGoalsAsync(CancellationToken ct = default)
            => Task.FromResult(new List<Goal>());

        public Task AddGoalAsync(string title, string description, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<bool> UpdateGoalAsync(Guid id, string title, string description, CancellationToken ct = default)
            => Task.FromResult(false);

        public Task<bool> DeleteGoalAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult(false);

        public Task<bool> MarkGoalCompletedAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult(false);
    }

    private sealed class PermissiveFileSystemGuard : IFileSystemGuard
    {
        public string GetFullPath(string relativePath)
            => Path.GetFullPath(relativePath);

        public void EnsureSafePath(string path)
        {
        }
    }

    private sealed class RecordingToolAuditService : IToolAuditService
    {
        public List<ToolAuditEntry> Entries { get; } = [];

        public void Record(ToolAuditEntry entry)
            => Entries.Add(entry);

        public ToolAuditSnapshot GetSnapshot()
            => new(Entries.Count, Entries.Count(entry => !entry.Success), 1.0, [], [], []);
    }

    private static async Task RunInWorkspaceAsync(string workspaceRoot, Func<Task> action)
    {
        lock (WorkspaceSync)
        {
            Directory.CreateDirectory(workspaceRoot);
        }

        var originalDirectory = Directory.GetCurrentDirectory();
        try
        {
            lock (WorkspaceSync)
            {
                Directory.SetCurrentDirectory(workspaceRoot);
            }

            await action();
        }
        finally
        {
            lock (WorkspaceSync)
            {
                Directory.SetCurrentDirectory(originalDirectory);
                if (Directory.Exists(workspaceRoot))
                {
                    Directory.Delete(workspaceRoot, recursive: true);
                }
            }
        }
    }

    private static async Task RunWithEnvAsync(IReadOnlyDictionary<string, string?> overrides, Func<Task> action)
    {
        Dictionary<string, string?> previous;
        lock (EnvSync)
        {
            previous = overrides.ToDictionary(
                pair => pair.Key,
                pair => Environment.GetEnvironmentVariable(pair.Key),
                StringComparer.OrdinalIgnoreCase);

            foreach (var pair in overrides)
            {
                Environment.SetEnvironmentVariable(pair.Key, pair.Value);
            }
        }

        try
        {
            await action();
        }
        finally
        {
            lock (EnvSync)
            {
                foreach (var pair in previous)
                {
                    Environment.SetEnvironmentVariable(pair.Key, pair.Value);
                }
            }
        }
    }
}

