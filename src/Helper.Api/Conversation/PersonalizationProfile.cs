namespace Helper.Api.Conversation;

public sealed record PersonalizationProfile(
    string ExplanationDepth,
    string DecisionAssertiveness,
    string ClarificationTolerance,
    string CitationPreference,
    string RepairStyle,
    string ReasoningStyle,
    string ReasoningEffort,
    string? PersonaBundleId)
{
    public static readonly PersonalizationProfile Default = new(
        ExplanationDepth: "balanced",
        DecisionAssertiveness: "balanced",
        ClarificationTolerance: "balanced",
        CitationPreference: "adaptive",
        RepairStyle: "direct_fix",
        ReasoningStyle: "concise",
        ReasoningEffort: "balanced",
        PersonaBundleId: null);
}
