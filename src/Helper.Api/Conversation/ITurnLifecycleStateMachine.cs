namespace Helper.Api.Conversation;

public interface ITurnLifecycleStateMachine
{
    void Transition(ChatTurnContext context, TurnLifecycleState nextState);
    bool CanTransition(TurnLifecycleState currentState, TurnLifecycleState nextState);
    bool TryRecoverToFinalize(ChatTurnContext context, out string recoveryReason);
}

