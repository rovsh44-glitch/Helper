namespace Helper.Api.Conversation;

public sealed class TurnLatencyBudgetStep
{
    private readonly ILatencyBudgetPolicy _latencyBudgetPolicy;

    public TurnLatencyBudgetStep(ILatencyBudgetPolicy latencyBudgetPolicy)
    {
        _latencyBudgetPolicy = latencyBudgetPolicy;
    }

    public Task ExecuteAsync(TurnPlanningContext planningContext, TurnPlanningState planningState, CancellationToken ct)
    {
        _ = planningState;
        _ = ct;
        var turn = planningContext.Turn;
        var budget = _latencyBudgetPolicy.Resolve(turn);
        turn.ExecutionMode = budget.Mode;
        turn.BudgetProfile = budget.Profile;
        turn.TimeBudget = budget.TimeBudget;
        turn.ToolCallBudget = budget.ToolCallBudget;
        turn.TokenBudget = budget.TokenBudget;
        turn.ModelCallBudget = budget.ModelCallBudget;
        turn.BackgroundBudget = budget.BackgroundBudget;
        turn.BudgetReason = budget.Reason;
        return Task.CompletedTask;
    }
}
