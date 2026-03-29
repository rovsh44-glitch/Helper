using Helper.Api.Conversation;

namespace Helper.Api.Backend.Persistence;

public interface IConversationWriteBehindStore
{
    int PendingDirtyConversations { get; }
    IReadOnlyList<ConversationState> DrainDirtyConversations(IReadOnlyCollection<string> conversationIds);
    IReadOnlyList<ConversationState> SnapshotAllConversations();
    void FlushDirtyConversationsNow();
}

