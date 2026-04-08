namespace Helper.Api.Conversation;

public sealed record UserUnderstandingState(
    string? PreferredInteractionMode,
    string? DecisionAssertiveness,
    string? ClarificationTolerance,
    string? CitationPreference,
    string? RepairStyle,
    string? ReasoningStyle,
    DateTimeOffset UpdatedAtUtc);
