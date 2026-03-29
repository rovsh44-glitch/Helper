namespace Helper.Api.Conversation;

internal static class TurnLifecycleProgression
{
    internal static void EnsureReadyToFinalize(ChatTurnContext context, ITurnLifecycleStateMachine lifecycle)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(lifecycle);

        if (context.LifecycleState is TurnLifecycleState.Finalize or TurnLifecycleState.PostAudit)
        {
            return;
        }

        var policyValidatedState = context.RequiresClarification
            ? TurnLifecycleState.Clarify
            : TurnLifecycleState.Verify;

        if (context.LifecycleState == policyValidatedState)
        {
            return;
        }

        if (lifecycle.CanTransition(context.LifecycleState, policyValidatedState))
        {
            lifecycle.Transition(context, policyValidatedState);
        }
    }
}

