using Helper.Runtime.Core;

namespace Helper.Runtime.Infrastructure;

public sealed class ToolService : IToolService
{
    private readonly ToolRegistry _registry;
    private readonly IToolPermitService? _permit;
    private readonly IToolAuditService? _audit;
    private readonly Lazy<Task> _mcpToolsLoader;

    public ToolService(
        IMcpProxyService mcpProxy,
        IProcessGuard processGuard,
        IGoalManager goalManager,
        IFileSystemGuard fileGuard,
        ISafetyGuard? safetyGuard = null,
        IToolPermitService? permit = null,
        IToolAuditService? audit = null,
        IExtensionRegistry extensionRegistry = null!)
    {
        ArgumentNullException.ThrowIfNull(extensionRegistry);

        _permit = permit;
        _audit = audit;
        _registry = new ToolRegistry();

        var executionGateway = new ToolExecutionGateway(processGuard, goalManager, fileGuard, safetyGuard, audit);
        new BuiltinToolRegistry(executionGateway).Register(_registry);

        var mcpBootstrapper = new McpToolBootstrapper(mcpProxy, audit, extensionRegistry);
        _mcpToolsLoader = new Lazy<Task>(() => mcpBootstrapper.RegisterAsync(_registry, CancellationToken.None));

        _ = _mcpToolsLoader.Value;
    }

    public async Task<List<ToolDefinition>> GetAvailableToolsAsync(CancellationToken ct = default)
    {
        await EnsureMcpToolsLoadedAsync(ct).ConfigureAwait(false);
        return _registry.SnapshotDefinitions();
    }

    public async Task<ToolExecutionResult> ExecuteToolAsync(string name, Dictionary<string, object> arguments, CancellationToken ct = default)
    {
        await EnsureMcpToolsLoadedAsync(ct).ConfigureAwait(false);
        if (!_registry.TryGetHandler(name, out var handler))
        {
            return new ToolExecutionResult(false, string.Empty, $"Tool not found: {name}");
        }

        if (_permit is not null)
        {
            var decision = await _permit.DecideAsync(name, arguments, ct).ConfigureAwait(false);
            if (!decision.Allowed)
            {
                _audit?.Record(new ToolAuditEntry(DateTimeOffset.UtcNow, name, "PERMIT_DENY", false, decision.Reason, Source: "tool_service"));
                return new ToolExecutionResult(false, string.Empty, decision.Reason);
            }
        }

        try
        {
            return await handler(arguments, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _audit?.Record(new ToolAuditEntry(DateTimeOffset.UtcNow, name, "UNHANDLED", false, ex.Message, Source: "tool_service"));
            return new ToolExecutionResult(false, string.Empty, ex.Message);
        }
    }

    private async Task EnsureMcpToolsLoadedAsync(CancellationToken ct)
    {
        try
        {
            await _mcpToolsLoader.Value.WaitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ToolService] MCP preload failed: {ex.Message}");
        }
    }
}

