using Helper.Runtime.Core;

namespace Helper.Runtime.Infrastructure;

internal sealed class McpToolBootstrapper
{
    private readonly IMcpProxyService _mcpProxy;
    private readonly IToolAuditService? _audit;
    private readonly IExtensionRegistry _extensionRegistry;
    private readonly object _bootstrapLogSync = new();
    private bool _bootstrapSummaryLogged;

    public McpToolBootstrapper(
        IMcpProxyService mcpProxy,
        IToolAuditService? audit,
        IExtensionRegistry extensionRegistry)
    {
        _mcpProxy = mcpProxy;
        _audit = audit;
        _extensionRegistry = extensionRegistry;
    }

    public async Task RegisterAsync(ToolRegistry registry, CancellationToken ct)
    {
        try
        {
            var bootstrapSummary = new List<string>();
            var certificationMode = ToolCommandPolicy.IsCertificationMode();
            var manifests = _extensionRegistry
                .GetSnapshot()
                .Manifests
                .Where(ToolCommandPolicy.IsMcpExtensionManifest)
                .ToArray();

            foreach (var manifest in manifests)
            {
                if (!ToolCommandPolicy.ShouldActivateManifest(manifest, certificationMode))
                {
                    continue;
                }

                var providerId = manifest.Id;
                var command = manifest.Command ?? string.Empty;
                if (!ToolCommandPolicy.IsMcpCommandAllowed(command))
                {
                    AppendBootstrapReason(bootstrapSummary, manifest, $"command '{command}' blocked by policy");
                    continue;
                }

                if (!ToolCommandPolicy.TryResolveExecutablePath(command, out var resolvedCommand))
                {
                    AppendBootstrapReason(bootstrapSummary, manifest, $"command '{command}' not found in PATH");
                    continue;
                }

                if (!ToolCommandPolicy.TryValidateMcpServerEnvironment(manifest, out var environmentReason))
                {
                    AppendBootstrapReason(bootstrapSummary, manifest, environmentReason);
                    continue;
                }

                if (ToolCommandPolicy.ShouldSkipCommandBootstrap(command, manifest.Args, out var bootstrapReason))
                {
                    AppendBootstrapReason(bootstrapSummary, manifest, bootstrapReason);
                    continue;
                }

                try
                {
                    var mcpTools = await _mcpProxy.DiscoverExternalToolsAsync(resolvedCommand, manifest.Args, ct).ConfigureAwait(false);
                    foreach (var mcpTool in mcpTools)
                    {
                        RegisterDiscoveredTool(registry, providerId, resolvedCommand, manifest.Args, mcpTool);
                    }

                    if (mcpTools.Count == 0)
                    {
                        AppendBootstrapReason(bootstrapSummary, manifest, "discovery returned 0 tools");
                    }
                }
                catch (Exception ex)
                {
                    AppendBootstrapReason(bootstrapSummary, manifest, $"discovery failed ({ex.Message})");
                }
            }

            LogBootstrapSummary(bootstrapSummary);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ToolService] MCP Load Error: {ex.Message}");
        }
    }

    private void RegisterDiscoveredTool(
        ToolRegistry registry,
        string providerId,
        string resolvedCommand,
        IReadOnlyList<string> args,
        McpTool mcpTool)
    {
        var toolId = $"{providerId}_{mcpTool.Name}";
        registry.Register(toolId, $"[MCP] {mcpTool.Description}", new Dictionary<string, string>(), async (callArgs, ct) =>
        {
            var startedAt = DateTimeOffset.UtcNow;
            var response = await _mcpProxy.CallExternalToolAsync(resolvedCommand, args, mcpTool.Name, callArgs, ct).ConfigureAwait(false);
            _audit?.Record(new ToolAuditEntry(
                startedAt,
                toolId,
                "MCP_CALL",
                response.Success,
                response.Error,
                $"{resolvedCommand} {string.Join(" ", args)}".Trim(),
                "tool_service"));
            return response;
        });
    }

    private void LogBootstrapSummary(IReadOnlyCollection<string> summary)
    {
        if (summary.Count == 0)
        {
            return;
        }

        lock (_bootstrapLogSync)
        {
            if (_bootstrapSummaryLogged)
            {
                return;
            }

            _bootstrapSummaryLogged = true;
            Console.WriteLine($"[ToolService] Optional MCP bootstrap summary: {string.Join("; ", summary)}");
        }
    }

    private static void AppendBootstrapReason(List<string> bootstrapSummary, ExtensionManifest manifest, string reason)
    {
        if (manifest.QuietWhenUnavailable)
        {
            return;
        }

        bootstrapSummary.Add($"{manifest.Id}: {reason}");
    }
}

