using Helper.Api.Conversation;
using Helper.Api.Backend.Application;
using Helper.Api.Hosting;
using Helper.Runtime.Core;
using Helper.Runtime.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Helper.Runtime.Tests;

public class ConversationE2ETests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task MultiTurnFlow_CoversAmbiguousEntryStreamFeedbackBranchRegenerateAndResume()
    {
        var store = new InMemoryConversationStore();
        var orchestrator = CreateOrchestrator(store);

        var clarification = await orchestrator.CompleteTurnAsync(
            new ChatRequestDto("help", null, 12, null),
            CancellationToken.None);
        Assert.True(
            string.Equals(clarification.GroundingStatus, "clarification_required", StringComparison.OrdinalIgnoreCase) ||
            clarification.Response.Contains("Assumptions:", StringComparison.OrdinalIgnoreCase) ||
            clarification.Response.Contains("Предположения:", StringComparison.OrdinalIgnoreCase) ||
            clarification.Response.Contains("?", StringComparison.Ordinal) ||
            clarification.RequiresConfirmation);

        ChatResponseDto? streamed = null;
        var tokenCount = 0;
        await foreach (var chunk in orchestrator.CompleteTurnStreamAsync(
                           new ChatRequestDto("Исследуй .NET streaming patterns", clarification.ConversationId, 12, null),
                           CancellationToken.None))
        {
            if (chunk.Type == ChatStreamChunkType.Token && !string.IsNullOrWhiteSpace(chunk.Content))
            {
                tokenCount++;
            }

            if (chunk.Type == ChatStreamChunkType.Done)
            {
                streamed = chunk.FinalResponse;
            }
        }

        Assert.True(tokenCount > 0);
        Assert.NotNull(streamed);
        Assert.NotNull(streamed!.TurnId);
        Assert.True((streamed.Sources?.Count ?? 0) >= 2);

        var feedback = new HelpfulnessTelemetryService();
        feedback.Record(streamed.ConversationId, streamed.TurnId, 5, new[] { "useful" }, "good");
        var feedbackSnapshot = feedback.GetConversationSnapshot(streamed.ConversationId);
        Assert.Equal(1, feedbackSnapshot.TotalVotes);
        Assert.True(feedbackSnapshot.AverageRating >= 5);

        var branchCreate = await orchestrator.CreateBranchAsync(
            streamed.ConversationId,
            streamed.TurnId!,
            "alt-path",
            CancellationToken.None);
        Assert.True(branchCreate.Success);

        var branchActivate = await orchestrator.ActivateBranchAsync(
            streamed.ConversationId,
            branchCreate.BranchId,
            CancellationToken.None);
        Assert.True(branchActivate.Success);

        var regenerated = await orchestrator.RegenerateTurnAsync(
            streamed.ConversationId,
            streamed.TurnId!,
            new TurnRegenerateRequestDto(MaxHistory: 12, BranchId: branchCreate.BranchId),
            CancellationToken.None);
        Assert.Equal(branchCreate.BranchId, regenerated.BranchId);

        var state = store.GetOrCreate(streamed.ConversationId);
        lock (state.SyncRoot)
        {
            state.ActiveTurnId = streamed.TurnId;
            state.ActiveTurnUserMessage = "Исследуй .NET streaming patterns";
            state.ActiveTurnStartedAt = DateTimeOffset.UtcNow;
        }

        var resumed = await orchestrator.ResumeActiveTurnAsync(
            streamed.ConversationId,
            new ChatResumeRequestDto(12, null),
            CancellationToken.None);

        Assert.Equal(streamed.ConversationId, resumed.ConversationId);
        Assert.NotNull(resumed.Response);
    }

    private static ChatOrchestrator CreateOrchestrator(InMemoryConversationStore store)
    {
        var model = new Mock<IModelOrchestrator>();
        model.Setup(x => x.AnalyzeIntentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string message, CancellationToken _) =>
            {
                if (message.Contains("исслед", StringComparison.OrdinalIgnoreCase) ||
                    message.Contains("research", StringComparison.OrdinalIgnoreCase))
                {
                    return new IntentAnalysis(IntentType.Research, "test-model");
                }

                return new IntentAnalysis(IntentType.Generate, "test-model");
            });
        model.Setup(x => x.SelectOptimalModelAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("test-model");

        var ai = new Mock<AILink>("http://localhost:11434", "qwen");
        ai.Setup(x => x.AskAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<string>()))
            .ReturnsAsync("Generated response");

        var research = new Mock<IResearchService>();
        research.Setup(x => x.ResearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<Action<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string topic, int _, Action<string>? __, CancellationToken ___) =>
                new ResearchResult(
                    topic,
                    "summary",
                    new List<string> { "https://source-1.test", "https://source-2.test" },
                    new List<string>(),
                    "Research full report",
                    DateTime.UtcNow));

        var critic = new Mock<ICriticService>();
        critic.Setup(x => x.CritiqueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CritiqueResult(true, "ok", null));

        var orchestrator = new Mock<IHelperOrchestrator>();
        orchestrator.SetupGet(x => x.Forge).Returns(Mock.Of<IProjectForgeOrchestrator>());
        orchestrator.Setup(x => x.GenerateProjectAsync(
                It.IsAny<GenerationRequest>(),
                It.IsAny<bool>(),
                It.IsAny<Action<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((GenerationRequest request, bool _, Action<string>? __, CancellationToken ___) =>
                new GenerationResult(
                    true,
                    new List<GeneratedFile>(),
                    request.OutputPath,
                    new List<BuildError>(),
                    TimeSpan.Zero));

        var resilienceTelemetry = new ChatResilienceTelemetryService();
        var resilience = new ChatResiliencePolicy(NullLogger<ChatResiliencePolicy>.Instance, resilienceTelemetry);
        var planner = new ChatTurnPlanner(
            new HybridIntentClassifier(model.Object, resilience, NullLogger<HybridIntentClassifier>.Instance),
            new HybridAmbiguityDetector(),
            new ClarificationPolicy(),
            new IntentTelemetryService());
        var executor = new ChatTurnExecutor(
            ai.Object,
            model.Object,
            research.Object,
            new ShortHorizonResearchCache(),
            resilience,
            orchestrator.Object,
            new ApiRuntimeConfig("root", "projects", "library", "logs", "templates", "test-key"));
        var criticStep = new ChatTurnCritic(critic.Object, resilience, resilienceTelemetry, new CriticRiskPolicy(), NullLogger<ChatTurnCritic>.Instance);
        var finalizer = new ChatTurnFinalizer(new CitationGroundingService());

        var engine = new TurnOrchestrationEngine(
            store,
            planner,
            executor,
            criticStep,
            finalizer,
            new InputRiskScannerV2(),
            new OutputExfiltrationGuardV2(),
            new TurnLifecycleStateMachine(),
            new TurnExecutionStateMachine(),
            new TurnCheckpointManager(),
            new TurnStagePolicy(),
            Mock.Of<IPostTurnAuditScheduler>(x => x.TrySchedule(It.IsAny<ChatTurnContext>(), It.IsAny<ChatResponseDto>()) == false),
            null,
            NullLogger<TurnOrchestrationEngine>.Instance);
        var dispatcher = new ConversationCommandDispatcher(
            engine,
            new ConversationBranchService(store),
            new ConversationCommandIdempotencyStore());
        return new ChatOrchestrator(dispatcher);
    }
}

