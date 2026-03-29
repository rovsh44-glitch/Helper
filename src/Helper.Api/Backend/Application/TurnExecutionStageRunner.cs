using Helper.Api.Conversation;
using Helper.Api.Hosting;
using Helper.Runtime.Core;

namespace Helper.Api.Backend.Application;

public interface ITurnExecutionStageRunner
{
    Task PlanAsync(ConversationState state, ChatTurnContext context, string branchId, CancellationToken ct);
    Task ExecuteAsync(ConversationState state, ChatTurnContext context, string branchId, CancellationToken ct);
    IAsyncEnumerable<TokenChunk> ExecuteStreamAsync(ConversationState state, ChatTurnContext context, string branchId, CancellationToken ct);
    Task ValidateByPolicyAsync(ChatTurnContext context, CancellationToken ct);
    Task FinalizeAsync(ChatTurnContext context, CancellationToken ct);
    Task ApplyOutputSafetyAsync(ChatTurnContext context, CancellationToken ct);
    void RecoverFromTurnFailure(ChatTurnContext context, Exception ex);
}

public sealed class TurnExecutionStageRunner : ITurnExecutionStageRunner
{
    private readonly IConversationStore _store;
    private readonly IChatTurnPlanner _planner;
    private readonly IChatTurnExecutor _executor;
    private readonly IChatTurnCritic _critic;
    private readonly IChatTurnFinalizer _finalizer;
    private readonly IOutputExfiltrationGuard _outputGuard;
    private readonly ITurnLifecycleStateMachine _legacyLifecycle;
    private readonly ITurnExecutionStateMachine _executionStateMachine;
    private readonly ITurnCheckpointManager _checkpointManager;
    private readonly ITurnStagePolicy _stagePolicy;
    private readonly IConversationStageMetricsService? _stageMetrics;
    private readonly ILogger<TurnExecutionStageRunner> _logger;

    public TurnExecutionStageRunner(
        IConversationStore store,
        IChatTurnPlanner planner,
        IChatTurnExecutor executor,
        IChatTurnCritic critic,
        IChatTurnFinalizer finalizer,
        IOutputExfiltrationGuard outputGuard,
        ITurnLifecycleStateMachine legacyLifecycle,
        ITurnExecutionStateMachine executionStateMachine,
        ITurnCheckpointManager checkpointManager,
        ITurnStagePolicy stagePolicy,
        IConversationStageMetricsService? stageMetrics = null,
        ILogger<TurnExecutionStageRunner>? logger = null)
    {
        _store = store;
        _planner = planner;
        _executor = executor;
        _critic = critic;
        _finalizer = finalizer;
        _outputGuard = outputGuard;
        _legacyLifecycle = legacyLifecycle;
        _executionStateMachine = executionStateMachine;
        _checkpointManager = checkpointManager;
        _stagePolicy = stagePolicy;
        _stageMetrics = stageMetrics;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<TurnExecutionStageRunner>.Instance;
    }

    public async Task PlanAsync(ConversationState state, ChatTurnContext context, string branchId, CancellationToken ct)
    {
        context.History = _store.GetRecentMessages(state, branchId, context.Request.MaxHistory ?? 12);
        _legacyLifecycle.Transition(context, TurnLifecycleState.Understand);
        var timer = System.Diagnostics.Stopwatch.StartNew();
        await _planner.PlanAsync(context, ct);
        _executionStateMachine.Transition(context, TurnExecutionState.Planned);
        _checkpointManager.SavePlannedCheckpoint(state, context, branchId);
        _store.MarkUpdated(state);
        _stageMetrics?.Record("plan", timer.ElapsedMilliseconds, success: true);
    }

    public async Task ExecuteAsync(ConversationState state, ChatTurnContext context, string branchId, CancellationToken ct)
    {
        _legacyLifecycle.Transition(context, context.RequiresClarification ? TurnLifecycleState.Clarify : TurnLifecycleState.Execute);
        var timer = System.Diagnostics.Stopwatch.StartNew();
        await _executor.ExecuteAsync(context, ct);
        _executionStateMachine.Transition(context, TurnExecutionState.Executed);
        _checkpointManager.SaveExecutionProgress(
            state,
            context,
            branchId,
            context.ExecutionOutput,
            TokenBudgetEstimator.Estimate(context.ExecutionOutput));
        _store.MarkUpdated(state);
        _stageMetrics?.Record("execute", timer.ElapsedMilliseconds, success: true);
    }

