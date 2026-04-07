using Helper.Api.Conversation;
using Microsoft.AspNetCore.SignalR;

namespace Helper.Api.Hosting;

public sealed record ApiRuntimeConfig(
    string RootPath,
    string DataRoot,
    string ProjectsRoot,
    string LibraryRoot,
    string LogsRoot,
    string TemplatesRoot,
    string ApiKey)
{
    public string IndexingQueuePath => Path.Combine(DataRoot, "indexing_queue.json");

    public ApiRuntimeConfig(
        string RootPath,
        string ProjectsRoot,
        string LibraryRoot,
        string LogsRoot,
        string TemplatesRoot,
        string ApiKey)
        : this(RootPath, RootPath, ProjectsRoot, LibraryRoot, LogsRoot, TemplatesRoot, ApiKey)
    {
    }
}

public record ChallengeRequest(string Proposal);
public record ResearchRequest(string Topic);
public record GenerationRequestDto(string Prompt, string? OutputPath);
public record StrategicPlanRequestDto(string Task, string? Context = null);
public record ArchitecturePlanRequestDto(string Prompt, string? TargetOs = null);
public record BuildRequestDto(string ProjectPath);
public record WorkspaceProjectRequestDto(string ProjectPath);
public record WorkspaceNodeRequestDto(string ProjectPath, string RelativePath);
public record WorkspaceCreateRequestDto(string ProjectPath, string? ParentRelativePath, string Name, bool IsFolder);
public record WorkspaceRenameRequestDto(string ProjectPath, string RelativePath, string NewName);
public record WorkspaceDeleteRequestDto(string ProjectPath, string RelativePath);
public record WorkspaceFileDto(string Name, string Path, string Language);
public record WorkspaceFolderDto(string Name, string Path, IReadOnlyList<WorkspaceFileDto> Files, IReadOnlyList<WorkspaceFolderDto> Folders);
public record WorkspaceProjectDto(string Name, string FullPath, WorkspaceFolderDto Root);
public record SearchRequestDto(string Query, int? Limit, string? Domain = null, string? PipelineVersion = null, bool? IncludeContext = null);
public record AddGoalDto(string Title, string Description);
public record UpdateGoalDto(string Title, string Description);
public record RagIngestRequestDto(string Title, string Content);
public record FileWriteRequestDto(string Path, string? Content);
public record LearningStartRequest(string? TargetPath, string? TargetDomain, bool? SingleFileOnly = null);
public record LibraryItemDto(string Path, string Name, string Folder, string Status);
public record SessionTokenExchangeRequestDto(
    string? ApiKey = null,
    string? Surface = null,
    IReadOnlyList<string>? RequestedScopes = null,
    int? TtlMinutes = null);
public record RuntimeLogSourceDto(
    string Id,
    string Label,
    string DisplayPath,
    long SizeBytes,
    DateTimeOffset? LastWriteTimeUtc,
    int TotalLines,
    bool IsPrimary);
public record RuntimeLogEntryDto(
    string SourceId,
    int LineNumber,
    string Text,
    string Severity,
    string? TimestampLabel,
    bool IsContinuation,
    RuntimeLogSemanticsDto? Semantics = null);
public record RuntimeLogSemanticsDto(
    string Scope,
    string Domain,
    string OperationKind,
    string Summary,
    string? Route = null,
    string? CorrelationId = null,
    int? LatencyMs = null,
    string? LatencyBucket = null,
    string? DegradationReason = null,
    IReadOnlyList<string>? Markers = null,
    bool Structured = true);
public record RuntimeLogsSnapshotDto(
    int SchemaVersion,
    string SemanticsVersion,
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<RuntimeLogSourceDto> Sources,
    IReadOnlyList<RuntimeLogEntryDto> Entries,
    IReadOnlyList<string> Alerts);
public record ModelCapabilityCatalogEntryDto(
    string CapabilityId,
    string RouteKey,
    string ModelClass,
    string IntendedUse,
    string LatencyTier,
    bool SupportsStreaming,
    bool SupportsToolUse,
    bool SupportsVision,
    string FallbackClass,
    string? ConfiguredFallbackModel,
    string ResolvedModel,
    bool ResolvedModelAvailable,
    IReadOnlyList<string> Notes);
public record DeclaredCapabilityCatalogEntryDto(
    string CapabilityId,
    string SurfaceKind,
    string OwnerId,
    string DisplayName,
    string DeclaredCapability,
    string Status,
    string? OwningGate,
    string? EvidenceType,
    string? EvidenceRef,
    bool Available,
    bool CertificationRelevant,
    bool EnabledInCertification,
    bool Certified,
    bool HasCriticalAlerts,
    IReadOnlyList<string> Notes);
