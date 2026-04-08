using Helper.Api.Conversation;
using Helper.Api.Hosting;
using Helper.Runtime.Core;

namespace Helper.Runtime.Tests;

[Trait("Lane", "Integration")]
public sealed class ConversationFollowThroughTests
{
    [Fact]
    public void FollowThroughScheduler_Queues_ProjectScoped_Research_FollowThrough()
    {
        var state = new ConversationState("conv-follow-through")
        {
            ProjectContext = new ProjectContextState("helper", "Helper", "keep researching", MemoryEnabled: true, Array.Empty<string>(), DateTimeOffset.UtcNow),
            ProactiveUpdatesEnabled = true
        };
        var context = new ChatTurnContext
        {
            TurnId = "turn-follow-through",
            Request = new ChatRequestDto("Monitor this topic and continue research.", state.Id, 12, null),
            Conversation = state,
            History = Array.Empty<ChatMessageDto>(),
            Intent = new IntentAnalysis(IntentType.Research, "test-model"),
            CollaborationIntent = new CollaborationIntentAnalysis(
                IsGuidanceSeeking: false,
                TrustsBestJudgment: true,
                SeeksDelegatedExecution: false,
                PrefersAnswerOverClarification: true,
                HasHardConstraintLanguage: false,
                PrimaryMode: "follow_through",
                Signals: new[] { "best_judgment" })
        };

        new FollowThroughScheduler().QueueResearchFollowThrough(state, context, DateTimeOffset.UtcNow);

        Assert.Single(state.BackgroundTasks);
        Assert.Equal("research_follow_through", state.BackgroundTasks[0].Kind);
        Assert.Equal("helper", state.BackgroundTasks[0].ProjectId);
        Assert.Single(state.ProactiveTopics);
    }

    [Fact]
    public void FollowThroughScheduler_Respects_OptIn_Toggles()
    {
        var state = new ConversationState("conv-follow-through-disabled")
        {
            BackgroundResearchEnabled = false,
            ProactiveUpdatesEnabled = false
        };
        var context = new ChatTurnContext
        {
            TurnId = "turn-follow-through-disabled",
            Request = new ChatRequestDto("Monitor this topic and continue research.", state.Id, 12, null),
            Conversation = state,
            History = Array.Empty<ChatMessageDto>(),
            Intent = new IntentAnalysis(IntentType.Research, "test-model"),
            CollaborationIntent = new CollaborationIntentAnalysis(
                IsGuidanceSeeking: false,
                TrustsBestJudgment: true,
                SeeksDelegatedExecution: false,
                PrefersAnswerOverClarification: true,
                HasHardConstraintLanguage: false,
                PrimaryMode: "follow_through",
                Signals: new[] { "best_judgment" })
        };

        new FollowThroughScheduler().QueueResearchFollowThrough(state, context, DateTimeOffset.UtcNow);

        Assert.Empty(state.BackgroundTasks);
        Assert.Empty(state.ProactiveTopics);
    }

    [Fact]
    public void ConversationFollowThroughProcessor_Completes_Queued_Task_And_Appends_Assistant_Message()
    {
        var store = new InMemoryConversationStore(
            new MemoryPolicyService(),
            new ConversationSummarizer(),
            persistenceEngine: null,
            writeBehindQueue: null);
        var state = store.GetOrCreate("conv-follow-through-processor");
        state.ProjectContext = new ProjectContextState("helper", "Helper", "keep continuity", MemoryEnabled: true, Array.Empty<string>(), DateTimeOffset.UtcNow);
        state.BackgroundTasks.Add(new BackgroundConversationTask(
            "bg-task-1",
            "research_follow_through",
            "Continue research follow-through",
            "queued",
            DateTimeOffset.UtcNow.AddMinutes(-10),
            DateTimeOffset.UtcNow.AddMinutes(-5),
            "helper",
            "Track the latest helper communication parity changes."));

        var processor = new ConversationFollowThroughProcessor(store, store);

        var processed = processor.ProcessPending(DateTimeOffset.UtcNow);

        Assert.Equal(1, processed);
        Assert.Contains(state.BackgroundTasks, task => task.Id == "bg-task-1" && task.Status == "completed");
        Assert.Contains(state.Messages, message => message.Role == "assistant" && message.Content.Contains("Background follow-through completed", StringComparison.Ordinal));
    }

    [Fact]
    public void ConversationFollowThroughProcessor_Can_Cancel_Task_And_Toggle_Topic()
    {
        var store = new InMemoryConversationStore(
            new MemoryPolicyService(),
            new ConversationSummarizer(),
            persistenceEngine: null,
            writeBehindQueue: null);
        var state = store.GetOrCreate("conv-follow-through-controls");
        state.BackgroundTasks.Add(new BackgroundConversationTask(
            "bg-task-2",
            "research_follow_through",
            "Continue research follow-through",
            "queued",
            DateTimeOffset.UtcNow,
            null,
            "helper",
            "Task note"));
        state.ProactiveTopics.Add(new ProactiveTopicSubscription(
            "topic-1",
            "Monitor helper communication parity",
            "manual",
            Enabled: true,
            DateTimeOffset.UtcNow,
            "helper"));

        var processor = new ConversationFollowThroughProcessor(store, store);

        Assert.True(processor.CancelTask(state.Id, "bg-task-2", "Canceled during test."));
        Assert.True(processor.SetTopicEnabled(state.Id, "topic-1", enabled: false));
        Assert.Contains(state.BackgroundTasks, task => task.Id == "bg-task-2" && task.Status == "canceled");
        Assert.Contains(state.ProactiveTopics, topic => topic.Id == "topic-1" && !topic.Enabled);
    }
}
