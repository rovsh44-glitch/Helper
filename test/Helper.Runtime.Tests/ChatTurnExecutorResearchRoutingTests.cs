using Helper.Api.Backend.Application;
using Helper.Api.Backend.Configuration;
using Helper.Api.Conversation;
using Helper.Api.Hosting;
using Helper.Runtime.Core;
using Helper.Runtime.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Helper.Runtime.Tests;

public sealed class ChatTurnExecutorResearchRoutingTests
{
    [Fact]
    public async Task ExecuteAsync_UsesWebSearchOrchestratorCache_ForRepeatedResearchPrompt()
    {
        var ai = new Mock<AILink>("http://localhost:11434", "qwen");
        var model = new Mock<IModelOrchestrator>();
        var research = new Mock<IResearchService>();
        research
            .Setup(service => service.ResearchAsync(
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<Action<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string topic, int _, Action<string>? __, CancellationToken ___) => new ResearchResult(
                topic,
                "summary",
                new List<string> { "https://example.org/report" },
                new List<string>(),
                "Research report",
                DateTime.UtcNow,
                EvidenceItems: new[]
                {
                    new ResearchEvidenceItem(1, "https://example.org/report", "Report", "Evidence snippet")
                }));

        var orchestrator = new Mock<IHelperOrchestrator>();
        orchestrator.SetupGet(x => x.Forge).Returns(Mock.Of<IProjectForgeOrchestrator>());

        var resilienceTelemetry = new ChatResilienceTelemetryService();
        var resilience = new ChatResiliencePolicy(NullLogger<ChatResiliencePolicy>.Instance, resilienceTelemetry);
        var executor = new ChatTurnExecutor(
            ai.Object,
            model.Object,
            research.Object,
            new ShortHorizonResearchCache(),
            resilience,
            orchestrator.Object,
            new ApiRuntimeConfig("root", "projects", "library", "logs", "templates", "test-key"),
            sourceNormalizer: new SourceNormalizationService());

        var first = CreateContext("latest .NET observability guidance");
        var second = CreateContext("latest .NET observability guidance");

        await executor.ExecuteAsync(first, CancellationToken.None);
        await executor.ExecuteAsync(second, CancellationToken.None);

        Assert.Equal("Research report", first.ExecutionOutput);
        Assert.Equal("Research report", second.ExecutionOutput);
        Assert.Contains("web_search:live_fetch", first.IntentSignals);
        Assert.Contains("web_search:cache_hit", second.IntentSignals);
        Assert.Single(second.Sources);
        Assert.Single(second.ResearchEvidenceItems);
        research.Verify(service => service.ResearchAsync(
            It.IsAny<string>(),
            It.IsAny<int>(),
            It.IsAny<Action<string>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private static ChatTurnContext CreateContext(string message)
    {
        return new ChatTurnContext
        {
            TurnId = Guid.NewGuid().ToString("N"),
            Request = new ChatRequestDto(message, "conv-research-route", 10, null),
            Conversation = new ConversationState("conv-research-route"),
            History = Array.Empty<ChatMessageDto>(),
            Intent = new IntentAnalysis(IntentType.Research, "test-model")
        };
    }
}

