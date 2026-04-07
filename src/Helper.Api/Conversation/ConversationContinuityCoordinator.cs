namespace Helper.Api.Conversation;

public interface IConversationContinuityCoordinator
{
    void CaptureTurnStart(ConversationState state, ChatTurnContext context);
}

public sealed class ConversationContinuityCoordinator : IConversationContinuityCoordinator
{
    public void CaptureTurnStart(ConversationState state, ChatTurnContext context)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(context);
    }
}
