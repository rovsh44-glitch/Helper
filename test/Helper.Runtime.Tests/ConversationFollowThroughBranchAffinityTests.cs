using Helper.Api.Conversation;
using Helper.Api.Backend.Persistence;
using Helper.Api.Hosting;
using Helper.Runtime.Core;

namespace Helper.Runtime.Tests;

public sealed class ConversationFollowThroughBranchAffinityTests
{
    [Fact]
    public void QueueResearchFollowThrough_Stamps_Originating_Branch()
    {
        var state = new ConversationState("conv-follow-through-queue")
        {
            BackgroundResearchEnabled = true,
            ActiveBranchId = "main"
        };
        var context = new ChatTurnContext
        {
            TurnId = "turn-follow-through-queue",
            Request = new ChatRequestDto("Monitor this topic and continue research.", state.Id, 12, null, BranchId: "main"),
            Conversation = state,
            History = Array.Empty<ChatMessageDto>(),
            Intent = new IntentAnalysis(IntentType.Research, "test-model")
        };

        new FollowThroughScheduler().QueueResearchFollowThrough(state, context, DateTimeOffset.UtcNow);

        Assert.Single(state.BackgroundTasks);
        Assert.Equal("main", state.BackgroundTasks[0].BranchId);
    }

    [Fact]
    public void ConversationFollowThroughProcessor_Writes_Completion_To_Originating_Branch()
    {
        var store = new InMemoryConversationStore(
            new MemoryPolicyService(),
            new ConversationSummarizer(),
            persistenceEngine: null,
            writeBehindQueue: null);
        var state = store.GetOrCreate("conv-follow-through-branch-affinity");
        var now = DateTimeOffset.UtcNow;
        state.ProjectContext = new ProjectContextState(
            "project-a",
            "Project A",
            "Stay on project A.",
            MemoryEnabled: true,
            new[] { "a-spec.md", "a-audit.json" },
            now.AddMinutes(-20));
        state.ProactiveUpdatesEnabled = true;
        state.ProactiveTopics.Add(new ProactiveTopicSubscription(
            "topic-a",
            "Monitor project A rollout",
            "manual",
            Enabled: true,
            now.AddMinutes(-15),
            "project-a"));

        store.AddMessage(state, new ChatMessageDto("user", "base question", now.AddMinutes(-30), "turn-1", BranchId: "main"));
        store.AddMessage(state, new ChatMessageDto("assistant", "base answer", now.AddMinutes(-29), "turn-1", BranchId: "main"));
        Assert.True(store.CreateBranch(state, "turn-1", "branch-alt", out var branchId));
        store.SetActiveBranch(state, "main");

        var context = new ChatTurnContext
        {
            TurnId = "turn-follow-through-branch-affinity",
            Request = new ChatRequestDto("Monitor this topic and continue research.", state.Id, 12, null, BranchId: "main"),
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
        new FollowThroughScheduler().QueueResearchFollowThrough(state, context, now.AddMinutes(-5));
        Assert.Single(state.BackgroundTasks);
        Assert.Equal("main", state.BackgroundTasks[0].BranchId);
        Assert.Equal("Project A", state.BackgroundTasks[0].ProjectLabelSnapshot);
        Assert.Equal(new[] { "a-spec.md", "a-audit.json" }, state.BackgroundTasks[0].ReferenceArtifactsSnapshot);
        Assert.Contains("Monitor project A rollout", state.BackgroundTasks[0].ProactiveTopicSnapshot ?? Array.Empty<string>());

        store.SetActiveBranch(state, branchId);
        state.ProjectContext = new ProjectContextState(
            "project-b",
            "Project B",
            "Stay on project B.",
            MemoryEnabled: true,
            new[] { "b-spec.md" },
            now);
        state.ProactiveTopics.Clear();
        state.ProactiveTopics.Add(new ProactiveTopicSubscription(
            "topic-b",
            "Monitor project B rollout",
            "manual",
            Enabled: true,
            now,
            "project-b"));
        var processor = new ConversationFollowThroughProcessor(store, store);

        var processed = processor.ProcessPending(now.AddHours(5));

        Assert.Equal(1, processed);
        Assert.Contains(state.Messages, message =>
            message.Role == "assistant" &&
            string.Equals(message.BranchId, "main", StringComparison.OrdinalIgnoreCase) &&
            message.Content.Contains("Project A", StringComparison.Ordinal) &&
            message.Content.Contains("a-spec.md", StringComparison.Ordinal) &&
            message.Content.Contains("Monitor project A rollout", StringComparison.Ordinal) &&
            !message.Content.Contains("Project B", StringComparison.Ordinal) &&
            !message.Content.Contains("b-spec.md", StringComparison.Ordinal) &&
            !message.Content.Contains("Monitor project B rollout", StringComparison.Ordinal) &&
            message.Content.Contains("Background follow-through completed", StringComparison.Ordinal));
        Assert.DoesNotContain(state.Messages, message =>
            message.Role == "assistant" &&
            string.Equals(message.BranchId, branchId, StringComparison.OrdinalIgnoreCase) &&
            message.Content.Contains("Background follow-through completed", StringComparison.Ordinal));
    }
}
