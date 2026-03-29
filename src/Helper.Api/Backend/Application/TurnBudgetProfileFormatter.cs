using Helper.Api.Conversation;

namespace Helper.Api.Backend.Application;

public static class TurnBudgetProfileFormatter
{
    public static string Format(TurnBudgetProfile profile)
    {
        return profile switch
        {
            TurnBudgetProfile.ChatGrounded => "chat-grounded",
            TurnBudgetProfile.Research => "research",
            TurnBudgetProfile.Generation => "generation",
            TurnBudgetProfile.HighRisk => "high-risk",
            _ => "chat-light"
        };
    }
}

