namespace Helper.Api.Conversation.Epistemic;

public sealed record EpistemicRiskSnapshot(
    string GroundingStatus,
    double CitationCoverage,
    double VerifiedClaimRatio,
    bool HasContradictions,
    bool HasWeakEvidence,
    bool HighRiskDomain,
    bool FreshnessSensitive,
    double CurrentConfidence,
    double ConfidenceCeiling,
    double CalibrationThreshold,
    bool AbstentionRecommended,
    IReadOnlyList<string> Trace);
