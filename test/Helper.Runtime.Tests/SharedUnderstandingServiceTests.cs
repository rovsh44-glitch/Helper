using Helper.Api.Conversation;
using Helper.Api.Hosting;
using Helper.Runtime.Core;

namespace Helper.Runtime.Tests;

public sealed class SharedUnderstandingServiceTests
{
    [Fact]
    public void SharedUnderstandingService_CapturesGuidanceAndTemplateResistance()
    {
        var service = new SharedUnderstandingService();
        var state = new ConversationState("conv-shared-understanding");
        var context = new ChatTurnContext
        {
            TurnId = "turn-shared-understanding",
            Request = new ChatRequestDto("подскажи следующий шаг", state.Id, 12, null),
            Conversation = state,
            History = Array.Empty<ChatMessageDto>(),
            CollaborationIntent = new CollaborationIntentAnalysis(
                IsGuidanceSeeking: true,
                TrustsBestJudgment: false,
                SeeksDelegatedExecution: false,
                PrefersAnswerOverClarification: true,
                HasHardConstraintLanguage: false,
                PrimaryMode: "guidance",
                Signals: new[] { "guidance_seeking" }),
            ForceBestEffort = true,
            StyleTelemetry = new ConversationStyleTelemetry(
                LeadPhraseFingerprint: "if you want, i can next",
                MixedLanguageDetected: false,
                GenericClarificationDetected: false,
                GenericNextStepDetected: true,
                MemoryAckTemplateDetected: false,
                SourceFingerprint: null)
        };

        service.CaptureTurnOutcome(state, context, DateTimeOffset.UtcNow);
        var block = service.BuildContextBlock(state, context);

        Assert.NotNull(state.SharedUnderstanding);
        Assert.True(state.SharedUnderstanding!.PrefersDecisiveAction);
        Assert.True(state.SharedUnderstanding.TemplateResistanceObserved);
        Assert.Contains("Shared understanding:", block);
        Assert.Contains("Avoid canned transitions", block, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SharedUnderstandingService_Persists_Durable_Interaction_Patterns_Without_Transient_Noise()
    {
        var service = new SharedUnderstandingService();
        var state = new ConversationState("conv-shared-understanding-durable");
        var context = new ChatTurnContext
        {
            TurnId = "turn-shared-understanding-durable",
            Request = new ChatRequestDto("Помоги, но коротко и спокойно", state.Id, 12, null),
            Conversation = state,
            History = Array.Empty<ChatMessageDto>(),
            InteractionState = new Helper.Api.Conversation.InteractionState.InteractionStateSnapshot(
                FrustrationLevel: Helper.Api.Conversation.InteractionState.InteractionSignalLevel.Moderate,
                UrgencyLevel: Helper.Api.Conversation.InteractionState.InteractionSignalLevel.Low,
                OverloadRisk: Helper.Api.Conversation.InteractionState.InteractionSignalLevel.Moderate,
                ReassuranceNeed: Helper.Api.Conversation.InteractionState.InteractionSignalLevel.High,
                ClarificationToleranceShift: -1,
                AssistantPressureRisk: Helper.Api.Conversation.InteractionState.InteractionSignalLevel.Moderate,
                Signals: new[] { "interaction.reassurance:lexical" }),
            InteractionPolicy = new Helper.Api.Conversation.InteractionState.InteractionPolicyProjection(
                PreferAnswerFirst: true,
                SoftenClarification: true,
                CompressStructure: true,
                UseCalmTone: true,
                IncreaseReassurance: true,
                NarrowRepairScope: true,
                SuppressGenericNextStep: true)
        };

        service.CaptureTurnOutcome(state, context, DateTimeOffset.UtcNow);
        var block = service.BuildContextBlock(state, context);

        Assert.True(state.SharedUnderstanding!.PrefersConciseReassurance);
        Assert.True(state.SharedUnderstanding.OverloadObservedRecently);
        Assert.True(state.SharedUnderstanding.FrustrationObservedRecently);
        Assert.Contains("calm reassurance", block, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("easier to scan", block, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("most important issue first", block, StringComparison.OrdinalIgnoreCase);
    }
}
