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

        store.SetActiveBranch(state, branchId);
        var processor = new ConversationFollowThroughProcessor(store, store);

        var processed = processor.ProcessPending(now.AddHours(5));

        Assert.Equal(1, processed);
        Assert.Contains(state.Messages, message =>
            message.Role == "assistant" &&
            string.Equals(message.BranchId, "main", StringComparison.OrdinalIgnoreCase) &&
            message.Content.Contains("Background follow-through completed", StringComparison.Ordinal));
        Assert.DoesNotContain(state.Messages, message =>
            message.Role == "assistant" &&
            string.Equals(message.BranchId, branchId, StringComparison.OrdinalIgnoreCase) &&
            message.Content.Contains("Background follow-through completed", StringComparison.Ordinal));
    }
}
