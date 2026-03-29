using Helper.Api.Hosting;

namespace Helper.Api.Conversation;

public interface IConversationSummarizer
{
    ConversationBranchSummary? TryBuild(
        string branchId,
        IReadOnlyList<ChatMessageDto> branchMessages,
        ConversationBranchSummary? previous,
        DateTimeOffset now);
}

