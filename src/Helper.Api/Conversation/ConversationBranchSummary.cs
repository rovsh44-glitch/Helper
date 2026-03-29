namespace Helper.Api.Conversation;

public sealed record ConversationBranchSummary(
    string BranchId,
    string Summary,
    int SourceMessageCount,
    double QualityScore,
    DateTimeOffset UpdatedAt);

