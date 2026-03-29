using Helper.Runtime.Core;

namespace Helper.Runtime.Generation;

public sealed class GenerationFixPlanner : IFixPlanner
{
    private readonly int _maxAttempts;
    private readonly bool _enableLlmStrategy;
    private readonly bool _enableRegenerateStrategy;
    private readonly bool _enableRuntimeConfigStrategy;
    private readonly bool _smokeProfile;
    private readonly IReadOnlyDictionary<FixStrategyKind, double> _historicalWinRates;

    public GenerationFixPlanner(IFixStrategyHistoryProvider? historyProvider = null)
    {
        _maxAttempts = ReadInt("HELPER_FIX_LOOP_MAX_ATTEMPTS", 3, 1, 8);
        _enableLlmStrategy = ReadFlag("HELPER_ENABLE_LLM_FIX_STRATEGY", true);
        _enableRegenerateStrategy = ReadFlag("HELPER_ENABLE_REGENERATE_FIX_STRATEGY", true);
        _enableRuntimeConfigStrategy = ReadFlag("HELPER_ENABLE_RUNTIME_CONFIG_FIX_STRATEGY", true);
        _smokeProfile = ReadFlag("HELPER_SMOKE_PROFILE", false);
        _historicalWinRates = (historyProvider ?? new FileFixStrategyHistoryProvider()).GetWinRates();
    }

    public FixPlan CreatePlan(GenerationRequest request, GenerationResult initialResult)
    {
        if (initialResult.Errors.Count == 0)
        {
            return new FixPlan(Array.Empty<FixStrategyKind>(), 0);
        }

        var strategies = new List<FixStrategyKind>
        {
            FixStrategyKind.DeterministicCompileGate
        };

        if (_enableRuntimeConfigStrategy && !_smokeProfile)
        {
            strategies.Add(FixStrategyKind.RuntimeConfig);
        }

        if (!_smokeProfile && _enableLlmStrategy)
        {
            strategies.Add(FixStrategyKind.LlmAutoHealer);
        }

        if (_enableRegenerateStrategy && !_smokeProfile)
        {
            strategies.Add(FixStrategyKind.Regenerate);
        }

        var prioritized = Prioritize(strategies);
        var maxAttempts = _smokeProfile ? 1 : _maxAttempts;
        return new FixPlan(prioritized, maxAttempts);
    }

    private IReadOnlyList<FixStrategyKind> Prioritize(IReadOnlyList<FixStrategyKind> strategies)
    {
        if (strategies.Count <= 1)
        {
            return strategies.ToList();
        }

        // Keep deterministic repair first to guarantee semantic-safe baseline,
        // then prioritize remaining strategies by historical win-rate.
        var list = strategies.Distinct().ToList();
        var deterministicFirst = list.Remove(FixStrategyKind.DeterministicCompileGate)
            ? new[] { FixStrategyKind.DeterministicCompileGate }
            : Array.Empty<FixStrategyKind>();

        var orderedTail = list
            .OrderByDescending(x => _historicalWinRates.TryGetValue(x, out var rate) ? rate : 0.0)
            .ThenBy(x => x.ToString(), StringComparer.OrdinalIgnoreCase)
            .ToList();

        return deterministicFirst.Concat(orderedTail).ToList();
    }

    private static bool ReadFlag(string envName, bool fallback)
    {
        var raw = Environment.GetEnvironmentVariable(envName);
        return bool.TryParse(raw, out var parsed) ? parsed : fallback;
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

