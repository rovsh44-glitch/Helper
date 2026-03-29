namespace Helper.Runtime.Generation;

public sealed class GenerationStageTimeoutPolicy : IGenerationStageTimeoutPolicy
{
    private readonly TimeSpan _globalTimeout;
    private readonly TimeSpan _routingTimeout;
    private readonly TimeSpan _forgeTimeout;
    private readonly TimeSpan _synthesisTimeout;
    private readonly TimeSpan _autofixTimeout;

    public GenerationStageTimeoutPolicy()
    {
        var globalSeconds = ReadInt("HELPER_CREATE_TIMEOUT_SEC", 900, 30, 900);
        _globalTimeout = TimeSpan.FromSeconds(globalSeconds);
        _routingTimeout = TimeSpan.FromSeconds(ReadInt("HELPER_CREATE_TIMEOUT_ROUTING_SEC", Math.Min(12, globalSeconds), 2, 180));
        _forgeTimeout = TimeSpan.FromSeconds(ReadInt("HELPER_CREATE_TIMEOUT_FORGE_SEC", Math.Min(30, globalSeconds), 5, 600));
        _synthesisTimeout = TimeSpan.FromSeconds(ReadInt("HELPER_CREATE_TIMEOUT_SYNTHESIS_SEC", globalSeconds, 10, 900));
        _autofixTimeout = TimeSpan.FromSeconds(ReadInt("HELPER_CREATE_TIMEOUT_AUTOFIX_SEC", Math.Min(45, globalSeconds), 5, 600));
    }

    public TimeSpan Resolve(GenerationTimeoutStage stage)
    {
        var stageTimeout = stage switch
        {
            GenerationTimeoutStage.Routing => _routingTimeout,
            GenerationTimeoutStage.Forge => _forgeTimeout,
            GenerationTimeoutStage.Synthesis => _synthesisTimeout,
            GenerationTimeoutStage.Autofix => _autofixTimeout,
            _ => _globalTimeout
        };

        return stageTimeout <= _globalTimeout ? stageTimeout : _globalTimeout;
    }

    private static int ReadInt(string envName, int fallback, int min, int max)
    {
        var raw = Environment.GetEnvironmentVariable(envName);
        if (!int.TryParse(raw, out var parsed))
        {
            parsed = fallback;
        }

        return Math.Clamp(parsed, min, max);
    }
}

