namespace Helper.Api.Hosting;

public interface IConversationMetricsService
{
    void RecordTurn(ConversationTurnMetric metric);
    ConversationMetricsSnapshot GetSnapshot();
}

public sealed record ReasoningTurnMetric(
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
    int ApproximateTokenCost);

public sealed record ConversationStyleTurnMetric(
    string? LeadPhraseFingerprint,
    bool MixedLanguageDetected,
    bool GenericClarificationDetected,
    bool GenericNextStepDetected,
    bool MemoryAckTemplateDetected,
    string? SourceFingerprint);

public sealed record ConversationTurnMetric(
    long FirstTokenLatencyMs,
    long FullResponseLatencyMs,
    int ToolCallsCount,
    bool IsFactualPrompt,
    bool HasCitations,
    double Confidence,
    bool IsSuccessful,
    int VerifiedClaims = 0,
    int TotalClaims = 0,
    long? ModelTtftMs = null,
    long? TransportTtftMs = null,
    long? EndToEndTtftMs = null,
    string? ExecutionMode = null,
    bool BudgetExceeded = false,
    string? Intent = null,
    bool ResearchClarificationFallback = false,
    ReasoningTurnMetric? Reasoning = null,
    ConversationStyleTurnMetric? Style = null);

public sealed record ReasoningEfficiencyMetricsSnapshot(
    int Turns,
    int BranchingTurns,
    double BranchingRate,
    double AvgBranchesExplored,
    double AvgCandidatesRejected,
    int LocalVerificationChecks,
    int LocalVerificationPasses,
    int LocalVerificationRejects,
    double LocalVerificationPassRate,
    double AvgModelCallsUsed,
    double AvgRetrievalChunksUsed,
    double AvgProceduralLessonsUsed,
    double AvgApproximateTokenCost,
    IReadOnlyList<string> Alerts);

public sealed record ConversationStyleMetricsSnapshot(
    int Turns,
    double RepeatedPhraseRate,
    double MixedLanguageTurnRate,
    double GenericClarificationRate,
    double GenericNextStepRate,
    double MemoryAckTemplateRate,
    double SourceReuseDominance,
    IReadOnlyList<string> Alerts);

public sealed record ConversationMetricsSnapshot(
    int TotalTurns,
    double AvgFirstTokenLatencyMs,
    double AvgFullResponseLatencyMs,
    int TotalToolCalls,
    double CitationCoverage,
    int VerifiedClaims,
    int TotalClaims,
    double AvgConfidence,
    double SuccessRate,
    double AvgModelTtftMs,
    double AvgTransportTtftMs,
    double AvgEndToEndTtftMs,
    double BudgetExceededRate,
    int FastModeTurns,
    int BalancedModeTurns,
    int DeepModeTurns,
    int UnknownModeTurns,
    int ResearchRoutedTurns,
    int ResearchClarificationFallbackTurns,
    ConversationStyleMetricsSnapshot Style,
    ReasoningEfficiencyMetricsSnapshot Reasoning,
    IReadOnlyList<string> Alerts);
