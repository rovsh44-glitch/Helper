using Helper.Api.Conversation;
using Helper.Api.Conversation.InteractionState;
using Helper.Api.Hosting;
using Helper.Runtime.Core;
using Moq;

namespace Helper.Runtime.Tests;

public sealed class TurnPersonalizationMergeTests
{
    [Fact]
    public async Task TurnPersonalizationStep_Computes_Effective_Profile_After_Collaboration_And_Factual_Signals()
    {
        var collaborationIntentDetector = new Mock<ICollaborationIntentDetector>();
        collaborationIntentDetector
            .Setup(x => x.Analyze(It.IsAny<string>(), It.IsAny<string?>()))
            .Returns(new CollaborationIntentAnalysis(
                IsGuidanceSeeking: true,
                TrustsBestJudgment: true,
                SeeksDelegatedExecution: false,
                PrefersAnswerOverClarification: true,
                HasHardConstraintLanguage: false,
                PrimaryMode: "guidance",
                Signals: new[] { "guidance_seeking" }));

        var conversation = new ConversationState("conv-personalization-step")
        {
            PersonalizationProfile = PersonalizationProfile.Default
        };
        var step = new TurnPersonalizationStep(
            new UserProfileService(),
            new TurnLanguageResolver(),
            collaborationIntentDetector.Object,
            new CommunicationQualityPolicy(),
            new PersonalizationMergePolicy(),
            new LocalFirstBenchmarkPolicy(),
            new InteractionStateAnalyzer(),
            new InteractionPolicyProjector());
        var turn = new ChatTurnContext
        {
            TurnId = Guid.NewGuid().ToString("N"),
            Request = new ChatRequestDto("What current sources compare .NET hosting guidance?", conversation.Id, 12, null),
            Conversation = conversation,
            History = Array.Empty<ChatMessageDto>()
        };
        var context = new TurnPlanningContext(turn);
        var state = new TurnPlanningState();

        await step.ExecuteAsync(context, state, CancellationToken.None);

        Assert.True(turn.IsFactualPrompt);
        Assert.Equal("low", state.Personalization.ClarificationTolerance);
        Assert.Equal("prefer", state.Personalization.CitationPreference);
        Assert.NotNull(turn.ResolvedUserProfile);
        Assert.Equal("low", turn.ResolvedUserProfile!.ClarificationTolerance);
        Assert.Equal("prefer", turn.ResolvedUserProfile.CitationPreference);
    }
}
