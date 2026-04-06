using Helper.Runtime.Core;

namespace Helper.Api.Conversation;

public interface IFollowThroughScheduler
{
    void QueueResearchFollowThrough(ConversationState state, ChatTurnContext context, DateTimeOffset now);
}

public sealed class FollowThroughScheduler : IFollowThroughScheduler
{
    private readonly IProactiveTopicPolicy _topicPolicy;

    public FollowThroughScheduler(IProactiveTopicPolicy? topicPolicy = null)
    {
        _topicPolicy = topicPolicy ?? new ProactiveTopicPolicy();
    }

    public void QueueResearchFollowThrough(ConversationState state, ChatTurnContext context, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(context);

        lock (state.SyncRoot)
        {
            if (state.BackgroundResearchEnabled &&
                context.Intent.Intent == IntentType.Research &&
                !state.BackgroundTasks.Any(task => task.Status == "queued"))
            {
                state.BackgroundTasks.Add(new BackgroundConversationTask(
                    Guid.NewGuid().ToString("N"),
                    "research_follow_through",
                    "Continue research follow-through",
                    "queued",
                    now,
                    now.AddHours(4),
                    state.ProjectContext?.ProjectId,
                    context.Request.Message.Trim()));
            }

            if (state.ProactiveUpdatesEnabled &&
                _topicPolicy.ShouldRegister(context) &&
                !state.ProactiveTopics.Any(topic => topic.Enabled && topic.Topic.Equals(context.Request.Message.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                state.ProactiveTopics.Add(new ProactiveTopicSubscription(
                    Guid.NewGuid().ToString("N"),
                    context.Request.Message.Trim(),
                    "manual",
                    Enabled: true,
                    now,
                    state.ProjectContext?.ProjectId));
            }
        }
    }
}
