namespace Helper.Api.Conversation;

public sealed record ReasoningCandidatePlan(
    string StrategyId,
    string InstructionSuffix);

public sealed record ReasoningCandidate(
    string StrategyId,
    string Output,
    ReasoningVerificationReport Verification,
    int EstimatedTokens,
    bool Selected);

