namespace Helper.Api.Conversation;

public sealed class TurnReasoningSelectionStep
{
    private readonly IReasoningEffortPolicy _reasoningEffortPolicy;

    public TurnReasoningSelectionStep(IReasoningEffortPolicy reasoningEffortPolicy)
    {
        _reasoningEffortPolicy = reasoningEffortPolicy;
    }

    public Task ExecuteAsync(TurnPlanningContext planningContext, TurnPlanningState planningState, CancellationToken ct)
    {
        _ = ct;
        planningContext.Turn.ReasoningEffort = _reasoningEffortPolicy.Resolve(planningContext.Turn, planningState.Personalization);
        return Task.CompletedTask;
    }
}
