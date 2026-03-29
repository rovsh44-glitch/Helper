namespace Helper.Api.Conversation;

public sealed record ClaimSourceMatch(
    int SourceIndex,
    double Score,
    string MatchMode,
    string? QuoteSpan = null,
    double Confidence = 0,
    bool ContradictionDetected = false,
    IReadOnlyList<string>? Signals = null);

