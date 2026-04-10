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
            var projectId = state.ProjectContext?.ProjectId;
            var branchId = state.ActiveBranchId;

            if (state.ProactiveUpdatesEnabled &&
                _topicPolicy.ShouldRegister(context) &&
                !state.ProactiveTopics.Any(topic =>
                    topic.Enabled &&
                    topic.Topic.Equals(context.Request.Message.Trim(), StringComparison.OrdinalIgnoreCase) &&
                    TopicMatchesProject(topic.ProjectId, projectId)))
            {
                state.ProactiveTopics.Add(new ProactiveTopicSubscription(
                    Guid.NewGuid().ToString("N"),
                    context.Request.Message.Trim(),
                    "manual",
                    Enabled: true,
                    now,
                    state.ProjectContext?.ProjectId));
            }

            if (state.BackgroundResearchEnabled &&
                context.Intent.Intent == Helper.Runtime.Core.IntentType.Research &&
                !state.BackgroundTasks.Any(task => TaskMatchesQueuedScope(task, projectId, branchId)))
            {
                var proactiveTopicSnapshot = state.ProactiveTopics
                    .Where(topic => topic.Enabled && TopicMatchesProject(topic.ProjectId, projectId))
                    .Select(topic => topic.Topic)
                    .Take(3)
                    .ToArray();

                state.BackgroundTasks.Add(new BackgroundConversationTask(
                    Guid.NewGuid().ToString("N"),
                    "research_follow_through",
                    "Continue research follow-through",
                    "queued",
                    now,
                    now.AddHours(4),
                    projectId,
                    context.Request.Message.Trim(),
                    BranchId: state.ActiveBranchId,
                    ProjectLabelSnapshot: state.ProjectContext?.Label ?? state.ProjectContext?.ProjectId,
                    ReferenceArtifactsSnapshot: state.ProjectContext?.ReferenceArtifacts.ToArray() ?? Array.Empty<string>(),
                    ProactiveTopicSnapshot: proactiveTopicSnapshot));
            }
        }
    }

    private static bool TopicMatchesProject(string? topicProjectId, string? projectId)
    {
        if (string.IsNullOrWhiteSpace(NormalizeScope(projectId)))
        {
            return string.IsNullOrWhiteSpace(NormalizeScope(topicProjectId));
        }

        return string.Equals(NormalizeScope(topicProjectId), NormalizeScope(projectId), StringComparison.OrdinalIgnoreCase);
    }

    private static bool TaskMatchesQueuedScope(BackgroundConversationTask task, string? projectId, string? branchId)
    {
        if (!string.Equals(task.Status, "queued", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var normalizedProjectId = NormalizeScope(projectId);
        var normalizedBranchId = NormalizeScope(branchId);
        var taskProjectId = NormalizeScope(task.ProjectId) ?? normalizedProjectId;
        var taskBranchId = NormalizeScope(task.BranchId) ?? normalizedBranchId;

        return string.Equals(taskProjectId, normalizedProjectId, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(taskBranchId, normalizedBranchId, StringComparison.OrdinalIgnoreCase);
    }

    private static string? NormalizeScope(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }
}
