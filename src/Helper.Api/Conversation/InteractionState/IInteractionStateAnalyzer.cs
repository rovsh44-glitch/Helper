namespace Helper.Api.Conversation.InteractionState;

public interface IInteractionStateAnalyzer
{
    InteractionStateSnapshot Analyze(ChatTurnContext context);
}
