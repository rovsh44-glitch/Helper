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
            ProjectContext = new ProjectContextState("helper", "Helper", "keep researching", MemoryEnabled: true, new[] { "spec.md" }, DateTimeOffset.UtcNow),
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
        Assert.Equal("Helper", state.BackgroundTasks[0].ProjectLabelSnapshot);
        Assert.Equal(new[] { "spec.md" }, state.BackgroundTasks[0].ReferenceArtifactsSnapshot);
        Assert.Equal(new[] { "Monitor this topic and continue research." }, state.BackgroundTasks[0].ProactiveTopicSnapshot);
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
    public void FollowThroughScheduler_Scopes_Queued_Tasks_By_Project_And_Branch()
    {
        var state = new ConversationState("conv-follow-through-scopes")
        {
            BackgroundResearchEnabled = true,
            ActiveBranchId = "main",
            ProjectContext = new ProjectContextState("project-a", "Project A", null, MemoryEnabled: true, Array.Empty<string>(), DateTimeOffset.UtcNow)
        };
        var scheduler = new FollowThroughScheduler();

        scheduler.QueueResearchFollowThrough(state, CreateResearchContext(state, "Monitor scoped follow-through.", "main"), DateTimeOffset.UtcNow);
        scheduler.QueueResearchFollowThrough(state, CreateResearchContext(state, "Monitor scoped follow-through.", "main"), DateTimeOffset.UtcNow.AddMinutes(1));

        state.ProjectContext = new ProjectContextState("project-b", "Project B", null, MemoryEnabled: true, Array.Empty<string>(), DateTimeOffset.UtcNow);
        scheduler.QueueResearchFollowThrough(state, CreateResearchContext(state, "Monitor scoped follow-through.", "main"), DateTimeOffset.UtcNow.AddMinutes(2));

        state.ProjectContext = new ProjectContextState("project-a", "Project A", null, MemoryEnabled: true, Array.Empty<string>(), DateTimeOffset.UtcNow);
        state.ActiveBranchId = "branch-alt";
        scheduler.QueueResearchFollowThrough(state, CreateResearchContext(state, "Monitor scoped follow-through.", "branch-alt"), DateTimeOffset.UtcNow.AddMinutes(3));

        Assert.Equal(3, state.BackgroundTasks.Count(task => task.Status == "queued"));
        Assert.Contains(state.BackgroundTasks, task => task.Status == "queued" && task.ProjectId == "project-a" && task.BranchId == "main");
        Assert.Contains(state.BackgroundTasks, task => task.Status == "queued" && task.ProjectId == "project-b" && task.BranchId == "main");
        Assert.Contains(state.BackgroundTasks, task => task.Status == "queued" && task.ProjectId == "project-a" && task.BranchId == "branch-alt");
    }

    [Fact]
    public void FollowThroughScheduler_Deduplicates_Proactive_Topics_Within_Project_Scope()
    {
        var state = new ConversationState("conv-follow-through-topic-scope")
        {
            BackgroundResearchEnabled = false,
            ProactiveUpdatesEnabled = true,
            ActiveBranchId = "main",
            ProjectContext = new ProjectContextState("project-a", "Project A", null, MemoryEnabled: true, Array.Empty<string>(), DateTimeOffset.UtcNow)
        };
        var scheduler = new FollowThroughScheduler();

        scheduler.QueueResearchFollowThrough(state, CreateResearchContext(state, "Monitor parity drift.", "main"), DateTimeOffset.UtcNow);
        scheduler.QueueResearchFollowThrough(state, CreateResearchContext(state, "Monitor parity drift.", "main"), DateTimeOffset.UtcNow.AddMinutes(1));

        state.ProjectContext = new ProjectContextState("project-b", "Project B", null, MemoryEnabled: true, Array.Empty<string>(), DateTimeOffset.UtcNow);
        scheduler.QueueResearchFollowThrough(state, CreateResearchContext(state, "Monitor parity drift.", "main"), DateTimeOffset.UtcNow.AddMinutes(2));
        scheduler.QueueResearchFollowThrough(state, CreateResearchContext(state, "Monitor parity drift.", "main"), DateTimeOffset.UtcNow.AddMinutes(3));

        Assert.Equal(2, state.ProactiveTopics.Count);
        Assert.Equal(1, state.ProactiveTopics.Count(topic => topic.ProjectId == "project-a" && topic.Topic == "Monitor parity drift."));
        Assert.Equal(1, state.ProactiveTopics.Count(topic => topic.ProjectId == "project-b" && topic.Topic == "Monitor parity drift."));
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

    private static ChatTurnContext CreateResearchContext(ConversationState state, string message, string branchId)
    {
        return new ChatTurnContext
        {
            TurnId = $"turn-{Guid.NewGuid():N}",
            Request = new ChatRequestDto(message, state.Id, 12, null, BranchId: branchId),
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
    }
}
