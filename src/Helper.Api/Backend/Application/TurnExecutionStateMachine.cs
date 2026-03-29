using Helper.Api.Conversation;

namespace Helper.Api.Backend.Application;

public interface ITurnExecutionStateMachine
{
    void Transition(ChatTurnContext context, TurnExecutionState nextState);
    bool CanTransition(TurnExecutionState currentState, TurnExecutionState nextState);
    bool TryRecoverToFinalize(ChatTurnContext context, out string recoveryReason);
}

public sealed class TurnExecutionStateMachine : ITurnExecutionStateMachine
{
    private static readonly IReadOnlyDictionary<TurnExecutionState, TurnExecutionState[]> AllowedTransitions =
        new Dictionary<TurnExecutionState, TurnExecutionState[]>
        {
            [TurnExecutionState.Received] = new[] { TurnExecutionState.Validated, TurnExecutionState.Failed },
            [TurnExecutionState.Validated] = new[] { TurnExecutionState.Planned, TurnExecutionState.Failed },
            [TurnExecutionState.Planned] = new[] { TurnExecutionState.Executed, TurnExecutionState.Failed },
            [TurnExecutionState.Executed] = new[] { TurnExecutionState.ValidatedByPolicy, TurnExecutionState.Finalized, TurnExecutionState.Failed },
            [TurnExecutionState.ValidatedByPolicy] = new[] { TurnExecutionState.Finalized, TurnExecutionState.Failed },
            [TurnExecutionState.Finalized] = new[] { TurnExecutionState.Persisted, TurnExecutionState.Failed },
            [TurnExecutionState.Persisted] = new[] { TurnExecutionState.AuditedAsync, TurnExecutionState.Completed, TurnExecutionState.Failed },
            [TurnExecutionState.AuditedAsync] = new[] { TurnExecutionState.Completed, TurnExecutionState.Failed },
            [TurnExecutionState.Completed] = Array.Empty<TurnExecutionState>(),
            [TurnExecutionState.Failed] = Array.Empty<TurnExecutionState>()
        };

    private static readonly HashSet<TurnExecutionState> RecoverableStates = new()
    {
        TurnExecutionState.Received,
        TurnExecutionState.Validated,
        TurnExecutionState.Planned,
        TurnExecutionState.Executed,
        TurnExecutionState.ValidatedByPolicy
    };

    public void Transition(ChatTurnContext context, TurnExecutionState nextState)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.ExecutionState == nextState)
        {
            return;
        }

        if (!CanTransition(context.ExecutionState, nextState))
        {
            throw new InvalidOperationException(
                $"Illegal execution transition: {context.ExecutionState} -> {nextState} (turn={context.TurnId}).");
        }

        context.ExecutionState = nextState;
        context.ExecutionTrace.Add(nextState);
    }

    public bool CanTransition(TurnExecutionState currentState, TurnExecutionState nextState)
    {
        if (currentState == nextState)
        {
            return true;
        }

        return AllowedTransitions.TryGetValue(currentState, out var allowed) && allowed.Contains(nextState);
    }

    public bool TryRecoverToFinalize(ChatTurnContext context, out string recoveryReason)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.ExecutionState == TurnExecutionState.Finalized)
        {
            recoveryReason = "already_finalized";
            return true;
        }

        if (!RecoverableStates.Contains(context.ExecutionState))
        {
            recoveryReason = $"execution_recovery_not_allowed_from_{context.ExecutionState.ToString().ToLowerInvariant()}";
            return false;
        }

        var previous = context.ExecutionState;
        context.ExecutionState = TurnExecutionState.Finalized;
        context.ExecutionTrace.Add(TurnExecutionState.Finalized);
        recoveryReason = $"recovered_from_{previous.ToString().ToLowerInvariant()}";
        return true;
    }
}

