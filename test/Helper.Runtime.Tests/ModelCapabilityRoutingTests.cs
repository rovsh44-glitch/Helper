using Helper.Runtime.Core;
using Helper.Runtime.Infrastructure;

namespace Helper.Runtime.Tests;

public class ModelCapabilityRoutingTests
{
    [Fact]
    public async Task ModelOrchestrator_UsesLongContextOverride_ForLargeContext()
    {
        using var scope = new EnvironmentVariableScope(new Dictionary<string, string?>
        {
            ["HELPER_MODEL_LONG_CONTEXT"] = "long-context-model",
            ["HELPER_MODEL_DEEP_REASONING"] = "deep-reasoning-model",
            ["HELPER_MODEL_REASONING"] = "reasoning-model"
        });

        var orchestrator = new ModelOrchestrator(new AILink());
        var decision = await ((IContextAwareModelOrchestrator)orchestrator).SelectRoutingDecisionAsync(
            new ModelRoutingRequest(
                Prompt: "Compare the tradeoffs and keep the full conversation in view.",
                Intent: IntentType.Research,
                ExecutionMode: "balanced",
                ContextMessageCount: 22,
                ApproximatePromptTokens: 2600));

        Assert.Equal("long-context-model", decision.PreferredModel);
        Assert.Equal("long_context", decision.RouteKey);
        Assert.Contains("long_context", decision.Reasons);
    }

    [Fact]
    public async Task ModelOrchestrator_UsesVerifierOverride_WhenVerificationIsRequired()
    {
        using var scope = new EnvironmentVariableScope(new Dictionary<string, string?>
        {
            ["HELPER_MODEL_VERIFIER"] = "verifier-model",
            ["HELPER_MODEL_CRITIC"] = "critic-model"
        });

        var orchestrator = new ModelOrchestrator(new AILink());
        var decision = await ((IContextAwareModelOrchestrator)orchestrator).SelectRoutingDecisionAsync(
            new ModelRoutingRequest(
                Prompt: "Validate this output against the contract and verify schema correctness.",
                Intent: IntentType.Research,
                ExecutionMode: "deep",
                RequiresVerification: true));

        Assert.Equal("verifier-model", decision.PreferredModel);
        Assert.Equal("verifier", decision.RouteKey);
        Assert.Contains("verification_required", decision.Reasons);
    }
}

