using Helper.Api.Hosting;

namespace Helper.Api.Conversation;

public interface IConversationStore
{
    ConversationState GetOrCreate(string? conversationId);
    void AddMessage(ConversationState state, ChatMessageDto message);
    IReadOnlyList<ChatMessageDto> GetRecentMessages(ConversationState state, int maxHistory);
    IReadOnlyList<ChatMessageDto> GetRecentMessages(ConversationState state, string branchId, int maxHistory);
    void SetActiveBranch(ConversationState state, string branchId);
    string GetActiveBranchId(ConversationState state);
    IReadOnlyList<string> GetBranchIds(ConversationState state);
    bool CreateBranch(ConversationState state, string fromTurnId, string? requestedBranchId, out string branchId);
    bool MergeBranch(ConversationState state, string sourceBranchId, string targetBranchId, out int mergedMessages, out string? error);
    bool TryGet(string conversationId, out ConversationState state);
    void MarkUpdated(ConversationState state);
    bool Remove(string conversationId);
}

