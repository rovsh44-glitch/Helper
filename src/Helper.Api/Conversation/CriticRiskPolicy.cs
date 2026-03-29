using Helper.Runtime.Core;
using Helper.Api.Hosting;
using Helper.Api.Backend.Configuration;

namespace Helper.Api.Conversation;

public sealed class CriticRiskPolicy : ICriticRiskPolicy
{
    private static readonly string[] HighRiskTokens =
    {
        "delete", "drop", "remove all", "format disk", "shutdown", "kill process",
        "production", "database migration", "medication", "dosage", "diagnosis",
        "legal advice", "investment advice", "prescribe", "удали", "прод", "дозировка",
        "диагноз", "инвестицион", "юридическ"
    };

    private readonly bool _allowFailOpenHighRisk;
    private readonly bool _allowFailOpenMediumRisk;

    public CriticRiskPolicy()
    {
        _allowFailOpenHighRisk = ReadFlag("HELPER_CRITIC_FAILOPEN_HIGH_RISK", false);
        _allowFailOpenMediumRisk = ReadFlag("HELPER_CRITIC_FAILOPEN_MEDIUM_RISK", true);
    }

    public CriticRiskTier Evaluate(ChatTurnContext context)
    {
        var text = context.Request.Message?.Trim() ?? string.Empty;
        if (context.RequiresConfirmation || context.IsFactualPrompt || ContainsAny(text, HighRiskTokens))
        {
            return CriticRiskTier.High;
        }

        if (context.Intent.Intent == IntentType.Research || context.Sources.Count > 0)
        {
            return CriticRiskTier.Medium;
        }

        return CriticRiskTier.Low;
    }

    public bool AllowFailOpen(CriticRiskTier tier)
    {
        return tier switch
        {
            CriticRiskTier.Low => true,
            CriticRiskTier.Medium => _allowFailOpenMediumRisk,
            CriticRiskTier.High => _allowFailOpenHighRisk,
            _ => false
        };
    }

    private static bool ContainsAny(string text, IEnumerable<string> tokens)
    {
        foreach (var token in tokens)
        {
            if (text.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ReadFlag(string envName, bool fallback)
    {
        var raw = Environment.GetEnvironmentVariable(envName);
        return bool.TryParse(raw, out var parsed) ? parsed : fallback;
    }
}