    public async IAsyncEnumerable<TokenChunk> ExecuteStreamAsync(
        ConversationState state,
        ChatTurnContext context,
        string branchId,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        _legacyLifecycle.Transition(context, TurnLifecycleState.Execute);
        var offset = 0;
        var materializedImmediateOutput =
            context.ExecutionState == TurnExecutionState.Planned &&
            !string.IsNullOrWhiteSpace(context.ExecutionOutput);
        var timer = System.Diagnostics.Stopwatch.StartNew();
        await foreach (var modelChunk in _executor.ExecuteStreamAsync(context, ct).WithCancellation(ct))
        {
            if (modelChunk.Type != ChatStreamChunkType.Token || string.IsNullOrEmpty(modelChunk.Content))
            {
                continue;
            }

            if (materializedImmediateOutput)
            {
                offset = Math.Max(offset, context.ExecutionOutput.Length);
                context.EstimatedTokensGenerated = TokenBudgetEstimator.Estimate(context.ExecutionOutput);
            }
            else
            {
                offset = modelChunk.Offset > offset ? modelChunk.Offset : offset + modelChunk.Content.Length;
                context.ExecutionOutput += modelChunk.Content;
                context.EstimatedTokensGenerated = TokenBudgetEstimator.Estimate(context.ExecutionOutput);
            }

            if (context.ExecutionState == TurnExecutionState.Planned)
            {
                _executionStateMachine.Transition(context, TurnExecutionState.Executed);
            }

            _checkpointManager.SaveExecutionProgress(state, context, branchId, context.ExecutionOutput, offset);
            _store.MarkUpdated(state);

            yield return new TokenChunk(
                ChatStreamChunkType.Token,
                modelChunk.Content,
                offset,
                modelChunk.TimestampUtc,
                ConversationId: state.Id,
                TurnId: context.TurnId,
                ModelStreamStartedAtUtc: modelChunk.ModelStreamStartedAtUtc,
                ResumeCursor: offset);
        }

        _stageMetrics?.Record("execute", timer.ElapsedMilliseconds, success: true);
    }

    public async Task ValidateByPolicyAsync(ChatTurnContext context, CancellationToken ct)
    {
        if (!context.RequiresClarification && _stagePolicy.RequiresSynchronousCritic(context))
        {
            _legacyLifecycle.Transition(context, TurnLifecycleState.Verify);
            var timer = System.Diagnostics.Stopwatch.StartNew();
            await _critic.CritiqueAsync(context, ct);
            _stageMetrics?.Record("critic", timer.ElapsedMilliseconds, success: true);
        }
        else
        {
            context.IsCritiqueApproved = true;
            context.CorrectedContent = context.ExecutionOutput;
            context.CritiqueFeedback ??= "Synchronous critic skipped by stage policy.";
        }

        _executionStateMachine.Transition(context, TurnExecutionState.ValidatedByPolicy);
    }

    public async Task FinalizeAsync(ChatTurnContext context, CancellationToken ct)
    {
        TurnLifecycleProgression.EnsureReadyToFinalize(context, _legacyLifecycle);
        _legacyLifecycle.Transition(context, TurnLifecycleState.Finalize);
        var timer = System.Diagnostics.Stopwatch.StartNew();
        await _finalizer.FinalizeAsync(context, ct);
        _executionStateMachine.Transition(context, TurnExecutionState.Finalized);
        _stageMetrics?.Record("finalizer", timer.ElapsedMilliseconds, success: true);
    }

    public async Task ApplyOutputSafetyAsync(ChatTurnContext context, CancellationToken ct)
    {
        var outputSafety = await _outputGuard.ScanAsync(context.FinalResponse, ct);
        if (!outputSafety.IsBlocked)
        {
            return;
        }

        context.FinalResponse = outputSafety.SanitizedOutput;
        context.UncertaintyFlags.AddRange(outputSafety.Flags);
        context.Confidence = Math.Min(context.Confidence, 0.3);
        context.GroundingStatus = "blocked";
    }

    public void RecoverFromTurnFailure(ChatTurnContext context, Exception ex)
    {
        _logger.LogWarning(
            ex,
            "Recovered failed turn. ConversationId={ConversationId} TurnId={TurnId} ExecutionState={ExecutionState} LifecycleState={LifecycleState}",
            context.Conversation.Id,
            context.TurnId,
            context.ExecutionState,
            context.LifecycleState);

        var legacyRecovered = _legacyLifecycle.TryRecoverToFinalize(context, out var legacyRecoveryReason);
        var executionRecovered = _executionStateMachine.TryRecoverToFinalize(context, out var executionRecoveryReason);
        if (!legacyRecovered || !executionRecovered)
        {
            throw new InvalidOperationException(
                $"Turn recovery failed for turn {context.TurnId} at lifecycle state {context.LifecycleState} and execution state {context.ExecutionState}.",
                ex);
        }

        if (!string.IsNullOrWhiteSpace(legacyRecoveryReason))
        {
            context.UncertaintyFlags.Add(legacyRecoveryReason);
        }

        if (!string.IsNullOrWhiteSpace(executionRecoveryReason))
        {
            context.UncertaintyFlags.Add(executionRecoveryReason);
        }

        context.UncertaintyFlags.Add("turn_pipeline_recovered");
        context.IsCritiqueApproved = true;
        context.Confidence = Math.Min(context.Confidence, 0.25);
        context.GroundingStatus = "degraded";
        context.CitationCoverage = 0;
        context.NextStep = "Retry the request or provide constraints to continue from this recovered turn.";

        if (string.IsNullOrWhiteSpace(context.ExecutionOutput))
        {
            context.ExecutionOutput =
                "I hit an internal processing error and switched to recovery mode. I can continue if you clarify your request.";
        }

        context.FinalResponse = context.ExecutionOutput;
    }
}

