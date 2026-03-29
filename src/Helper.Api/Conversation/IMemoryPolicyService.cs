using Helper.Api.Hosting;

namespace Helper.Api.Conversation;

public interface IMemoryPolicyService
{
    void CaptureFromUserMessage(ConversationState state, ChatMessageDto message, DateTimeOffset now);
    void ApplyPreferences(ConversationState state, ConversationPreferenceDto dto, DateTimeOffset now);
    ConversationMemoryPolicySnapshot GetPolicySnapshot(ConversationState state);
    IReadOnlyList<ConversationMemoryItem> GetActiveItems(ConversationState state, DateTimeOffset now);
    bool DeleteItem(ConversationState state, string memoryId, DateTimeOffset now);
}

