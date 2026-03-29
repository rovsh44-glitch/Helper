using Helper.Runtime.Core;

namespace Helper.Runtime.Generation;

public sealed class FixSafetyPolicy : IFixSafetyPolicy
{
    private readonly bool _allowL3ByDefault;

    public FixSafetyPolicy()
    {
        _allowL3ByDefault = ReadFlag("HELPER_FIX_ALLOW_L3", false);
    }

    public FixSafetyTier ResolveTier(FixStrategyKind strategy)
    {
        return strategy switch
        {
            FixStrategyKind.DeterministicCompileGate => FixSafetyTier.L1SafeDeterministic,
            FixStrategyKind.RuntimeConfig => FixSafetyTier.L1SafeDeterministic,
            FixStrategyKind.Regenerate => FixSafetyTier.L2GuardedStructural,
            FixStrategyKind.LlmAutoHealer => FixSafetyTier.L3RiskySemantic,
            _ => FixSafetyTier.L2GuardedStructural
        };
    }

    public bool IsAllowed(
        FixStrategyKind strategy,
        GenerationRequest request,
        GenerationResult current,
        out string reason)
    {
        var tier = ResolveTier(strategy);
        if (tier != FixSafetyTier.L3RiskySemantic)
        {
            reason = "Allowed: non-L3 strategy.";
            return true;
        }

        if (_allowL3ByDefault)
        {
            reason = "Allowed: HELPER_FIX_ALLOW_L3=true.";
            return true;
        }

        if (request.Metadata is not null &&
            request.Metadata.TryGetValue("allow_l3_fix", out var allowL3Metadata) &&
            bool.TryParse(allowL3Metadata, out var allowFromMetadata) &&
            allowFromMetadata)
        {
            reason = "Allowed: request metadata allow_l3_fix=true.";
            return true;
        }

        if (!string.IsNullOrWhiteSpace(request.Prompt) &&
            request.Prompt.Contains("[allow-l3-fix]", StringComparison.OrdinalIgnoreCase))
        {
            reason = "Allowed: explicit prompt policy marker [allow-l3-fix].";
            return true;
        }

        reason = "Blocked: L3 risky strategy requires explicit policy enablement.";
        return false;
    }

    private static bool ReadFlag(string envName, bool fallback)
    {
        var raw = Environment.GetEnvironmentVariable(envName);
        return bool.TryParse(raw, out var parsed) ? parsed : fallback;
    }
}


