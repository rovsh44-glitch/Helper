namespace Helper.Api.Conversation;

public interface ICriticRiskPolicy
{
    CriticRiskTier Evaluate(ChatTurnContext context);
    bool AllowFailOpen(CriticRiskTier tier);
}

