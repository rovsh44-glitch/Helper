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
    private readonly IConversationStore _store;
    private readonly IConversationWriteBehindStore _snapshotStore;

    public ConversationFollowThroughProcessor(
        IConversationStore store,
        IConversationWriteBehindStore snapshotStore)
    {
        _store = store;
        _snapshotStore = snapshotStore;
    }

    public int ProcessPending(DateTimeOffset now)
    {
        var processed = 0;
        foreach (var state in _snapshotStore.SnapshotAllConversations())
        {
            var task = TryStartNextTask(state, now);
            if (task == null)
            {
                continue;
            }

            var message = BuildCompletionMessage(state, task, now);
            var activeBranchId = _store.GetActiveBranchId(state);
            _store.AddMessage(state, new ChatMessageDto(
                "assistant",
                message,
                now,
                TurnId: $"background-{task.Id}",
                BranchId: activeBranchId,
                ToolCalls: new[] { "background_research_follow_through" }));

            CompleteTask(state, task.Id, now);
            processed++;
        }

        return processed;
    }

    public bool CancelTask(string conversationId, string taskId, string? reason = null)
    {
        if (!_store.TryGet(conversationId, out var state))
        {
            return false;
        }

        lock (state.SyncRoot)
        {
            var index = state.BackgroundTasks.FindIndex(task => task.Id.Equals(taskId, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                return false;
            }

            var current = state.BackgroundTasks[index];
            if (string.Equals(current.Status, "completed", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(current.Status, "canceled", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            state.BackgroundTasks[index] = current with
            {
                Status = "canceled",
                Notes = BuildCanceledNotes(current.Notes, reason)
            };
            state.UpdatedAt = DateTimeOffset.UtcNow;
        }

        _store.MarkUpdated(state);
        return true;
    }

    public bool SetTopicEnabled(string conversationId, string topicId, bool enabled)
    {
        if (!_store.TryGet(conversationId, out var state))
        {
            return false;
        }

        lock (state.SyncRoot)
        {
            var index = state.ProactiveTopics.FindIndex(topic => topic.Id.Equals(topicId, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                return false;
            }

            var current = state.ProactiveTopics[index];
            if (current.Enabled == enabled)
            {
                return true;
            }

            state.ProactiveTopics[index] = current with
            {
                Enabled = enabled
            };
            state.UpdatedAt = DateTimeOffset.UtcNow;
        }

        _store.MarkUpdated(state);
        return true;
    }

    private BackgroundConversationTask? TryStartNextTask(ConversationState state, DateTimeOffset now)
    {
        lock (state.SyncRoot)
        {
            var index = state.BackgroundTasks.FindIndex(task =>
                string.Equals(task.Status, "queued", StringComparison.OrdinalIgnoreCase) &&
                (!task.DueAtUtc.HasValue || task.DueAtUtc.Value <= now));
            if (index < 0)
            {
                return null;
            }

            var current = state.BackgroundTasks[index];
            state.BackgroundTasks[index] = current with { Status = "running" };
            state.UpdatedAt = now;
            return current;
        }
    }

    private void CompleteTask(ConversationState state, string taskId, DateTimeOffset now)
    {
        lock (state.SyncRoot)
        {
            var index = state.BackgroundTasks.FindIndex(task => task.Id.Equals(taskId, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                return;
            }

            state.BackgroundTasks[index] = state.BackgroundTasks[index] with
            {
                Status = "completed"
            };
            state.UpdatedAt = now;
        }

        _store.MarkUpdated(state);
    }

    private static string BuildCompletionMessage(ConversationState state, BackgroundConversationTask task, DateTimeOffset now)
    {
        var projectLabel = state.ProjectContext?.Label ?? state.ProjectContext?.ProjectId ?? task.ProjectId ?? "this conversation";
        var references = state.ProjectContext?.ReferenceArtifacts ?? Array.Empty<string>();
        var referenceSummary = references.Count == 0
            ? "No shared multimodal references were active."
            : $"Active references: {string.Join(", ", references.Take(3))}.";
        var proactiveSummary = state.ProactiveTopics.Count == 0
            ? "No proactive topics are currently enabled."
            : $"Enabled follow-up topics: {string.Join(", ", state.ProactiveTopics.Where(topic => topic.Enabled).Select(topic => topic.Topic).Take(3))}.";
        var dueLabel = task.DueAtUtc?.ToString("u") ?? "immediate";

        return
            $"Background follow-through completed for {projectLabel}. " +
            $"Task '{task.Title}' moved from queue to completed at {now:u}. " +
            $"Notes: {(string.IsNullOrWhiteSpace(task.Notes) ? "n/a" : task.Notes.Trim())}. " +
            $"{referenceSummary} {proactiveSummary} " +
            $"Queued due time was {dueLabel}. Resume this conversation if you want the next pass synthesized into a fresh user-visible answer.";
    }

    private static string BuildCanceledNotes(string? existingNotes, string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return existingNotes ?? "Canceled by operator.";
        }

        return string.IsNullOrWhiteSpace(existingNotes)
            ? $"Canceled: {reason.Trim()}"
            : $"{existingNotes.Trim()} | Canceled: {reason.Trim()}";
    }
}
