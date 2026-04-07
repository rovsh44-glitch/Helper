using Helper.Api.Conversation.Epistemic;
using Helper.Api.Conversation.InteractionState;
using Helper.Api.Hosting;
using Helper.Runtime.Core;

namespace Helper.Api.Conversation;

public sealed class ChatTurnContext
{
    public TurnLifecycleState LifecycleState { get; set; } = TurnLifecycleState.New;
    public List<TurnLifecycleState> LifecycleTrace { get; } = new() { TurnLifecycleState.New };
    public TurnExecutionState ExecutionState { get; set; } = TurnExecutionState.Received;
    public List<TurnExecutionState> ExecutionTrace { get; } = new() { TurnExecutionState.Received };
    public required string TurnId { get; init; }
    public required ChatRequestDto Request { get; init; }
    public required ConversationState Conversation { get; init; }
    public required IReadOnlyList<ChatMessageDto> History { get; set; }
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;
    public int ToolCallBudget { get; set; } = 3;
    public int ModelCallBudget { get; set; } = 2;
    public int BackgroundBudget { get; set; } = 1;
    public TimeSpan TimeBudget { get; set; } = TimeSpan.FromSeconds(20);
    public int TokenBudget { get; set; } = 1400;
    public TurnExecutionMode ExecutionMode { get; set; } = TurnExecutionMode.Balanced;
    public TurnBudgetProfile BudgetProfile { get; set; } = TurnBudgetProfile.ChatLight;
    public string BudgetReason { get; set; } = "Default budget policy.";

    public IntentAnalysis Intent { get; set; } = new(IntentType.Unknown, string.Empty);
    public double IntentConfidence { get; set; }
    public string IntentSource { get; set; } = "unknown";
    public List<string> IntentSignals { get; } = new();
    public CollaborationIntentAnalysis CollaborationIntent { get; set; } = CollaborationIntentAnalysis.None;
    public string AmbiguityType { get; set; } = nameof(global::Helper.Api.Conversation.AmbiguityType.None);
    public double AmbiguityConfidence { get; set; }
    public string? AmbiguityReason { get; set; }
    public string? ClarificationBoundary { get; set; }
    public bool RequiresClarification { get; set; }
    public bool RequiresConfirmation { get; set; }
    public string? ClarifyingQuestion { get; set; }
    public bool ForceBestEffort { get; set; }
    public string? ForceBestEffortReason { get; set; }
    public string ExecutionOutput { get; set; } = string.Empty;
    public string FinalResponse { get; set; } = string.Empty;
    public string? CritiqueFeedback { get; set; }
    public string? CorrectedContent { get; set; }
    public bool IsCritiqueApproved { get; set; } = true;
    public bool BudgetExceeded { get; set; }
    public int EstimatedTokensGenerated { get; set; }
    public int ToolCallsUsed { get; set; }
    public double Confidence { get; set; } = 0.5;
    public bool IsFactualPrompt { get; set; }
    public string? NextStep { get; set; }
    public string? ModelRouteKey { get; set; }
    public string? ModelRouteReason { get; set; }
    public List<string> ModelRouteSignals { get; } = new();
    public List<string> RequestedMemoryLayers { get; } = new();
    public List<string> UsedMemoryLayers { get; } = new();
    public int MemoryHistoryBudget { get; set; }
    public int ProceduralLessonBudget { get; set; }
    public int RetrievalChunkBudget { get; set; }
    public int ProceduralLessonsUsed { get; set; }
    public int RetrievalChunksUsed { get; set; }
    public string? SelectedRetrievalPurpose { get; set; }
    public List<string> RetrievalTrace { get; } = new();
    public string? ResolvedTurnLanguage { get; set; }
    public string ResolvedLiveWebRequirement { get; set; } = "no_web_needed";
    public string? ResolvedLiveWebReason { get; set; }
    public List<string> LiveWebSignals { get; } = new();
    public bool IsLocalFirstBenchmarkTurn { get; set; }
    public LocalFirstBenchmarkMode LocalFirstBenchmarkMode { get; set; } = LocalFirstBenchmarkMode.None;
    public bool RequireExplicitBenchmarkUncertainty { get; set; }
    public string? ResolvedStyleMode { get; set; }
    public string? ResolvedTonePreset { get; set; }
    public EpistemicRiskSnapshot? EpistemicRiskSnapshot { get; set; }
    public EpistemicAnswerMode EpistemicAnswerMode { get; set; } = EpistemicAnswerMode.Direct;
    public InteractionStateSnapshot? InteractionState { get; set; }
    public InteractionPolicyProjection? InteractionPolicy { get; set; }
    public string? ReasoningEffort { get; set; }
    public string? DecisionExplanation { get; set; }
    public string? RepairClass { get; set; }
    public string? RepairDriver { get; set; }
    public string? ActiveProjectId { get; set; }
    public bool AuditEligible { get; set; }
    public bool AuditExpectedTrace { get; set; }
    public bool AuditStrictMode { get; set; }
    public string AuditDecision { get; set; } = "not_evaluated";
    public int AuditOutstandingAtDecision { get; set; }
    public int AuditPendingAtDecision { get; set; }
    public int AuditMaxOutstandingAudits { get; set; } = 1;
    public bool LocalVerificationApplied { get; set; }
    public bool LocalVerificationPassed { get; set; }
    public string? LocalVerificationSummary { get; set; }
    public List<string> LocalVerificationTrace { get; } = new();
    public int LocalVerificationAppliedCount { get; set; }
    public int LocalVerificationPassCount { get; set; }
    public int LocalVerificationRejectCount { get; set; }
    public bool ReasoningBranchingApplied { get; set; }
    public int ReasoningCandidatesGenerated { get; set; }
    public int ReasoningCandidatesRejected { get; set; }
    public int ReasoningModelCallsUsed { get; set; }
    public int ApproximateReasoningTokenCost { get; set; }
    public string? SelectedReasoningStrategy { get; set; }
    public List<string> ReasoningCandidateTrace { get; } = new();
    public string? GroundingStatus { get; set; } = "unknown";
    public double CitationCoverage { get; set; }
    public int VerifiedClaims { get; set; }
    public int TotalClaims { get; set; }
    public List<ClaimGrounding> ClaimGroundings { get; } = new();
    public List<ResearchEvidenceItem> ResearchEvidenceItems { get; } = new();
    public ConversationStyleTelemetry? StyleTelemetry { get; set; }
    public CommunicationQualitySnapshot? CommunicationQualitySnapshot { get; set; }
    public List<string> UncertaintyFlags { get; } = new();
    public List<string> ToolCalls { get; } = new();
    public List<string> Sources { get; } = new();
}

public enum TurnLifecycleState
{
    New,
    Understand,
    Clarify,
    Execute,
    Verify,
    Finalize,
    PostAudit
}

public enum TurnExecutionState
{
    Received,
    Validated,
    Planned,
    Executed,
    ValidatedByPolicy,
    Finalized,
    Persisted,
    AuditedAsync,
    Completed,
    Failed
}

