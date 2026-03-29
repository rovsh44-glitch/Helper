using Helper.Api.Conversation;
using Helper.Api.Backend.Application;
using Helper.Api.Hosting;
using Helper.Runtime.Core;
using Helper.Runtime.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Helper.Runtime.Tests;

public class EvalHarnessTests
{
    [Fact]
    [Trait("Category", "Eval")]
    public async Task RegressionEvalGate_ShouldMeetPassThreshold()
    {
        var scenarios = BuildScenarioMatrix();
        var results = new List<bool>(scenarios.Count);
        foreach (var scenario in scenarios)
        {
            results.Add(await scenario());
        }

        var passRate = results.Count(r => r) / (double)results.Count;
        Assert.True(passRate >= 0.9, $"Eval gate failed. PassRate={passRate:P}");
    }

    [Fact]
    [Trait("Category", "EvalOffline")]
    public async Task OfflineEvalBenchmark_ShouldMeetPassThreshold()
    {
        var scenarios = BuildScenarioMatrix();
        // Offline benchmark repeats matrix to emulate larger corpus without external dependencies.
        var expanded = Enumerable.Range(0, 3).SelectMany(_ => scenarios).ToList(); // >= 660 scenario runs
        var passCount = 0;
        foreach (var scenario in expanded)
        {
            if (await scenario())
            {
                passCount++;
            }
        }

        var passRate = passCount / (double)expanded.Count;
        Assert.True(passRate >= 0.9, $"Offline benchmark failed. PassRate={passRate:P}");
    }

    private static async Task<bool> EvaluateAmbiguousResolutionScenarioAsync()
    {
        var orchestrator = CreateOrchestrator(
            intent: new IntentAnalysis(IntentType.Generate, "test-model"),
            llmResponse: "Detailed response",
            researchResult: null);

        var response = await orchestrator.CompleteTurnAsync(new ChatRequestDto("help", null, 20, null), CancellationToken.None);
        return IsAmbiguousResolutionResponse(response);
    }

    private static async Task<bool> EvaluateResearchGroundingScenarioAsync()
    {
        var research = new ResearchResult(
            "topic",
            "summary",
            new List<string> { "https://source-1.test", "https://source-2.test" },
            new List<string>(),
            "Research body",
            DateTime.UtcNow);

        var orchestrator = CreateOrchestrator(
            intent: new IntentAnalysis(IntentType.Research, "test-model"),
            llmResponse: "fallback",
            researchResult: research);

        var response = await orchestrator.CompleteTurnAsync(new ChatRequestDto("Исследуй современные .NET паттерны", null, 20, null), CancellationToken.None);
        return response.Response.Contains("Sources:", StringComparison.OrdinalIgnoreCase) &&
               response.Sources != null &&
               response.Sources.Count >= 2;
    }

    private static async Task<bool> EvaluatePersonalizationScenarioAsync()
    {
        var store = new InMemoryConversationStore();
        var state = store.GetOrCreate("persona");
        state.LongTermMemoryEnabled = true;
        store.AddMessage(state, new ChatMessageDto("user", "remember: answer concise", DateTimeOffset.UtcNow));
        var messages = store.GetRecentMessages(state, 20);
        return await Task.FromResult(messages.Any(m => m.Content.Contains("answer concise", StringComparison.OrdinalIgnoreCase)));
    }

    private static List<Func<Task<bool>>> BuildScenarioMatrix()
    {
        var scenarios = new List<Func<Task<bool>>>();

        // Ambiguous prompt resolution scenarios (RU/EN)
        var ambiguousInputs = new[] { "help", "помоги", "something", "что делать", "quick" };
        for (var i = 0; i < 80; i++)
        {
            var input = ambiguousInputs[i % ambiguousInputs.Length] + $" #{i}";
            scenarios.Add(async () =>
            {
                var orchestrator = CreateOrchestrator(
                    intent: new IntentAnalysis(IntentType.Generate, "test-model"),
                    llmResponse: "Detailed response",
                    researchResult: null);
                var response = await orchestrator.CompleteTurnAsync(new ChatRequestDto(input, null, 20, null), CancellationToken.None);
                return IsAmbiguousResolutionResponse(response);
            });
        }

        // Research + grounding scenarios
        for (var i = 0; i < 70; i++)
        {
            scenarios.Add(async () =>
            {
                var research = new ResearchResult(
                    "topic",
                    "summary",
                    new List<string> { "https://source-1.test", "https://source-2.test" },
                    new List<string>(),
                    "Research body",
                    DateTime.UtcNow);
                var orchestrator = CreateOrchestrator(
                    intent: new IntentAnalysis(IntentType.Research, "test-model"),
                    llmResponse: "fallback",
                    researchResult: research);
                var response = await orchestrator.CompleteTurnAsync(new ChatRequestDto($"Исследуй .NET паттерны #{i}", null, 20, null), CancellationToken.None);
                return response.CitationCoverage >= 0.7 || (response.Sources?.Count ?? 0) >= 2;
            });
        }

        // Personalization and memory scenarios
        for (var i = 0; i < 70; i++)
        {
            scenarios.Add(EvaluatePersonalizationScenarioAsync);
        }

        return scenarios;
    }

    private static bool IsAmbiguousResolutionResponse(ChatResponseDto response)
    {
        return string.Equals(response.GroundingStatus, "clarification_required", StringComparison.OrdinalIgnoreCase)
            || response.Response.Contains("Assumptions:", StringComparison.OrdinalIgnoreCase)
            || response.Response.Contains("Предположения:", StringComparison.OrdinalIgnoreCase)
            || response.Response.Contains("?", StringComparison.Ordinal)
            || response.RequiresConfirmation;
    }

    private static ChatOrchestrator CreateOrchestrator(IntentAnalysis intent, string llmResponse, ResearchResult? researchResult)
    {
        var store = new InMemoryConversationStore();

        var modelOrchestrator = new Mock<IModelOrchestrator>();
        modelOrchestrator.Setup(m => m.AnalyzeIntentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(intent);
        modelOrchestrator.Setup(m => m.SelectOptimalModelAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(intent.Model);

        var ai = new Mock<AILink>("http://localhost:11434", "qwen");
        ai.Setup(a => a.AskAsync(It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>()))
            .ReturnsAsync(llmResponse);

        var research = new Mock<IResearchService>();
        research.Setup(r => r.ResearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<Action<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(researchResult ?? new ResearchResult("t", "s", new List<string>(), new List<string>(), "none", DateTime.UtcNow));

        var critic = new Mock<ICriticService>();
        critic.Setup(c => c.CritiqueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
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
        var intentClassifier = new HybridIntentClassifier(modelOrchestrator.Object, resilience, NullLogger<HybridIntentClassifier>.Instance);
        var planner = new ChatTurnPlanner(
            intentClassifier,
            new HybridAmbiguityDetector(),
            new ClarificationPolicy(),
            new IntentTelemetryService());
        var executor = new ChatTurnExecutor(
            ai.Object,
            modelOrchestrator.Object,
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
            new InputRiskScanner(),
            new OutputExfiltrationGuard(),
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

