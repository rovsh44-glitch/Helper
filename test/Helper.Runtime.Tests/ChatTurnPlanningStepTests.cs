using Helper.Api.Conversation;
using Helper.Api.Hosting;
using Helper.Runtime.Core;
using Moq;

namespace Helper.Runtime.Tests;

public sealed class ChatTurnPlanningStepTests
{
    [Fact]
    public async Task TurnIntentAnalysisStep_UsesLegacyIntent_When_Flag_Disabled()
    {
        var classifier = new Mock<IIntentClassifier>(MockBehavior.Strict);
        var telemetry = new IntentTelemetryService();
        var featureFlags = new Mock<IFeatureFlags>();
        featureFlags.SetupGet(x => x.IntentV2Enabled).Returns(false);

        var step = new TurnIntentAnalysisStep(classifier.Object, telemetry, featureFlags.Object);
        var context = new TurnPlanningContext(CreateTurn("Summarize this file."));
        var state = new TurnPlanningState();

        await step.ExecuteAsync(context, state, CancellationToken.None);

        Assert.NotNull(state.IntentClassification);
        Assert.Equal(state.IntentClassification!.Analysis, context.Turn.Intent);
        classifier.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task TurnLatencyBudgetStep_Applies_Policy_Result_To_Turn()
    {
        var policy = new Mock<ILatencyBudgetPolicy>();
        policy.Setup(x => x.Resolve(It.IsAny<ChatTurnContext>()))
            .Returns(new TurnLatencyBudget(
                TurnExecutionMode.Fast,
                TurnBudgetProfile.ChatLight,
                TimeSpan.FromSeconds(10),
                ToolCallBudget: 1,
                TokenBudget: 256,
                ModelCallBudget: 1,
                BackgroundBudget: 0,
                Reason: "test"));

        var step = new TurnLatencyBudgetStep(policy.Object);
        var context = new TurnPlanningContext(CreateTurn("fast mode please"));
        var state = new TurnPlanningState
        {
            IntentClassification = new IntentClassification(new IntentAnalysis(IntentType.Unknown, string.Empty), 0.8, "test", Array.Empty<string>())
        };

        await step.ExecuteAsync(context, state, CancellationToken.None);

        Assert.Equal(TurnExecutionMode.Fast, context.Turn.ExecutionMode);
        Assert.Equal(TurnBudgetProfile.ChatLight, context.Turn.BudgetProfile);
        Assert.Equal(1, context.Turn.ToolCallBudget);
        Assert.Equal(256, context.Turn.TokenBudget);
    }

    [Fact]
    public async Task TurnIntentOverrideStep_Promotes_To_Research_For_Explicit_Research_Prompt()
    {
        var clarificationPolicy = new ClarificationPolicy();
        var step = new TurnIntentOverrideStep(clarificationPolicy);
        var context = new TurnPlanningContext(CreateTurn("Research the current .NET hosting guidance and compare sources."));
        var state = new TurnPlanningState
        {
            IntentClassification = new IntentClassification(new IntentAnalysis(IntentType.Unknown, string.Empty), 0.7, "test", Array.Empty<string>())
        };

        await step.ExecuteAsync(context, state, CancellationToken.None);

        Assert.Equal(IntentType.Research, context.Turn.Intent.Intent);
        Assert.Contains("planner:explicit_research_override", context.Turn.IntentSignals);
    }

    private static ChatTurnContext CreateTurn(string message)
    {
        return new ChatTurnContext
        {
            TurnId = Guid.NewGuid().ToString("N"),
            Request = new ChatRequestDto(message, "conv-step", 12, null),
            Conversation = new ConversationState("conv-step"),
            History = Array.Empty<ChatMessageDto>()
        };
    }
}
