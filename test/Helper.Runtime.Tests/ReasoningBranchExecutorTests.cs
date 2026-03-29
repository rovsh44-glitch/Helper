using Helper.Api.Conversation;
using Helper.Api.Hosting;
using Helper.Api.Backend.ModelGateway;
using Helper.Runtime.Core;
using Helper.Runtime.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Helper.Runtime.Tests;

public class ReasoningBranchExecutorTests
{
    [Fact]
    public async Task ReasoningBranchExecutor_SelectsApprovedCandidate_AndRecordsTrace()
    {
        using var scope = new EnvironmentVariableScope(new Dictionary<string, string?>
        {
            ["HELPER_REASONING_BRANCH_VERIFY_ENABLED"] = "true",
            ["HELPER_REASONING_BRANCH_MAX_CANDIDATES"] = "2"
        });

        var gateway = new Mock<IModelGateway>(MockBehavior.Strict);
        gateway.SetupSequence(x => x.AskAsync(It.IsAny<ModelGatewayRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("not-json")
            .ReturnsAsync("{\"status\":\"ok\",\"count\":3}");

        var resilienceTelemetry = new ChatResilienceTelemetryService();
        var resilience = new ChatResiliencePolicy(NullLogger<ChatResiliencePolicy>.Instance, resilienceTelemetry);
        var executor = new ReasoningBranchExecutor(
            gateway.Object,
            resilience,
            new ReasoningVerifier(new StructuredOutputVerifier(new IReasoningOutputVerifier[] { new JsonSchemaReasoningVerifier() })),
            new ReasoningSelectionPolicy());

        var context = new ChatTurnContext
        {
            TurnId = "branch-select",
            Request = new ChatRequestDto("Return only JSON: {\"status\":\"ok\",\"count\":3}.", null, 8, null),
            Conversation = new ConversationState("conv-branch-select"),
            History = new[] { new ChatMessageDto("user", "Return only JSON: {\"status\":\"ok\",\"count\":3}.", DateTimeOffset.UtcNow) },
            Intent = new IntentAnalysis(IntentType.Unknown, "test-model"),
            ExecutionMode = TurnExecutionMode.Deep,
            ModelCallBudget = 3
        };

        var output = await executor.ExecuteAsync(
            new ChatTurnPreparedInvocation("prompt", "preferred-model", "base-instruction"),
            context,
            CancellationToken.None);

        Assert.Equal("{\"status\":\"ok\",\"count\":3}", output);
        Assert.True(context.ReasoningBranchingApplied);
        Assert.Equal(2, context.ReasoningCandidatesGenerated);
        Assert.Equal(1, context.ReasoningCandidatesRejected);
        Assert.Equal("format_guard", context.SelectedReasoningStrategy);
        Assert.Contains(context.ReasoningCandidateTrace, trace => trace.Contains("baseline:rejected", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(context.ReasoningCandidateTrace, trace => trace.Contains("format_guard:approved", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ChatTurnExecutor_UsesBranchAndVerify_WhenEnabledForSelectedIntent()
    {
        using var scope = new EnvironmentVariableScope(new Dictionary<string, string?>
        {
            ["HELPER_REASONING_BRANCH_VERIFY_ENABLED"] = "true",
            ["HELPER_REASONING_BRANCH_MAX_CANDIDATES"] = "2"
        });

        var ai = new Mock<AILink>("http://localhost:11434", "qwen");
        var gateway = new Mock<IModelGateway>(MockBehavior.Strict);
        gateway.Setup(x => x.WarmAsync(It.IsAny<HelperModelClass>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        gateway.SetupSequence(x => x.AskAsync(It.IsAny<ModelGatewayRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("not-json")
            .ReturnsAsync("{\"status\":\"ok\",\"count\":3}");

        var model = new Mock<IModelOrchestrator>();
        var research = new Mock<IResearchService>();
        var orchestrator = new Mock<IHelperOrchestrator>();
        orchestrator.SetupGet(x => x.Forge).Returns(Mock.Of<IProjectForgeOrchestrator>());

        var resilienceTelemetry = new ChatResilienceTelemetryService();
        var resilience = new ChatResiliencePolicy(NullLogger<ChatResiliencePolicy>.Instance, resilienceTelemetry);
        var branchExecutor = new ReasoningBranchExecutor(
            gateway.Object,
            resilience,
            new ReasoningVerifier(new StructuredOutputVerifier(new IReasoningOutputVerifier[] { new JsonSchemaReasoningVerifier() })),
            new ReasoningSelectionPolicy());
        var executor = new ChatTurnExecutor(
            ai.Object,
            model.Object,
            research.Object,
            new ShortHorizonResearchCache(),
            resilience,
            orchestrator.Object,
            new ApiRuntimeConfig("root", "projects", "library", "logs", "templates", "test-key"),
            new UserProfileService(),
            modelGateway: gateway.Object,
            reasoningBranchExecutor: branchExecutor);

        var context = new ChatTurnContext
        {
            TurnId = "executor-branch",
            Request = new ChatRequestDto("Return only JSON: {\"status\":\"ok\",\"count\":3}.", "conv-executor-branch", 8, null),
            Conversation = new ConversationState("conv-executor-branch"),
            History = new[] { new ChatMessageDto("user", "Return only JSON: {\"status\":\"ok\",\"count\":3}.", DateTimeOffset.UtcNow) },
            Intent = new IntentAnalysis(IntentType.Unknown, "test-model"),
            ExecutionMode = TurnExecutionMode.Deep,
            ModelCallBudget = 3
        };

        await executor.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal("{\"status\":\"ok\",\"count\":3}", context.ExecutionOutput);
        Assert.True(context.ReasoningBranchingApplied);
        Assert.Equal("format_guard", context.SelectedReasoningStrategy);
        Assert.Equal(2, context.ReasoningCandidatesGenerated);
    }
}

