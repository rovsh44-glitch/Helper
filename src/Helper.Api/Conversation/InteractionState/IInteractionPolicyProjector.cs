namespace Helper.Api.Conversation.InteractionState;

public interface IInteractionPolicyProjector
{
    InteractionPolicyProjection Project(ChatTurnContext context, InteractionStateSnapshot snapshot);
}
