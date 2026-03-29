namespace Helper.Api.Conversation;

public sealed record ConversationMemoryPolicySnapshot(
    bool LongTermMemoryEnabled,
    bool PersonalMemoryConsentGranted,
    DateTimeOffset? PersonalMemoryConsentAt,
    int SessionMemoryTtlMinutes,
    int TaskMemoryTtlHours,
    int LongTermMemoryTtlDays);

