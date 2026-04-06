using Helper.Api.Conversation;
using Helper.Api.Hosting;

namespace Helper.Runtime.Tests;

public sealed class MisunderstandingRepairPolicyTests
{
    [Fact]
    public void MisunderstandingRepairPolicy_BuildsTargetedRepairNextStep()
    {
        var policy = new MisunderstandingRepairPolicy();
        var context = new ChatTurnContext
        {
            TurnId = "turn-repair-policy",
            Request = new ChatRequestDto("не это имел в виду, нужен другой scope", "conv-repair-policy", 12, null),
            Conversation = new ConversationState("conv-repair-policy"),
            History = Array.Empty<ChatMessageDto>(),
            IsCritiqueApproved = false
        };

        var kind = policy.Classify(context);
        var nextStep = policy.BuildRepairNextStep(context, isRussian: true);

        Assert.Equal(MisunderstandingRepairKind.Scope, kind);
        Assert.Contains("scope", nextStep!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MisunderstandingRepairPolicy_Uses_Reassuring_Narrow_Scope_Copy_When_InteractionPolicy_Asks_For_It()
    {
        var policy = new MisunderstandingRepairPolicy();
        var context = new ChatTurnContext
        {
            TurnId = "turn-repair-policy-reassuring",
            Request = new ChatRequestDto("тон ответа не тот", "conv-repair-policy-reassuring", 12, null),
            Conversation = new ConversationState("conv-repair-policy-reassuring"),
            History = Array.Empty<ChatMessageDto>(),
            IsCritiqueApproved = false,
            InteractionPolicy = new Helper.Api.Conversation.InteractionState.InteractionPolicyProjection(
                PreferAnswerFirst: true,
                SoftenClarification: true,
                CompressStructure: true,
                UseCalmTone: true,
                IncreaseReassurance: true,
                NarrowRepairScope: true,
                SuppressGenericNextStep: true)
        };

        var nextStep = policy.BuildRepairNextStep(context, isRussian: false);

        Assert.Contains("adjust the delivery without reworking the whole meaning", nextStep!, StringComparison.OrdinalIgnoreCase);
    }
}
