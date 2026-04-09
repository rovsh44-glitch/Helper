using Helper.Api.Backend.Persistence;
using Helper.Api.Conversation;

namespace Helper.Runtime.Tests;

public sealed class BackgroundConversationTaskPersistenceTests
{
    [Fact]
    public void ConversationPersistenceModelMapper_RoundTrips_BackgroundTask_BranchId()
    {
        var state = new ConversationState("conv-background-task-roundtrip")
        {
            PersonalizationProfile = PersonalizationProfile.Default
        };
        var now = DateTimeOffset.UtcNow;

        state.BackgroundTasks.Add(new BackgroundConversationTask(
            "task-1",
            "research_follow_through",
            "Continue review",
            "queued",
            now,
            now.AddHours(1),
            "helper-public",
            "queued for parity",
            BranchId: "main"));

        var persisted = ConversationPersistenceModelMapper.ToPersistenceModel(state);
        var restored = ConversationPersistenceModelMapper.FromPersistenceModel(persisted);

        Assert.Single(restored.BackgroundTasks);
        Assert.Equal("main", restored.BackgroundTasks[0].BranchId);
    }
}