public record CapabilityCatalogSurfaceSummaryDto(
    string SurfaceKind,
    int Total,
    int Available,
    int Certified,
    int MissingGateOwnership,
    int DisabledInCertification,
    int Degraded);
public record CapabilityCatalogSummaryDto(
    int TotalDeclaredCapabilities,
    int MissingGateOwnership,
    int DisabledInCertification,
    int Degraded,
    IReadOnlyList<CapabilityCatalogSurfaceSummaryDto> Surfaces);
public record CapabilityCatalogSnapshotDto(
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<ModelCapabilityCatalogEntryDto> Models,
    IReadOnlyList<DeclaredCapabilityCatalogEntryDto> DeclaredCapabilities,
    CapabilityCatalogSummaryDto Summary,
    IReadOnlyList<string> Alerts);
public record SessionTokenResponseDto(
    string AccessToken,
    DateTimeOffset ExpiresAtUtc,
    int ExpiresInSeconds,
    string TokenType,
    string PrincipalType,
    string Role,
    string KeyId,
    IReadOnlyList<string> Scopes,
    string Surface);
public record AuthKeyRotateRequestDto(
    string? KeyId = null,
    string? Role = null,
    IReadOnlyList<string>? Scopes = null,
    int? TtlDays = null);
public record AuthKeyRevokeRequestDto(string? Reason = null);
public record AuthKeyRotateResponseDto(
    string KeyId,
    string ApiKey,
    string Role,
    IReadOnlyList<string> Scopes,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ExpiresAtUtc,
    string PrincipalType);
public record AuthKeyMetadataDto(
    string KeyId,
    string Role,
    IReadOnlyList<string> Scopes,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ExpiresAtUtc,
    bool IsRevoked,
    DateTimeOffset? RevokedAtUtc,
    string? RevokedReason,
    string PrincipalType,
    bool IsSystemManaged);
public record AttachmentDto(string Id, string Type, string Name, long SizeBytes, string? ReferenceUri = null);
public record BranchCreateRequestDto(string FromTurnId, string? BranchId = null, string? IdempotencyKey = null);
public record BranchMergeRequestDto(string SourceBranchId, string TargetBranchId, string? IdempotencyKey = null);
public record BranchActivateRequestDto(string BranchId);
public record FeedbackRequestDto(string? TurnId, int Rating, IReadOnlyList<string>? Tags = null, string? Comment = null);
public record ConversationRepairRequestDto(
    string CorrectedIntent,
    string? TurnId = null,
    string? RepairNote = null,
    int? MaxHistory = null,
    string? SystemInstruction = null,
    string? BranchId = null,
    string? IdempotencyKey = null,
    string? LiveWebMode = null);
public record TurnRegenerateRequestDto(int? MaxHistory = null, string? SystemInstruction = null, string? BranchId = null, string? IdempotencyKey = null, string? LiveWebMode = null);

public record ChatMessageDto(
    string Role,
    string Content,
    DateTimeOffset Timestamp,
    string? TurnId = null,
    int TurnVersion = 1,
    string? BranchId = null,
    IReadOnlyList<string>? ToolCalls = null,
    IReadOnlyList<string>? Citations = null,
    IReadOnlyList<AttachmentDto>? Attachments = null,
    string? InputMode = null);
public record ChatRequestDto(
    string Message,
    string? ConversationId,
    int? MaxHistory,
    string? SystemInstruction,
    string? BranchId = null,
    IReadOnlyList<AttachmentDto>? Attachments = null,
    string? IdempotencyKey = null,
    string? LiveWebMode = null,
    string? InputMode = null);
public record ChatResumeRequestDto(int? MaxHistory, string? SystemInstruction, string? IdempotencyKey = null, string? LiveWebMode = null);
public record ChatStreamResumeRequestDto(int CursorOffset = 0, int? MaxHistory = null, string? SystemInstruction = null, string? TurnId = null, string? IdempotencyKey = null, string? LiveWebMode = null);
public record ConversationPreferenceDto(
    bool? LongTermMemoryEnabled,
    string? PreferredLanguage,
    string? DetailLevel,
    string? Formality = null,
    string? DomainFamiliarity = null,
    string? PreferredStructure = null,
    string? Warmth = null,
    string? Enthusiasm = null,
    string? Directness = null,
    string? DefaultAnswerShape = null,
    string? SearchLocalityHint = null,
    bool? PersonalMemoryConsentGranted = null,
    int? SessionMemoryTtlMinutes = null,
    int? TaskMemoryTtlHours = null,
    int? LongTermMemoryTtlDays = null);
