using Helper.Api.Conversation;
using Helper.Api.Hosting;
using Helper.Runtime.Core;

namespace Helper.Api.Backend.Application;

public interface ITurnResponseWriter
{
    ChatResponseDto PersistCompletedTurn(ConversationState state, ChatTurnContext context, string branchId, int turnVersion);
    ChatResponseDto BuildBlockedResponse(ConversationState state, ChatRequestDto request, InputRiskScanResult risk);
}

public sealed class TurnResponseWriter : ITurnResponseWriter
{
    private readonly IConversationStore _store;
    private readonly ITurnCheckpointManager _checkpointManager;
    private readonly IPostTurnAuditScheduler _auditScheduler;
    private readonly ITurnLifecycleStateMachine _legacyLifecycle;
    private readonly ITurnExecutionStateMachine _executionStateMachine;
    private readonly ITurnRouteTelemetryRecorder _telemetryRecorder;
    private readonly IConversationStyleTelemetryAnalyzer _styleTelemetryAnalyzer;
    private readonly IWebSearchTraceProjector _webSearchTraceProjector;

    public TurnResponseWriter(
        IConversationStore store,
        ITurnCheckpointManager checkpointManager,
        IPostTurnAuditScheduler auditScheduler,
        ITurnLifecycleStateMachine legacyLifecycle,
        ITurnExecutionStateMachine executionStateMachine,
        ITurnRouteTelemetryRecorder telemetryRecorder,
        IConversationStyleTelemetryAnalyzer? styleTelemetryAnalyzer = null)
        : this(
            store,
            checkpointManager,
            auditScheduler,
            legacyLifecycle,
            executionStateMachine,
            telemetryRecorder,
            styleTelemetryAnalyzer,
            new WebSearchTraceProjector())
    {
    }

    internal TurnResponseWriter(
        IConversationStore store,
        ITurnCheckpointManager checkpointManager,
        IPostTurnAuditScheduler auditScheduler,
        ITurnLifecycleStateMachine legacyLifecycle,
        ITurnExecutionStateMachine executionStateMachine,
        ITurnRouteTelemetryRecorder telemetryRecorder,
        IConversationStyleTelemetryAnalyzer? styleTelemetryAnalyzer,
        IWebSearchTraceProjector webSearchTraceProjector)
    {
        _store = store;
        _checkpointManager = checkpointManager;
        _auditScheduler = auditScheduler;
        _legacyLifecycle = legacyLifecycle;
        _executionStateMachine = executionStateMachine;
        _telemetryRecorder = telemetryRecorder;
        _styleTelemetryAnalyzer = styleTelemetryAnalyzer ?? new ConversationStyleTelemetryAnalyzer(new SourceNormalizationService());
        _webSearchTraceProjector = webSearchTraceProjector;
    }

    public ChatResponseDto PersistCompletedTurn(
        ConversationState state,
        ChatTurnContext context,
        string branchId,
        int turnVersion)
    {
        _legacyLifecycle.Transition(context, TurnLifecycleState.PostAudit);
        _executionStateMachine.Transition(context, TurnExecutionState.Persisted);
        UpdateClarificationState(state, context.RequiresClarification);
        _store.AddMessage(state, new ChatMessageDto(
            "assistant",
            context.FinalResponse,
            DateTimeOffset.UtcNow,
            context.TurnId,
            turnVersion,
            branchId,
            context.ToolCalls.ToList(),
            context.Sources.ToList(),
            context.Request.Attachments,
            ConversationInputMode.Normalize(context.Request.InputMode)));
        _checkpointManager.Clear(state);
        var reasoningMetrics = BuildReasoningMetrics(context);
        var styleTelemetry = _styleTelemetryAnalyzer.Analyze(context);
        var searchTrace = _webSearchTraceProjector.Build(context);
        context.StyleTelemetry = styleTelemetry;
        var response = new ChatResponseDto(
            state.Id,
            context.FinalResponse,
            _store.GetRecentMessages(state, branchId, context.Request.MaxHistory ?? 20),
            DateTimeOffset.UtcNow,
            context.Confidence,
            context.Sources.ToList(),
            context.TurnId,
            context.ToolCalls.ToList(),
            context.RequiresConfirmation,
            context.NextStep,
            context.GroundingStatus,
            context.CitationCoverage,
            context.VerifiedClaims,
            context.TotalClaims,
            context.UncertaintyFlags.ToList(),
            branchId,
            _store.GetBranchIds(state),
            context.ClaimGroundings.ToList(),
            context.ExecutionMode.ToString().ToLowerInvariant(),
            TurnBudgetProfileFormatter.Format(context.BudgetProfile),
            context.BudgetExceeded,
            context.EstimatedTokensGenerated,
            context.Intent.Intent.ToString().ToLowerInvariant(),
            context.IntentConfidence,
            ExecutionTrace: context.ExecutionTrace.Select(x => x.ToString()).ToArray(),
            LifecycleTrace: context.LifecycleTrace.Select(x => x.ToString()).ToArray(),
            ReasoningMetrics: reasoningMetrics,
            StyleTelemetry: ToDto(styleTelemetry),
            SearchTrace: searchTrace,
            InputMode: ConversationInputMode.Normalize(context.Request.InputMode));

        if (_auditScheduler.TrySchedule(context, response))
        {
            _executionStateMachine.Transition(context, TurnExecutionState.AuditedAsync);
        }

        _executionStateMachine.Transition(context, TurnExecutionState.Completed);
        _telemetryRecorder.RecordCompletedTurn(context);
        var auditStatus = BuildAuditStatus(context);
        return response with
        {
            ExecutionTrace = context.ExecutionTrace.Select(x => x.ToString()).ToArray(),
            LifecycleTrace = context.LifecycleTrace.Select(x => x.ToString()).ToArray(),
            ReasoningMetrics = reasoningMetrics,
            AuditStatus = auditStatus,
            StyleTelemetry = ToDto(styleTelemetry),
            SearchTrace = searchTrace
        };
    }

