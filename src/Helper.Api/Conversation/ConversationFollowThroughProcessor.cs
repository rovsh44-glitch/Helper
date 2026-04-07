using Helper.Api.Backend.Persistence;
using Helper.Api.Hosting;

namespace Helper.Api.Conversation;

public interface IConversationFollowThroughProcessor
{
    int ProcessPending(DateTimeOffset now);
    bool CancelTask(string conversationId, string taskId, string? reason = null);
    bool SetTopicEnabled(string conversationId, string topicId, bool enabled);
}

public sealed class ConversationFollowThroughProcessor : IConversationFollowThroughProcessor
{
    public ConversationFollowThroughProcessor(
        IConversationStore store,
        IConversationWriteBehindStore snapshotStore)
    {
    }

    public int ProcessPending(DateTimeOffset now)
    {
        _ = now;
        return 0;
    }

    public bool CancelTask(string conversationId, string taskId, string? reason = null)
    {
        _ = conversationId;
        _ = taskId;
        _ = reason;
        return false;
    }

    public bool SetTopicEnabled(string conversationId, string topicId, bool enabled)
    {
        _ = conversationId;
        _ = topicId;
        _ = enabled;
        return false;
    }
}
