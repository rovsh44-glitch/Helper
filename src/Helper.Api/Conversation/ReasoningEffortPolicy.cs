using Helper.Runtime.Core;

namespace Helper.Api.Conversation;

public interface IReasoningEffortPolicy
{
    string Resolve(ChatTurnContext context, PersonalizationProfile profile);
}

public sealed class ReasoningEffortPolicy : IReasoningEffortPolicy
{
    public string Resolve(ChatTurnContext context, PersonalizationProfile profile)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(profile);

        if (!string.IsNullOrWhiteSpace(context.Request.SystemInstruction) &&
            context.Request.SystemInstruction.Contains("deep", StringComparison.OrdinalIgnoreCase))
        {
            return "deep";
        }

        if (context.IsFactualPrompt || context.Intent.Intent == IntentType.Research || context.BudgetProfile == TurnBudgetProfile.HighRisk)
        {
            return "deep";
        }

        return profile.ReasoningEffort switch
        {
            "fast" => "fast",
            "deep" => "deep",
            _ => ResolveFallbackEffort(context)
        };
    }

    private string ResolveFallbackEffort(ChatTurnContext context)
    {
        if (context.ExecutionMode == TurnExecutionMode.Fast)
        {
            return "fast";
        }

        return "balanced";
    }
}
