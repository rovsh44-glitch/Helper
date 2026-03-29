namespace Helper.Api.Conversation;

public sealed record BranchDescriptor(string BranchId, string? ParentBranchId, string? FromTurnId, DateTimeOffset CreatedAt);