    public ChatResponseDto BuildBlockedResponse(ConversationState state, ChatRequestDto request, InputRiskScanResult risk)
    {
        var blockedText = $"Request blocked by safety policy. {risk.Reason}";
        var turnId = Guid.NewGuid().ToString("N");
        var branchId = _store.GetActiveBranchId(state);
        _store.AddMessage(state, new ChatMessageDto(
            "assistant",
            blockedText,
            DateTimeOffset.UtcNow,
            turnId,
            TurnVersion: 1,
            BranchId: branchId,
            ToolCalls: Array.Empty<string>(),
            Citations: Array.Empty<string>(),
            Attachments: null,
            InputMode: ConversationInputMode.Normalize(request.InputMode)));
        _telemetryRecorder.RecordBlockedTurn(state, risk);

        return new ChatResponseDto(
            state.Id,
            blockedText,
            _store.GetRecentMessages(state, branchId, request.MaxHistory ?? 20),
            DateTimeOffset.UtcNow,
            Confidence: 0.1,
            Sources: Array.Empty<string>(),
            TurnId: turnId,
            ToolCalls: Array.Empty<string>(),
            RequiresConfirmation: true,
            NextStep: "Rephrase your request without policy-violating instructions.",
            GroundingStatus: "blocked",
            CitationCoverage: 0,
            VerifiedClaims: 0,
            TotalClaims: 0,
            UncertaintyFlags: risk.Flags,
            BranchId: branchId,
            AvailableBranches: _store.GetBranchIds(state),
            Intent: IntentType.Unknown.ToString().ToLowerInvariant(),
            IntentConfidence: 0.1,
            InputMode: ConversationInputMode.Normalize(request.InputMode));
    }

    private static ReasoningEfficiencyMetricsDto? BuildReasoningMetrics(ChatTurnContext context)
    {
        var pathActive = context.ReasoningBranchingApplied || context.LocalVerificationAppliedCount > 0;
        if (!pathActive)
        {
            return null;
        }

        var modelCallsUsed = context.ReasoningModelCallsUsed > 0
            ? context.ReasoningModelCallsUsed
            : 1;
        var approximateTokenCost = context.ApproximateReasoningTokenCost > 0
            ? context.ApproximateReasoningTokenCost
            : Math.Max(0, context.EstimatedTokensGenerated);

        return new ReasoningEfficiencyMetricsDto(
            PathActive: true,
            BranchingApplied: context.ReasoningBranchingApplied,
            BranchesExplored: Math.Max(0, context.ReasoningCandidatesGenerated),
            CandidatesRejected: Math.Max(0, context.ReasoningCandidatesRejected),
            LocalVerificationChecks: Math.Max(0, context.LocalVerificationAppliedCount),
            LocalVerificationPasses: Math.Max(0, context.LocalVerificationPassCount),
            LocalVerificationRejects: Math.Max(0, context.LocalVerificationRejectCount),
            ModelCallsUsed: modelCallsUsed,
            RetrievalChunksUsed: Math.Max(0, context.RetrievalChunksUsed),
            ProceduralLessonsUsed: Math.Max(0, context.ProceduralLessonsUsed),
            ApproximateTokenCost: approximateTokenCost,
            SelectedStrategy: context.SelectedReasoningStrategy);
    }

    private static PostTurnAuditStatusDto? BuildAuditStatus(ChatTurnContext context)
    {
        if (string.Equals(context.AuditDecision, "not_evaluated", StringComparison.OrdinalIgnoreCase) &&
            !context.AuditEligible &&
            !context.AuditExpectedTrace &&
            !context.AuditStrictMode &&
            context.AuditOutstandingAtDecision == 0 &&
            context.AuditPendingAtDecision == 0 &&
            context.AuditMaxOutstandingAudits == 1)
        {
            return null;
        }

        return new PostTurnAuditStatusDto(
            Eligible: context.AuditEligible,
            ExpectedTrace: context.AuditExpectedTrace,
            StrictMode: context.AuditStrictMode,
            Decision: context.AuditDecision,
            OutstandingAtDecision: context.AuditOutstandingAtDecision,
            PendingAtDecision: context.AuditPendingAtDecision,
            MaxOutstandingAudits: context.AuditMaxOutstandingAudits);
    }

    private static ConversationStyleTelemetryDto ToDto(ConversationStyleTelemetry telemetry)
    {
        return new ConversationStyleTelemetryDto(
            telemetry.LeadPhraseFingerprint,
            telemetry.MixedLanguageDetected,
            telemetry.GenericClarificationDetected,
            telemetry.GenericNextStepDetected,
            telemetry.MemoryAckTemplateDetected,
            telemetry.SourceFingerprint);
    }

    private static void UpdateClarificationState(ConversationState state, bool clarificationIssued)
    {
        lock (state.SyncRoot)
        {
            if (clarificationIssued)
            {
                state.ConsecutiveClarificationTurns = Math.Min(state.ConsecutiveClarificationTurns + 1, 1000);
                state.LastClarificationAt = DateTimeOffset.UtcNow;
                return;
            }

            state.ConsecutiveClarificationTurns = 0;
        }
    }
}