public record ReasoningEfficiencyMetricsDto(
    bool PathActive,
    bool BranchingApplied,
    int BranchesExplored,
    int CandidatesRejected,
    int LocalVerificationChecks,
    int LocalVerificationPasses,
    int LocalVerificationRejects,
    int ModelCallsUsed,
    int RetrievalChunksUsed,
    int ProceduralLessonsUsed,
    int ApproximateTokenCost,
    string? SelectedStrategy = null);
public record PostTurnAuditStatusDto(
    bool Eligible,
    bool ExpectedTrace,
    bool StrictMode,
    string Decision,
    int OutstandingAtDecision,
    int PendingAtDecision,
    int MaxOutstandingAudits);
public record ConversationStyleTelemetryDto(
    string? LeadPhraseFingerprint,
    bool MixedLanguageDetected,
    bool GenericClarificationDetected,
    bool GenericNextStepDetected,
    bool MemoryAckTemplateDetected,
    string? SourceFingerprint);
public record SearchTraceSourceDto(
    int Ordinal,
    string Title,
    string Url,
    string? PublishedAt = null,
    string? EvidenceKind = null,
    string? TrustLevel = null,
    bool WasSanitized = false,
    IReadOnlyList<string>? SafetyFlags = null,
    string? Snippet = null,
    int PassageCount = 0);
public record SearchTraceDto(
    string RequestedMode,
    string ResolvedRequirement,
    string? Reason,
    string Status,
    IReadOnlyList<string>? Signals = null,
    IReadOnlyList<string>? Events = null,
    IReadOnlyList<SearchTraceSourceDto>? Sources = null,
    string? InputMode = null);
public record EpistemicRiskSnapshotDto(
    string AnswerMode,
    string? GroundingStatus,
    double CitationCoverage,
    double VerifiedClaimRatio,
    bool HasContradictions,
    bool HasWeakEvidence,
    bool HighRiskDomain,
    bool FreshnessSensitive,
    double ConfidenceCeiling,
    double CalibrationThreshold,
    bool AbstentionRecommended,
    IReadOnlyList<string>? Trace = null);
public record InteractionStateSnapshotDto(
    string FrustrationLevel,
    string UrgencyLevel,
    string OverloadRisk,
    string ReassuranceNeed,
    int ClarificationToleranceShift,
    string AssistantPressureRisk,
    IReadOnlyList<string>? Signals = null);
public record ChatResponseDto(
    string ConversationId,
    string Response,
    IReadOnlyList<ChatMessageDto> Messages,
    DateTimeOffset Timestamp,
    double Confidence = 0.0,
    IReadOnlyList<string>? Sources = null,
    string? TurnId = null,
    IReadOnlyList<string>? ToolCalls = null,
    bool RequiresConfirmation = false,
    string? NextStep = null,
    string? GroundingStatus = null,
    double CitationCoverage = 0.0,
    int VerifiedClaims = 0,
    int TotalClaims = 0,
    IReadOnlyList<string>? UncertaintyFlags = null,
    string? BranchId = null,
    IReadOnlyList<string>? AvailableBranches = null,
    IReadOnlyList<ClaimGrounding>? ClaimGroundings = null,
    string? ExecutionMode = null,
    string? BudgetProfile = null,
    bool BudgetExceeded = false,
    int EstimatedTokensGenerated = 0,
    string? Intent = null,
    double IntentConfidence = 0.0,
    IReadOnlyList<string>? ExecutionTrace = null,
    IReadOnlyList<string>? LifecycleTrace = null,
    ReasoningEfficiencyMetricsDto? ReasoningMetrics = null,
    string? ReasoningEffort = null,
    string? DecisionExplanation = null,
    string? RepairClass = null,
    string? RepairDriver = null,
    string? EpistemicAnswerMode = null,
    EpistemicRiskSnapshotDto? EpistemicRisk = null,
    InteractionStateSnapshotDto? InteractionState = null,
    PostTurnAuditStatusDto? AuditStatus = null,
    ConversationStyleTelemetryDto? StyleTelemetry = null,
    SearchTraceDto? SearchTrace = null,
    string? InputMode = null);

public record ConversationFeedbackSnapshot(
    int TotalVotes,
    double AverageRating,
    double HelpfulnessScore,
    IReadOnlyList<string> Alerts);

public sealed class HelperHub : Hub { }

