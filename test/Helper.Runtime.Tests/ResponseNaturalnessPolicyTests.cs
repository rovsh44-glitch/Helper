using Helper.Api.Conversation;
using Helper.Api.Hosting;

namespace Helper.Runtime.Tests;

public sealed class ResponseNaturalnessPolicyTests
{
    [Fact]
    public void NextStepComposer_SuppressesGenericNextStep_WhenPressureIsHigh()
    {
        var composer = new NextStepComposer(new ConversationVariationPolicy());
        var context = new ChatTurnContext
        {
            TurnId = "turn-next-step-pressure",
            Request = new ChatRequestDto("help", "conv-next-step-pressure", 12, null),
            Conversation = new ConversationState("conv-next-step-pressure"),
            History = Array.Empty<ChatMessageDto>(),
            CommunicationQualitySnapshot = new CommunicationQualitySnapshot(
                GenericClarificationPressure: 0,
                GenericNextStepPressure: 2,
                MixedLanguagePressure: 0,
                LowStyleFeedbackPressure: 0)
        };

        var shouldRender = composer.ShouldRender(
            context,
            "Short useful answer.",
            "If you want, I can next:");

        Assert.False(shouldRender);
    }

    [Fact]
    public void AnswerShapePolicy_Trims_GenericHereIs_ForGuidanceSeekingTurn()
    {
        var policy = new AnswerShapePolicy();
        var context = new ChatTurnContext
        {
            TurnId = "turn-guidance-naturalness",
            Request = new ChatRequestDto("help me choose the safer rollout", "conv-guidance-naturalness", 12, null),
            Conversation = new ConversationState("conv-guidance-naturalness"),
            History = Array.Empty<ChatMessageDto>(),
            CollaborationIntent = new CollaborationIntentAnalysis(
                IsGuidanceSeeking: true,
                TrustsBestJudgment: false,
                SeeksDelegatedExecution: false,
                PrefersAnswerOverClarification: true,
                HasHardConstraintLanguage: false,
                PrimaryMode: "guidance",
                Signals: new[] { "help me choose" })
        };

        var result = policy.ApplyConversationalNaturalness(context, "Here is the safer rollout path.");

        Assert.Equal("the safer rollout path.", result);
    }
}
