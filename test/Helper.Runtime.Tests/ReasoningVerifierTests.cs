using Helper.Api.Conversation;
using Helper.Api.Hosting;
using Helper.Runtime.Core;
using Moq;
using Microsoft.Extensions.Logging.Abstractions;

namespace Helper.Runtime.Tests;

public class ReasoningVerifierTests
{
    [Fact]
    public async Task JsonSchemaReasoningVerifier_Approves_ValidJsonOutput()
    {
        var verifier = new JsonSchemaReasoningVerifier();
        var context = new ChatTurnContext
        {
            TurnId = "json-pass",
            Request = new ChatRequestDto("Return only JSON: {\"status\":\"ok\",\"count\":3}.", null, 8, null),
            Conversation = new ConversationState("conv-json-pass"),
            History = Array.Empty<ChatMessageDto>(),
            ExecutionOutput = "{\"status\":\"ok\",\"count\":3}"
        };

        var result = await verifier.VerifyAsync(context, CancellationToken.None);

        Assert.Equal(ReasoningVerificationStatus.Approved, result.Status);
        Assert.Contains("JSON", result.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DiscreteTransformVerifier_Rejects_WrongDeterministicSequence()
    {
        var verifier = new DiscreteTransformVerifier();
        var context = new ChatTurnContext
        {
            TurnId = "sequence-fail",
            Request = new ChatRequestDto("Continue the pattern: 2, 4, 8, 16, ?. Answer with the next number only.", null, 8, null),
            Conversation = new ConversationState("conv-sequence-fail"),
            History = Array.Empty<ChatMessageDto>(),
            ExecutionOutput = "31"
        };

        var result = await verifier.VerifyAsync(context, CancellationToken.None);

        Assert.Equal(ReasoningVerificationStatus.Rejected, result.Status);
        Assert.Contains("Expected '32'", result.Summary);
        Assert.NotNull(result.CorrectedContent);
    }

    [Fact]
    public async Task ChatTurnCritic_SkipsLlmCritic_WhenLocalVerifierApproves()
    {
        var critic = new Mock<ICriticService>(MockBehavior.Strict);
        var resilienceTelemetry = new ChatResilienceTelemetryService();
        var resilience = new ChatResiliencePolicy(NullLogger<ChatResiliencePolicy>.Instance, resilienceTelemetry);
        var verifier = new StructuredOutputVerifier(new IReasoningOutputVerifier[] { new JsonSchemaReasoningVerifier() });
        var step = new ChatTurnCritic(
            critic.Object,
            verifier,
            resilience,
            resilienceTelemetry,
            new CriticRiskPolicy(),
            NullLogger<ChatTurnCritic>.Instance);
        var context = new ChatTurnContext
        {
            TurnId = "critic-local-pass",
            Request = new ChatRequestDto("Return only JSON: {\"status\":\"ok\",\"count\":3}.", null, 8, null),
            Conversation = new ConversationState("conv-critic-local-pass"),
            History = Array.Empty<ChatMessageDto>(),
            ExecutionOutput = "{\"status\":\"ok\",\"count\":3}"
        };

        await step.CritiqueAsync(context, CancellationToken.None);

        Assert.True(context.IsCritiqueApproved);
        Assert.True(context.LocalVerificationApplied);
        Assert.True(context.LocalVerificationPassed);
        Assert.Contains("JsonSchemaReasoningVerifier", context.LocalVerificationTrace.Single());
    }

    [Fact]
    public async Task ChatTurnCritic_RejectsDraft_WhenLocalVerifierFails()
    {
        var critic = new Mock<ICriticService>(MockBehavior.Strict);
        var resilienceTelemetry = new ChatResilienceTelemetryService();
        var resilience = new ChatResiliencePolicy(NullLogger<ChatResiliencePolicy>.Instance, resilienceTelemetry);
        var verifier = new StructuredOutputVerifier(new IReasoningOutputVerifier[] { new DiscreteTransformVerifier() });
        var step = new ChatTurnCritic(
            critic.Object,
            verifier,
            resilience,
            resilienceTelemetry,
            new CriticRiskPolicy(),
            NullLogger<ChatTurnCritic>.Instance);
        var context = new ChatTurnContext
        {
            TurnId = "critic-local-fail",
            Request = new ChatRequestDto("Continue the pattern: 2, 4, 8, 16, ?. Answer with the next number only.", null, 8, null),
            Conversation = new ConversationState("conv-critic-local-fail"),
            History = Array.Empty<ChatMessageDto>(),
            ExecutionOutput = "31"
        };

        await step.CritiqueAsync(context, CancellationToken.None);

        Assert.False(context.IsCritiqueApproved);
        Assert.True(context.LocalVerificationApplied);
        Assert.False(context.LocalVerificationPassed);
        Assert.Contains("local_verifier:sequence_fail", context.UncertaintyFlags);
        Assert.Contains("Local verification rejected", context.CorrectedContent ?? string.Empty);
    }
}

