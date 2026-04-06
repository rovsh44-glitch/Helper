using Helper.Api.Conversation;
using Helper.Api.Conversation.InteractionState;
using Helper.Api.Hosting;
using Helper.Runtime.Core;

namespace Helper.Runtime.Tests;

public sealed class InteractionPolicyProjectorTests
{
    [Fact]
    public void Project_Prefers_Answer_First_And_Calm_Tone_For_Frustrated_User()
    {
        var projector = new InteractionPolicyProjector();
        var context = new ChatTurnContext
        {
            TurnId = "interaction-projector-frustrated",
            Request = new ChatRequestDto("Не то", null, 12, null),
            Conversation = new ConversationState("interaction-projector-frustrated"),
            History = Array.Empty<ChatMessageDto>()
        };

        var projection = projector.Project(
            context,
            new InteractionStateSnapshot(
                FrustrationLevel: InteractionSignalLevel.High,
                UrgencyLevel: InteractionSignalLevel.Low,
                OverloadRisk: InteractionSignalLevel.Moderate,
                ReassuranceNeed: InteractionSignalLevel.Moderate,
                ClarificationToleranceShift: -1,
                AssistantPressureRisk: InteractionSignalLevel.Moderate,
                Signals: Array.Empty<string>()));

        Assert.True(projection.PreferAnswerFirst);
        Assert.True(projection.SoftenClarification);
        Assert.True(projection.CompressStructure);
        Assert.True(projection.UseCalmTone);
        Assert.True(projection.IncreaseReassurance);
        Assert.True(projection.NarrowRepairScope);
        Assert.True(projection.SuppressGenericNextStep);
    }

    [Fact]
    public void Project_Stays_Lightweight_For_Neutral_Turn()
    {
        var projector = new InteractionPolicyProjector();
        var context = new ChatTurnContext
        {
            TurnId = "interaction-projector-neutral",
            Request = new ChatRequestDto("Помоги с планом", null, 12, null),
            Conversation = new ConversationState("interaction-projector-neutral"),
            History = Array.Empty<ChatMessageDto>()
        };

        var projection = projector.Project(
            context,
            new InteractionStateSnapshot(
                FrustrationLevel: InteractionSignalLevel.None,
                UrgencyLevel: InteractionSignalLevel.None,
                OverloadRisk: InteractionSignalLevel.None,
                ReassuranceNeed: InteractionSignalLevel.None,
                ClarificationToleranceShift: 0,
                AssistantPressureRisk: InteractionSignalLevel.None,
                Signals: Array.Empty<string>()));

        Assert.False(projection.PreferAnswerFirst);
        Assert.False(projection.SoftenClarification);
        Assert.False(projection.CompressStructure);
        Assert.False(projection.UseCalmTone);
        Assert.False(projection.IncreaseReassurance);
        Assert.False(projection.NarrowRepairScope);
        Assert.False(projection.SuppressGenericNextStep);
    }
}
