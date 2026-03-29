using Helper.Api.Conversation;
using Helper.Runtime.Core;

namespace Helper.Runtime.Tests;

public sealed class LiveWebRequirementPolicyTests
{
    private readonly LiveWebRequirementPolicy _policy = new();

    [Fact]
    public void Evaluate_ReturnsWebRequired_ForCurrentFinancePrompt()
    {
        var decision = _policy.Evaluate(
            "What is the current price of BTC today?",
            new IntentAnalysis(IntentType.Unknown, "test-model"));

        Assert.Equal(LiveWebRequirementLevel.WebRequired, decision.Requirement);
        Assert.Equal("finance", decision.ReasonCode);
        Assert.Contains("currentness", decision.Signals);
        Assert.Contains("finance", decision.Signals);
    }

    [Fact]
    public void Evaluate_ReturnsWebRequired_ForMutableEntityFactPrompt()
    {
        var decision = _policy.Evaluate(
            "Кто сейчас CEO OpenAI?",
            new IntentAnalysis(IntentType.Unknown, "test-model"));

        Assert.Equal(LiveWebRequirementLevel.WebRequired, decision.Requirement);
        Assert.Equal("mutable_entity_fact", decision.ReasonCode);
        Assert.Contains("leadership", decision.Signals);
    }

    [Fact]
    public void Evaluate_ReturnsWebHelpful_ForProductComparisonPrompt()
    {
        var decision = _policy.Evaluate(
            "Compare the best hosting services for a new startup",
            new IntentAnalysis(IntentType.Unknown, "test-model"));

        Assert.Equal(LiveWebRequirementLevel.WebHelpful, decision.Requirement);
        Assert.Equal("product_comparison", decision.ReasonCode);
        Assert.Contains("comparison", decision.Signals);
        Assert.Contains("product", decision.Signals);
    }

    [Fact]
    public void Evaluate_ReturnsNoWebNeeded_ForEvergreenExplainPrompt()
    {
        var decision = _policy.Evaluate(
            "Объясни что такое TCP handshake",
            new IntentAnalysis(IntentType.Unknown, "test-model"));

        Assert.Equal(LiveWebRequirementLevel.NoWebNeeded, decision.Requirement);
        Assert.Equal("none", decision.ReasonCode);
        Assert.Empty(decision.Signals);
    }
}

