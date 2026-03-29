namespace Helper.Api.Conversation;

public sealed record ConversationMemoryItem(
    string Id,
    string Type,
    string Content,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt,
    string? SourceTurnId,
    bool IsPersonal);

