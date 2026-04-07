namespace Helper.Api.Conversation;

public interface IFollowThroughScheduler
{
    void QueueResearchFollowThrough(ConversationState state, ChatTurnContext context, DateTimeOffset now);
}

public sealed class FollowThroughScheduler : IFollowThroughScheduler
{
    public void QueueResearchFollowThrough(ConversationState state, ChatTurnContext context, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(context);
    }
}
