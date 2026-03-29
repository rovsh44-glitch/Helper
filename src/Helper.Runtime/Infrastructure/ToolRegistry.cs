using Helper.Runtime.Core;

namespace Helper.Runtime.Infrastructure;

internal sealed class ToolRegistry
{
    private readonly Dictionary<string, Func<Dictionary<string, object>, CancellationToken, Task<ToolExecutionResult>>> _handlers = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ToolDefinition> _definitions = new();

    public void Register(
        string name,
        string description,
        Dictionary<string, string> parameters,
        Func<Dictionary<string, object>, CancellationToken, Task<ToolExecutionResult>> handler)
    {
        _definitions.RemoveAll(existing => string.Equals(existing.Name, name, StringComparison.OrdinalIgnoreCase));
        _definitions.Add(new ToolDefinition(name, description, parameters));
        _handlers[name] = handler;
    }

    public bool TryGetHandler(
        string name,
        out Func<Dictionary<string, object>, CancellationToken, Task<ToolExecutionResult>> handler)
        => _handlers.TryGetValue(name, out handler!);

    public List<ToolDefinition> SnapshotDefinitions()
        => _definitions.ToList();
}

