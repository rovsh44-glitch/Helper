using Helper.Api.Conversation;
using Helper.Api.Conversation.Epistemic;
using Helper.Api.Conversation.InteractionState;
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
    private readonly ISharedUnderstandingService _sharedUnderstandingService;
    private readonly ICommunicationQualityPolicy _communicationQualityPolicy;
    private readonly IFollowThroughScheduler _followThroughScheduler;

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
            new WebSearchTraceProjector(),
            new SharedUnderstandingService(),
            new CommunicationQualityPolicy(),
            new FollowThroughScheduler())
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
        IWebSearchTraceProjector webSearchTraceProjector,
        ISharedUnderstandingService? sharedUnderstandingService,
        ICommunicationQualityPolicy? communicationQualityPolicy,
        IFollowThroughScheduler? followThroughScheduler)
    {
        _store = store;
        _checkpointManager = checkpointManager;
        _auditScheduler = auditScheduler;
        _legacyLifecycle = legacyLifecycle;
        _executionStateMachine = executionStateMachine;
        _telemetryRecorder = telemetryRecorder;
        _styleTelemetryAnalyzer = styleTelemetryAnalyzer ?? new ConversationStyleTelemetryAnalyzer(new SourceNormalizationService());
        _webSearchTraceProjector = webSearchTraceProjector;
        _sharedUnderstandingService = sharedUnderstandingService ?? new SharedUnderstandingService();
        _communicationQualityPolicy = communicationQualityPolicy ?? new CommunicationQualityPolicy();
        _followThroughScheduler = followThroughScheduler ?? new FollowThroughScheduler();
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
        var answerMode = ToApiValue(context.EpistemicAnswerMode);
        var epistemicRisk = ToDto(context.EpistemicRiskSnapshot, answerMode);
        var interactionState = ToDto(context.InteractionState);
        context.StyleTelemetry = styleTelemetry;
        _sharedUnderstandingService.CaptureTurnOutcome(state, context, DateTimeOffset.UtcNow);
        _communicationQualityPolicy.RecordCompletedTurn(state, context, styleTelemetry, DateTimeOffset.UtcNow);
        _followThroughScheduler.QueueResearchFollowThrough(state, context, DateTimeOffset.UtcNow);
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
            ReasoningEffort: context.ReasoningEffort,
            DecisionExplanation: context.DecisionExplanation,
            RepairClass: context.RepairClass,
            RepairDriver: context.RepairDriver,
            StyleTelemetry: ToDto(styleTelemetry),
            SearchTrace: searchTrace,
            InputMode: ConversationInputMode.Normalize(context.Request.InputMode),
            EpistemicAnswerMode: answerMode,
            EpistemicRisk: epistemicRisk,
            InteractionState: interactionState);

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
            SearchTrace = searchTrace,
            EpistemicAnswerMode = answerMode,
            EpistemicRisk = epistemicRisk,
            InteractionState = interactionState
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
            ReasoningEffort: null,
            DecisionExplanation: "blocked_by_safety_policy",
            RepairClass: null,
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

    private static EpistemicRiskSnapshotDto? ToDto(EpistemicRiskSnapshot? snapshot, string answerMode)
    {
        if (snapshot is null)
        {
            return null;
        }

        return new EpistemicRiskSnapshotDto(
            AnswerMode: answerMode,
            GroundingStatus: snapshot.GroundingStatus,
            CitationCoverage: snapshot.CitationCoverage,
            VerifiedClaimRatio: snapshot.VerifiedClaimRatio,
            HasContradictions: snapshot.HasContradictions,
            HasWeakEvidence: snapshot.HasWeakEvidence,
            HighRiskDomain: snapshot.HighRiskDomain,
            FreshnessSensitive: snapshot.FreshnessSensitive,
            ConfidenceCeiling: snapshot.ConfidenceCeiling,
            CalibrationThreshold: snapshot.CalibrationThreshold,
            AbstentionRecommended: snapshot.AbstentionRecommended,
            Trace: snapshot.Trace);
    }

    private static InteractionStateSnapshotDto? ToDto(InteractionStateSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return null;
        }

        return new InteractionStateSnapshotDto(
            FrustrationLevel: ToApiValue(snapshot.FrustrationLevel),
            UrgencyLevel: ToApiValue(snapshot.UrgencyLevel),
            OverloadRisk: ToApiValue(snapshot.OverloadRisk),
            ReassuranceNeed: ToApiValue(snapshot.ReassuranceNeed),
            ClarificationToleranceShift: snapshot.ClarificationToleranceShift,
            AssistantPressureRisk: ToApiValue(snapshot.AssistantPressureRisk),
            Signals: snapshot.Signals);
    }

    private static string ToApiValue(EpistemicAnswerMode answerMode)
    {
        return answerMode switch
        {
            EpistemicAnswerMode.BestEffortHypothesis => "best_effort_hypothesis",
            EpistemicAnswerMode.NeedsVerification => "needs_verification",
            _ => answerMode.ToString().ToLowerInvariant()
        };
    }

    private static string ToApiValue(InteractionSignalLevel level)
    {
        return level.ToString().ToLowerInvariant();
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

