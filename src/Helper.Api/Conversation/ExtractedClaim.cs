namespace Helper.Api.Conversation;

public sealed record ExtractedClaim(
    string Text,
    ClaimSentenceType Type,
    int Sequence,
    IReadOnlyList<string>? Entities = null);

