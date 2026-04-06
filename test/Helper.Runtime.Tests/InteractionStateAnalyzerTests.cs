using Helper.Api.Conversation;
using Helper.Api.Conversation.InteractionState;
using Helper.Api.Hosting;
using Helper.Runtime.Core;

namespace Helper.Runtime.Tests;

public sealed class InteractionStateAnalyzerTests
{
    [Fact]
    public void Analyze_Detects_Frustration_And_Negative_Clarification_Shift()
    {
        var analyzer = new InteractionStateAnalyzer();
        var context = new ChatTurnContext
        {
            TurnId = "interaction-frustration",
            Request = new ChatRequestDto("Опять не то, не помогло, почему ты снова ушёл не туда?", null, 12, null),
            Conversation = new ConversationState("interaction-frustration")
            {
                ConsecutiveClarificationTurns = 2
            },
            History = Array.Empty<ChatMessageDto>(),
            IsCritiqueApproved = false,
            CritiqueFeedback = "The last repair still missed the point."
        };

        var snapshot = analyzer.Analyze(context);

        Assert.True(snapshot.FrustrationLevel >= InteractionSignalLevel.Moderate);
        Assert.Equal(-1, snapshot.ClarificationToleranceShift);
        Assert.Contains(snapshot.Signals, signal => signal.Contains("clarification_loop", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_Detects_Overload_Reassurance_And_Assistant_Pressure()
    {
        var analyzer = new InteractionStateAnalyzer();
        var context = new ChatTurnContext
        {
            TurnId = "interaction-overload",
            Request = new ChatRequestDto(
                "Мне очень тревожно; срочно помоги, но без длинной лекции: у меня список ограничений, дедлайнов, форматов, и я боюсь ошибиться, поэтому дай короткий безопасный старт: формат, риск, дедлайн, scope, next step, ограничения, приоритеты.",
                null,
                12,
                null),
            Conversation = new ConversationState("interaction-overload"),
            History = Array.Empty<ChatMessageDto>(),
            CollaborationIntent = new CollaborationIntentAnalysis(
                IsGuidanceSeeking: true,
                TrustsBestJudgment: false,
                SeeksDelegatedExecution: true,
                PrefersAnswerOverClarification: true,
                HasHardConstraintLanguage: true,
                PrimaryMode: "execution",
                Signals: new[] { "delegated_execution" }),
            CommunicationQualitySnapshot = new CommunicationQualitySnapshot(
                GenericClarificationPressure: 2,
                GenericNextStepPressure: 0,
                MixedLanguagePressure: 0,
                LowStyleFeedbackPressure: 0)
        };

        var snapshot = analyzer.Analyze(context);

        Assert.True(snapshot.OverloadRisk >= InteractionSignalLevel.Moderate);
        Assert.True(snapshot.ReassuranceNeed >= InteractionSignalLevel.Moderate);
        Assert.True(snapshot.AssistantPressureRisk >= InteractionSignalLevel.Moderate);
    }

    [Fact]
    public void Analyze_Enriches_Optional_Latent_Signals_Without_Requiring_Them()
    {
        var analyzer = new InteractionStateAnalyzer(new FakeLatentInteractionSignalProvider("stress_cluster", "repair_memory"));
        var context = new ChatTurnContext
        {
            TurnId = "interaction-latent",
            Request = new ChatRequestDto("Помоги продолжить", null, 12, null),
            Conversation = new ConversationState("interaction-latent"),
            History = Array.Empty<ChatMessageDto>()
        };

        var snapshot = analyzer.Analyze(context);

        Assert.Contains(snapshot.Signals, signal => signal == "latent:stress_cluster");
        Assert.Contains(snapshot.Signals, signal => signal == "latent:repair_memory");
    }

    private sealed class FakeLatentInteractionSignalProvider(params string[] signals) : ILatentInteractionSignalProvider
    {
        public IReadOnlyList<string> GetSignals(ChatTurnContext context)
        {
            return signals;
        }
    }
}
