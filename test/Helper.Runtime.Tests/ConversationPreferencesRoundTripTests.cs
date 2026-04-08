using Helper.Api.Conversation;
using Helper.Api.Hosting;

namespace Helper.Runtime.Tests;

public sealed class ConversationPreferencesRoundTripTests
{
    [Fact]
    public void PreferencesRoundTrip_Persists_AdvancedPersonalization_ProjectContext_And_FollowThroughFlags()
    {
        var state = new ConversationState("conv-roundtrip");
        var userProfile = new UserProfileService();
        var memoryPolicy = new MemoryPolicyService();
        var now = DateTimeOffset.UtcNow;

        var dto = new ConversationPreferenceDto(
            LongTermMemoryEnabled: true,
            PreferredLanguage: "en",
            DetailLevel: "deep",
            Formality: "formal",
            DomainFamiliarity: "expert",
            PreferredStructure: "step_by_step",
            Warmth: "warm",
            Enthusiasm: "high",
            Directness: "direct",
            DefaultAnswerShape: "bullets",
            SearchLocalityHint: "berlin",
            DecisionAssertiveness: "high",
            ClarificationTolerance: "low",
            CitationPreference: "prefer",
            RepairStyle: "explain_first",
            ReasoningStyle: "exploratory",
            ReasoningEffort: "deep",
            PersonaBundleId: "research-mentor",
            ProjectId: "helper-public",
            ProjectLabel: "Helper Public",
            ProjectInstructions: "Keep public contract honest.",
            ProjectMemoryEnabled: true,
            BackgroundResearchEnabled: false,
            ProactiveUpdatesEnabled: true,
            PersonalMemoryConsentGranted: true,
            SessionMemoryTtlMinutes: 120,
            TaskMemoryTtlHours: 48,
            LongTermMemoryTtlDays: 365);

        userProfile.ApplyPreferences(state, dto);
        memoryPolicy.ApplyPreferences(state, dto, now);
        state.BackgroundTasks.Add(new BackgroundConversationTask("task-1", "research_follow_through", "Continue review", "queued", now, now.AddHours(1), "helper-public", "queued for parity"));
        state.ProactiveTopics.Add(new ProactiveTopicSubscription("topic-1", "public parity", "manual", true, now, "helper-public"));

        var persisted = ConversationPersistenceModelMapper.ToPersistenceModel(state);
        var restored = ConversationPersistenceModelMapper.FromPersistenceModel(persisted);
        var restoredProfile = userProfile.Resolve(restored);

        Assert.Equal("high", restoredProfile.DecisionAssertiveness);
        Assert.Equal("low", restoredProfile.ClarificationTolerance);
        Assert.Equal("prefer", restoredProfile.CitationPreference);
        Assert.Equal("explain_first", restoredProfile.RepairStyle);
        Assert.Equal("exploratory", restoredProfile.ReasoningStyle);
        Assert.Equal("deep", restoredProfile.ReasoningEffort);
        Assert.Equal("research-mentor", restoredProfile.PersonaBundleId);
        Assert.Equal("helper-public", restored.ProjectContext?.ProjectId);
        Assert.Equal("Helper Public", restored.ProjectContext?.Label);
        Assert.Equal("Keep public contract honest.", restored.ProjectContext?.Instructions);
        Assert.True(restored.ProjectContext?.MemoryEnabled);
        Assert.False(restored.BackgroundResearchEnabled);
        Assert.True(restored.ProactiveUpdatesEnabled);
        Assert.Single(restored.BackgroundTasks);
        Assert.Single(restored.ProactiveTopics);
        Assert.Equal("deep", restored.PersonalizationProfile?.ReasoningEffort);
        Assert.Equal("research-mentor", restored.PersonalizationProfile?.PersonaBundleId);
    }
}
