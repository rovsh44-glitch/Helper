namespace Helper.Api.Conversation.InteractionState;

public interface ILatentInteractionSignalProvider
{
    IReadOnlyList<string> GetSignals(ChatTurnContext context);
}
