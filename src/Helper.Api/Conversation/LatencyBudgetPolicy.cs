namespace Helper.Api.Conversation;

public enum TurnExecutionMode
{
    Fast,
    Balanced,
    Deep
}

public sealed record TurnLatencyBudget(
    TurnExecutionMode Mode,
    TurnBudgetProfile Profile,
    TimeSpan TimeBudget,
    int ToolCallBudget,
    int TokenBudget,
    int ModelCallBudget,
    int BackgroundBudget,
    string Reason);

public interface ILatencyBudgetPolicy
{
    TurnLatencyBudget Resolve(ChatTurnContext context);
}

public sealed class LatencyBudgetPolicy : ILatencyBudgetPolicy
{
    private readonly TurnExecutionMode _defaultMode;

    public LatencyBudgetPolicy()
    {
        _defaultMode = ReadDefaultMode();
    }

    public TurnLatencyBudget Resolve(ChatTurnContext context)
    {
        var mode = ResolveRequestedMode(context.Request.Message, context.Request.SystemInstruction);
        var profile = ResolveProfile(context);

        // Research and long prompts need broader budgets to stay useful.
        if (context.Intent.Intent == Helper.Runtime.Core.IntentType.Research && mode == TurnExecutionMode.Fast)
        {
            mode = TurnExecutionMode.Balanced;
        }

        if (context.Request.Message.Length > 1600 && mode != TurnExecutionMode.Deep)
        {
            mode = TurnExecutionMode.Deep;
        }
        else if (context.Request.Message.Length > 900 && mode == TurnExecutionMode.Fast)
        {
            mode = TurnExecutionMode.Balanced;
        }

        var budget = mode switch
        {
            TurnExecutionMode.Fast => new TurnLatencyBudget(
                mode,
                profile,
                TimeSpan.FromSeconds(10),
                ToolCallBudget: 1,
                TokenBudget: 500,
                ModelCallBudget: 1,
                BackgroundBudget: 0,
                Reason: "Fast mode budget for short turn latency."),
            TurnExecutionMode.Deep => new TurnLatencyBudget(
                mode,
                profile,
                TimeSpan.FromSeconds(45),
                ToolCallBudget: 6,
                TokenBudget: 2600,
                ModelCallBudget: 3,
                BackgroundBudget: 2,
                Reason: "Deep mode budget for complex or research-heavy tasks."),
            _ => new TurnLatencyBudget(
                TurnExecutionMode.Balanced,
                profile,
                TimeSpan.FromSeconds(20),
                ToolCallBudget: 3,
                TokenBudget: 1400,
                ModelCallBudget: 2,
                BackgroundBudget: 1,
                Reason: "Balanced mode budget for default assistant turns.")
        };

        if (context.Intent.Intent == Helper.Runtime.Core.IntentType.Research)
        {
            budget = budget with
            {
                TimeBudget = budget.TimeBudget + TimeSpan.FromSeconds(10),
                ToolCallBudget = Math.Min(8, budget.ToolCallBudget + 1),
                TokenBudget = Math.Min(3200, budget.TokenBudget + 500),
                BackgroundBudget = Math.Min(4, budget.BackgroundBudget + 1),
                Reason = $"{budget.Reason} Adjusted for research intent."
            };
        }

        return budget;
    }

    private static TurnBudgetProfile ResolveProfile(ChatTurnContext context)
    {
        var message = context.Request.Message?.Trim() ?? string.Empty;
        if (context.RequiresConfirmation ||
            context.IsFactualPrompt ||
            ContainsHighRiskToken(message))
        {
            return TurnBudgetProfile.HighRisk;
        }

        if (context.Intent.Intent == Helper.Runtime.Core.IntentType.Research)
        {
            return TurnBudgetProfile.Research;
        }

        if (context.Intent.Intent == Helper.Runtime.Core.IntentType.Generate)
        {
            return TurnBudgetProfile.Generation;
        }

        if (context.IsFactualPrompt)
        {
            return TurnBudgetProfile.ChatGrounded;
        }

        return TurnBudgetProfile.ChatLight;
    }

    private TurnExecutionMode ResolveRequestedMode(string message, string? systemInstruction)
    {
        var combined = $"{message} {systemInstruction}".ToLowerInvariant();
        if (HasAny(combined, "fast mode", "быстро", "кратко", "short answer", "concise"))
        {
            return TurnExecutionMode.Fast;
        }

        if (HasAny(combined, "deep mode", "детально", "подробно", "deep analysis", "in depth"))
        {
            return TurnExecutionMode.Deep;
        }

        return _defaultMode;
    }

    private static bool HasAny(string text, params string[] tokens)
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

    private static bool ContainsHighRiskToken(string text)
    {
        return HasAny(text,
            "delete",
            "drop",
            "remove all",
            "format disk",
            "shutdown",
            "kill process",
            "production",
            "database migration",
            "medication",
            "dosage",
            "diagnosis",
            "legal advice",
            "investment advice",
            "prescribe",
            "удали",
            "прод",
            "дозировка",
            "диагноз",
            "инвестицион",
            "юридическ");
    }

    private static TurnExecutionMode ReadDefaultMode()
    {
        var raw = Environment.GetEnvironmentVariable("HELPER_DEFAULT_EXECUTION_MODE");
        return raw?.Trim().ToLowerInvariant() switch
        {
            "fast" => TurnExecutionMode.Fast,
            "deep" => TurnExecutionMode.Deep,
            _ => TurnExecutionMode.Balanced
        };
    }
}

public static class TokenBudgetEstimator
{
    public static int Estimate(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        // Conservative approximation for mixed RU/EN plain text.
        return Math.Max(1, (int)Math.Ceiling(text.Length / 4.0));
    }

    public static string TruncateToBudget(string text, int tokenBudget)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        if (tokenBudget <= 0)
        {
            return string.Empty;
        }

        var maxChars = Math.Max(16, tokenBudget * 4);
        if (text.Length <= maxChars)
        {
            return text;
        }

        return text[..maxChars].TrimEnd();
    }
}

