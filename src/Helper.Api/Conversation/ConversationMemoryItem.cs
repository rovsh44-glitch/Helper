namespace Helper.Api.Conversation;

public sealed record ConversationMemoryItem(
    string Id,
    string Type,
    string Content,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt,
    string? SourceTurnId,
    bool IsPersonal,
    MemoryScope Scope = MemoryScope.Session,
    string Retention = "ttl",
    string WhyRemembered = "captured_from_turn",
    int Priority = 0,
    string? SourceProjectId = null,
    bool UserEditable = true);

