using Helper.Runtime.Core;
using Helper.Runtime.Infrastructure;

namespace Helper.Runtime.Tests;

public sealed class ExtensionRegistryTests
{
    private static readonly object EnvSync = new();

    [Fact]
    public async Task ExtensionRegistry_LoadsVersionedManifests_AndRejectsAbsolutePaths()
    {
        var tempRoot = CreateTempWorkspace();
        await WriteManifestAsync(
            tempRoot,
            "helper.local-tools.json",
            """
            {
              "schemaVersion": "helper.extension.manifest/v1",
              "id": "helper-local-tools",
              "displayName": "Helper Local Tools",
              "category": "built_in",
              "providerType": "local_tools",
              "transport": "none",
              "description": "Built-in local tools.",
              "declaredTools": ["shell_execute", "dotnet_test", "read_file", "write_file"],
              "capabilities": ["filesystem", "process-execution"],
              "trustLevel": "built_in",
              "defaultEnabled": true,
              "disabledInCertificationMode": false,
              "quietWhenUnavailable": false
            }
            """);

        await WriteManifestAsync(
            tempRoot,
            "invalid.sqlite.json",
            """
            {
              "schemaVersion": "helper.extension.manifest/v1",
              "id": "sqlite",
              "displayName": "SQLite MCP",
              "category": "external",
              "providerType": "mcp",
              "transport": "stdio",
              "description": "Invalid because the args path is absolute.",
              "command": "python",
              "args": ["-m", "mcp_server_sqlite", "--db-path", "C:/machine/local/memory.db"],
              "requiredEnv": [],
              "capabilities": ["sqlite"],
              "trustLevel": "trusted_external",
              "defaultEnabled": false,
              "disabledInCertificationMode": true,
              "quietWhenUnavailable": true
            }
            """);

        await RunInWorkspaceAsync(tempRoot, () =>
        {
            var registry = new ExtensionRegistry();
            var snapshot = registry.GetSnapshot();

            Assert.Single(snapshot.Manifests);
            Assert.Equal("helper-local-tools", snapshot.Manifests[0].Id);
            Assert.Equal(ExtensionCategory.BuiltIn, snapshot.Manifests[0].Category);
            Assert.Single(snapshot.Failures);
            Assert.Contains("not portable", snapshot.Failures[0], StringComparison.OrdinalIgnoreCase);

            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task ToolPermitService_UsesManifestTrustAndAllowedTools()
    {
        var tempRoot = CreateTempWorkspace();
        await WriteManifestAsync(
            tempRoot,
            "sample.github.external.json",
            """
            {
              "schemaVersion": "helper.extension.manifest/v1",
              "id": "github",
              "displayName": "GitHub MCP",
              "category": "external",
              "providerType": "mcp",
              "transport": "stdio",
              "description": "Disabled sample external provider.",
              "command": "npx",
              "args": ["-y", "@modelcontextprotocol/server-github"],
              "requiredEnv": ["GITHUB_PERSONAL_ACCESS_TOKEN"],
              "capabilities": ["issues", "pull-requests"],
              "trustLevel": "trusted_external",
              "defaultEnabled": false,
              "disabledInCertificationMode": true,
              "quietWhenUnavailable": true,
              "toolPolicy": {
                "allowAllTools": false,
                "allowedTools": ["searchIssues"]
              }
            }
            """);

        await RunInWorkspaceAsync(tempRoot, async () =>
        {
            var registry = new ExtensionRegistry();
            var permit = new ToolPermitService(registry);

            var allowed = await permit.DecideAsync("mcp:github:searchIssues", new Dictionary<string, object>());
            var denied = await permit.DecideAsync("mcp:github:createPullRequest", new Dictionary<string, object>());

            Assert.True(allowed.Allowed);
            Assert.False(denied.Allowed);
            Assert.Contains("provider policy", denied.Reason, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public async Task ToolService_KeepsOptionalProvidersQuietInCertificationMode()
    {
        var tempRoot = CreateTempWorkspace();
        await WriteManifestAsync(
            tempRoot,
            "sample.brave-search.external.json",
            """
            {
              "schemaVersion": "helper.extension.manifest/v1",
              "id": "brave-search",
              "displayName": "Brave Search MCP",
              "category": "external",
              "providerType": "mcp",
              "transport": "stdio",
              "description": "Disabled external search provider.",
              "command": "npx",
              "args": ["-y", "@modelcontextprotocol/server-brave-search"],
              "requiredEnv": ["BRAVE_API_KEY"],
              "capabilities": ["web-search"],
              "trustLevel": "trusted_external",
              "defaultEnabled": true,
              "disabledInCertificationMode": true,
              "quietWhenUnavailable": true
            }
            """);

        await RunWithEnvAsync(new Dictionary<string, string?> { ["HELPER_CERT_MODE"] = "true" }, async () =>
        {
            await RunInWorkspaceAsync(tempRoot, async () =>
            {
                var originalOut = Console.Out;
                var output = new StringWriter();
                try
                {
                    Console.SetOut(output);
                    var registry = new ExtensionRegistry();
                    var proxy = new RecordingMcpProxy();
                    var service = new ToolService(
                        proxy,
                        new AllowAllProcessGuard(),
                        new EmptyGoalManager(),
                        new PermissiveFileSystemGuard(),
                        permit: new ToolPermitService(registry),
                        extensionRegistry: registry);

                    var tools = await service.GetAvailableToolsAsync();

                    Assert.NotEmpty(tools);
                    Assert.Equal(0, proxy.DiscoverCalls);
                    Assert.DoesNotContain("brave-search", output.ToString(), StringComparison.OrdinalIgnoreCase);
                    Assert.DoesNotContain("Optional MCP bootstrap summary", output.ToString(), StringComparison.Ordinal);
                }
                finally
                {
                    Console.SetOut(originalOut);
                }
            });
        });
    }

    private static string CreateTempWorkspace()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"helper-extension-registry-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(tempRoot, "mcp_config", "extensions"));
        return tempRoot;
    }

    private static Task WriteManifestAsync(string tempRoot, string fileName, string content)
    {
        var fullPath = Path.Combine(tempRoot, "mcp_config", "extensions", fileName);
        return File.WriteAllTextAsync(fullPath, content);
    }

    private static async Task RunInWorkspaceAsync(string tempRoot, Func<Task> action)
    {
        var originalDirectory = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(tempRoot);
            await action();
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDirectory);
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private static async Task RunWithEnvAsync(IReadOnlyDictionary<string, string?> overrides, Func<Task> action)
    {
        Dictionary<string, string?> previous;
        lock (EnvSync)
        {
            previous = overrides.ToDictionary(
                x => x.Key,
                x => Environment.GetEnvironmentVariable(x.Key),
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

    private sealed class RecordingMcpProxy : IMcpProxyService
    {
        public int DiscoverCalls { get; private set; }

        public Task<List<McpTool>> DiscoverExternalToolsAsync(string serverPath, string arguments, CancellationToken ct = default)
        {
            DiscoverCalls++;
            return Task.FromResult(new List<McpTool>());
        }

        public Task<List<McpTool>> DiscoverExternalToolsAsync(string serverPath, IReadOnlyList<string> arguments, CancellationToken ct = default)
            => DiscoverExternalToolsAsync(serverPath, string.Join(" ", arguments), ct);

        public Task<ToolExecutionResult> CallExternalToolAsync(string serverPath, string arguments, string toolName, Dictionary<string, object> args, CancellationToken ct = default)
            => Task.FromResult(new ToolExecutionResult(true, "{}"));

        public Task<ToolExecutionResult> CallExternalToolAsync(string serverPath, IReadOnlyList<string> arguments, string toolName, Dictionary<string, object> args, CancellationToken ct = default)
            => CallExternalToolAsync(serverPath, string.Join(" ", arguments), toolName, args, ct);
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
}

