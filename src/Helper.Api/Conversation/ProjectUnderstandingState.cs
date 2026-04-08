namespace Helper.Api.Conversation;

public sealed record ProjectUnderstandingState(
    string? ProjectId,
    string? WorkingGoal,
    string? CollaborationContract,
    DateTimeOffset UpdatedAtUtc);
