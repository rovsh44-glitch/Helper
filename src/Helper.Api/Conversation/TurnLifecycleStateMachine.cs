namespace Helper.Api.Conversation;

public sealed class TurnLifecycleStateMachine : ITurnLifecycleStateMachine
{
    private static readonly IReadOnlyDictionary<TurnLifecycleState, TurnLifecycleState[]> AllowedTransitions =
        new Dictionary<TurnLifecycleState, TurnLifecycleState[]>
        {
            [TurnLifecycleState.New] = new[] { TurnLifecycleState.Understand },
            [TurnLifecycleState.Understand] = new[] { TurnLifecycleState.Clarify, TurnLifecycleState.Execute },
            [TurnLifecycleState.Clarify] = new[] { TurnLifecycleState.Finalize },
            [TurnLifecycleState.Execute] = new[] { TurnLifecycleState.Verify },
            [TurnLifecycleState.Verify] = new[] { TurnLifecycleState.Finalize },
            [TurnLifecycleState.Finalize] = new[] { TurnLifecycleState.PostAudit },
            [TurnLifecycleState.PostAudit] = Array.Empty<TurnLifecycleState>()
        };

    private static readonly HashSet<TurnLifecycleState> RecoveryToFinalizeAllowed = new()
    {
        TurnLifecycleState.New,
        TurnLifecycleState.Understand,
        TurnLifecycleState.Clarify,
        TurnLifecycleState.Execute,
        TurnLifecycleState.Verify
    };

    public void Transition(ChatTurnContext context, TurnLifecycleState nextState)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.LifecycleState == nextState)
        {
            return;
        }

        if (!CanTransition(context.LifecycleState, nextState))
        {
            throw new InvalidOperationException(
                $"Illegal lifecycle transition: {context.LifecycleState} -> {nextState} (turn={context.TurnId}).");
        }

        context.LifecycleState = nextState;
        context.LifecycleTrace.Add(nextState);
    }

    public bool CanTransition(TurnLifecycleState currentState, TurnLifecycleState nextState)
    {
        if (currentState == nextState)
        {
            return true;
        }

        return AllowedTransitions.TryGetValue(currentState, out var allowed) &&
               allowed.Contains(nextState);
    }

    public bool TryRecoverToFinalize(ChatTurnContext context, out string recoveryReason)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.LifecycleState == TurnLifecycleState.PostAudit)
        {
            recoveryReason = "cannot_recover_from_post_audit";
            return false;
        }

        if (context.LifecycleState == TurnLifecycleState.Finalize)
        {
            recoveryReason = "already_in_finalize";
            return true;
        }

        if (!RecoveryToFinalizeAllowed.Contains(context.LifecycleState))
        {
            recoveryReason = $"recovery_not_allowed_from_{context.LifecycleState.ToString().ToLowerInvariant()}";
            return false;
        }

        var from = context.LifecycleState;
        context.LifecycleState = TurnLifecycleState.Finalize;
        context.LifecycleTrace.Add(TurnLifecycleState.Finalize);
        recoveryReason = $"recovered_from_{from.ToString().ToLowerInvariant()}";
        return true;
    }
}

