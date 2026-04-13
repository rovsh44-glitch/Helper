using Helper.Api.Conversation;
using Helper.Api.Backend.Application;
using Helper.Api.Backend.ModelGateway;
using Helper.Api.Hosting;
using Helper.Runtime.Core;
using Helper.Runtime.Infrastructure;
using Helper.Runtime.WebResearch;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Helper.Runtime.Tests;

public partial class ConversationRuntimeTests
{
    [Fact]
    public async Task TurnExecutionStageRunner_PreservesImmediateOutput_WithoutDuplicatingStreamedToken()
    {
        var store = new InMemoryConversationStore();
        var state = store.GetOrCreate("conv-immediate-stream");
        var context = new ChatTurnContext
        {
            TurnId = "turn-immediate-stream",
            Request = new ChatRequestDto("Исследуй статью", "conv-immediate-stream", 12, null),
            Conversation = state,
            History = Array.Empty<ChatMessageDto>(),
            LifecycleState = TurnLifecycleState.Understand,
            ExecutionState = TurnExecutionState.Planned,
            ExecutionOutput = "Research request: https://example.org/article",
            Intent = new IntentAnalysis(IntentType.Research, "test")
        };

        var runner = new TurnExecutionStageRunner(
            store,
            Mock.Of<IChatTurnPlanner>(),
            new StubChatTurnExecutor(new TokenChunk(
                ChatStreamChunkType.Token,
                context.ExecutionOutput,
                1,
                DateTimeOffset.UtcNow)),
            Mock.Of<IChatTurnCritic>(),
            Mock.Of<IChatTurnFinalizer>(),
            Mock.Of<IOutputExfiltrationGuard>(),
            new TurnLifecycleStateMachine(),
            new TurnExecutionStateMachine(),
            new TurnCheckpointManager(),
            Mock.Of<ITurnStagePolicy>(),
            logger: NullLogger<TurnExecutionStageRunner>.Instance);

        var emitted = new List<TokenChunk>();
        await foreach (var chunk in runner.ExecuteStreamAsync(state, context, "main", CancellationToken.None))
        {
            emitted.Add(chunk);
        }

        Assert.Single(emitted);
        Assert.Equal("Research request: https://example.org/article", context.ExecutionOutput);
    }

    [Fact]
    public async Task ChatTurnPlanner_RequestsConfirmation_ForRiskyPrompt()
    {
        var model = new Mock<IModelOrchestrator>();
        model.Setup(x => x.AnalyzeIntentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IntentAnalysis(IntentType.Unknown, "test-model"));

        var resilienceTelemetry = new ChatResilienceTelemetryService();
        var resilience = new ChatResiliencePolicy(NullLogger<ChatResiliencePolicy>.Instance, resilienceTelemetry);
        var classifier = new HybridIntentClassifier(model.Object, resilience, NullLogger<HybridIntentClassifier>.Instance);
        var planner = new ChatTurnPlanner(
            classifier,
            new HybridAmbiguityDetector(),
            new ClarificationPolicy(),
            new IntentTelemetryService());
        var context = new ChatTurnContext
        {
            TurnId = "risk-turn",
            Request = new ChatRequestDto("удали все временные файлы", "conv-risk", 12, null),
            Conversation = new ConversationState("conv-risk"),
            History = Array.Empty<ChatMessageDto>()
        };

        await planner.PlanAsync(context, CancellationToken.None);

        Assert.True(context.RequiresClarification);
        Assert.True(context.RequiresConfirmation);
        Assert.Equal("ru", context.ResolvedTurnLanguage);
        Assert.Contains("разруш", context.ClarifyingQuestion ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("destructive", context.ClarifyingQuestion ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ChatTurnPlanner_UsesSoftBestEffortEntry_ForUnderspecifiedPrompt()
    {
        var classifier = new Mock<IIntentClassifier>();
        classifier
            .Setup(x => x.ClassifyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IntentClassification(
                new IntentAnalysis(IntentType.Generate, "test-model"),
                0.82,
                "test",
                Array.Empty<string>()));

        var planner = new ChatTurnPlanner(
            classifier.Object,
            new HybridAmbiguityDetector(),
            new ClarificationPolicy(),
            new IntentTelemetryService());

        var context = new ChatTurnContext
        {
            TurnId = "soft-clarify-turn",
            Request = new ChatRequestDto("помоги", "conv-soft-clarify", 12, null),
            Conversation = new ConversationState("conv-soft-clarify"),
            History = Array.Empty<ChatMessageDto>()
        };

        await planner.PlanAsync(context, CancellationToken.None);

        Assert.False(context.RequiresClarification);
        Assert.False(context.RequiresConfirmation);
        Assert.True(context.ForceBestEffort);
        Assert.Equal("ru", context.ResolvedTurnLanguage);
        Assert.Contains("кратко", context.ClarifyingQuestion ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("подроб", context.ClarifyingQuestion ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("план", context.ClarifyingQuestion ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("предполож", context.ClarifyingQuestion ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("soft_best_effort_entry", context.UncertaintyFlags);
        Assert.DoesNotContain("Уточните основную цель", context.ClarifyingQuestion ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ClarificationPolicy_OffersHelpfulBranching_ForLowConfidence()
    {
        var policy = new ClarificationPolicy();
        var question = policy.BuildLowConfidenceQuestion(
            new IntentClassification(
                new IntentAnalysis(IntentType.Unknown, "test-model"),
                0.32,
                "test",
                Array.Empty<string>()),
            attemptNumber: 1,
            resolvedLanguage: "en");

        Assert.Contains("briefly", question, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("in depth", question, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("plan", question, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("best-effort", question, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ClarificationPolicy_UsesHardStop_ForSafetyConfirmation()
    {
        var policy = new ClarificationPolicy();
        var question = policy.BuildQuestion(
            new AmbiguityDecision(true, AmbiguityType.SafetyConfirmation, 0.96, "Potentially destructive intent without explicit confirmation."),
            new IntentClassification(
                new IntentAnalysis(IntentType.Generate, "test-model"),
                0.88,
                "test",
                Array.Empty<string>()),
            attemptNumber: 1,
            resolvedLanguage: "ru");

        Assert.Contains("подтверд", question, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("кратко", question, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("предполож", question, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ChatTurnFinalizer_UsesSoftBestEffortEnvelope_ForAmbiguousTurn()
    {
        var finalizer = new ChatTurnFinalizer(new CitationGroundingService(), ResponseComposerServiceFactory.CreateDefault());
        var context = new ChatTurnContext
        {
            TurnId = "turn-soft-best-effort",
            Request = new ChatRequestDto("помоги", null, 10, null),
            Conversation = new ConversationState("conv-soft-best-effort"),
            History = Array.Empty<ChatMessageDto>(),
            ExecutionOutput = "Начните с фиксации цели и одного ближайшего шага.",
            IsCritiqueApproved = true,
            ForceBestEffort = true,
            ForceBestEffortReason = "Запрос пока слишком общий, поэтому беру самый полезный практический старт.",
            ClarifyingQuestion = "Чтобы попасть точнее, подскажите направление: кратко, подробно или в виде плана? Если хотите, могу начать с разумных допущений и явно пометить предположения.",
            AmbiguityType = nameof(AmbiguityType.Goal),
            ResolvedTurnLanguage = "ru"
        };
        context.UncertaintyFlags.Add("soft_best_effort_entry");

        await finalizer.FinalizeAsync(context, CancellationToken.None);

        Assert.Contains("Дам полезный старт", context.FinalResponse, StringComparison.Ordinal);
        Assert.Contains("Предположения:", context.FinalResponse, StringComparison.Ordinal);
        Assert.Contains("Если захотите скорректировать направление", context.FinalResponse, StringComparison.Ordinal);
        Assert.DoesNotContain("Режим разумных допущений:", context.FinalResponse, StringComparison.Ordinal);
        Assert.True(context.Confidence <= 0.5);
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private sealed class StubChatTurnExecutor : IChatTurnExecutor
    {
        private readonly IReadOnlyList<TokenChunk> _chunks;

        public StubChatTurnExecutor(params TokenChunk[] chunks)
        {
            _chunks = chunks;
        }

        public Task ExecuteAsync(ChatTurnContext context, CancellationToken ct)
            => Task.CompletedTask;

        public async IAsyncEnumerable<TokenChunk> ExecuteStreamAsync(ChatTurnContext context, [EnumeratorCancellation] CancellationToken ct)
        {
            foreach (var chunk in _chunks)
            {
                yield return chunk;
                await Task.Yield();
            }
        }
    }

    [Fact]
    public async Task ChatTurnPlanner_ForcesBestEffort_WhenClarificationBudgetExceeded()
    {
        var model = new Mock<IModelOrchestrator>();
        model.Setup(x => x.AnalyzeIntentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IntentAnalysis(IntentType.Generate, "test-model"));

        var resilienceTelemetry = new ChatResilienceTelemetryService();
        var resilience = new ChatResiliencePolicy(NullLogger<ChatResiliencePolicy>.Instance, resilienceTelemetry);
        var classifier = new HybridIntentClassifier(model.Object, resilience, NullLogger<HybridIntentClassifier>.Instance);
        var policy = new ClarificationPolicy(maxClarificationTurns: 1);
        var planner = new ChatTurnPlanner(
            classifier,
            new HybridAmbiguityDetector(),
            policy,
            new IntentTelemetryService());

        var state = new ConversationState("conv-clarify-budget")
        {
            ConsecutiveClarificationTurns = 1
        };
        var context = new ChatTurnContext
        {
            TurnId = "clarify-budget-turn",
            Request = new ChatRequestDto("help", "conv-clarify-budget", 12, null),
            Conversation = state,
            History = Array.Empty<ChatMessageDto>()
        };

        await planner.PlanAsync(context, CancellationToken.None);

        Assert.False(context.RequiresClarification);
        Assert.True(context.ForceBestEffort);
        Assert.Contains("clarification_budget_exhausted", context.UncertaintyFlags);
    }

    [Fact]
    public async Task ChatTurnPlanner_TriggersAssumptionCheck_ForUnconstrainedRiskyAction()
    {
        var model = new Mock<IModelOrchestrator>();
        model.Setup(x => x.AnalyzeIntentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IntentAnalysis(IntentType.Generate, "test-model"));

        var resilienceTelemetry = new ChatResilienceTelemetryService();
        var resilience = new ChatResiliencePolicy(NullLogger<ChatResiliencePolicy>.Instance, resilienceTelemetry);
        var classifier = new HybridIntentClassifier(model.Object, resilience, NullLogger<HybridIntentClassifier>.Instance);
        var planner = new ChatTurnPlanner(
            classifier,
            new HybridAmbiguityDetector(),
            new ClarificationPolicy(),
            new IntentTelemetryService(),
            new LatencyBudgetPolicy(),
            new AssumptionCheckPolicy());

        var context = new ChatTurnContext
        {
            TurnId = "assumption-check-turn",
            Request = new ChatRequestDto("Deploy release to production now", "conv-assumption", 12, null),
            Conversation = new ConversationState("conv-assumption"),
            History = Array.Empty<ChatMessageDto>()
        };

        await planner.PlanAsync(context, CancellationToken.None);

        Assert.True(context.RequiresClarification);
        Assert.True(context.RequiresConfirmation);
        Assert.Contains("safe mode", context.ClarifyingQuestion ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("assumption_check_required", context.UncertaintyFlags);
    }

    [Fact]
    public void AssumptionCheckPolicy_DoesNotTrigger_ForResearchPromptAboutProduction()
    {
        var policy = new AssumptionCheckPolicy();
        var context = new ChatTurnContext
        {
            TurnId = "assumption-research-turn",
            Request = new ChatRequestDto("Исследуй подходы к tracing и метрикам в проде", "conv-assumption-research", 12, null),
            Conversation = new ConversationState("conv-assumption-research"),
            History = Array.Empty<ChatMessageDto>(),
            Intent = new IntentAnalysis(IntentType.Research, "test-model")
        };

        var decision = policy.Evaluate(context);

        Assert.False(decision.RequiresClarification);
        Assert.True(string.IsNullOrWhiteSpace(decision.Flag));
    }

    [Fact]
    public async Task ChatTurnPlanner_AppliesFastModeBudget_FromPromptHints()
    {
        var model = new Mock<IModelOrchestrator>();
        model.Setup(x => x.AnalyzeIntentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IntentAnalysis(IntentType.Generate, "test-model"));

        var resilienceTelemetry = new ChatResilienceTelemetryService();
        var resilience = new ChatResiliencePolicy(NullLogger<ChatResiliencePolicy>.Instance, resilienceTelemetry);
        var classifier = new HybridIntentClassifier(model.Object, resilience, NullLogger<HybridIntentClassifier>.Instance);
        var planner = new ChatTurnPlanner(
            classifier,
            new HybridAmbiguityDetector(),
            new ClarificationPolicy(),
            new IntentTelemetryService(),
            new LatencyBudgetPolicy());

        var context = new ChatTurnContext
        {
            TurnId = "fast-budget-turn",
            Request = new ChatRequestDto("Быстро и кратко объясни difference между REST и gRPC", "conv-fast-budget", 12, null),
            Conversation = new ConversationState("conv-fast-budget"),
            History = Array.Empty<ChatMessageDto>()
        };

        await planner.PlanAsync(context, CancellationToken.None);

        Assert.Equal(TurnExecutionMode.Fast, context.ExecutionMode);
        Assert.True(context.ToolCallBudget <= 1);
        Assert.True(context.TokenBudget <= 500);
        Assert.True(context.TimeBudget <= TimeSpan.FromSeconds(12));
    }

    [Fact]
    public async Task ChatTurnPlanner_UsesLegacyIntent_WhenIntentV2FlagIsDisabled()
    {
        var model = new Mock<IModelOrchestrator>();
        model.Setup(x => x.AnalyzeIntentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IntentAnalysis(IntentType.Research, "test-model"));

        var resilienceTelemetry = new ChatResilienceTelemetryService();
        var resilience = new ChatResiliencePolicy(NullLogger<ChatResiliencePolicy>.Instance, resilienceTelemetry);
        var classifier = new HybridIntentClassifier(model.Object, resilience, NullLogger<HybridIntentClassifier>.Instance);
        var flags = new Mock<IFeatureFlags>();
        flags.SetupGet(x => x.IntentV2Enabled).Returns(false);

        var planner = new ChatTurnPlanner(
            classifier,
            new HybridAmbiguityDetector(),
            new ClarificationPolicy(),
            new IntentTelemetryService(),
            new LatencyBudgetPolicy(),
            new AssumptionCheckPolicy(),
            flags.Object);

        var context = new ChatTurnContext
        {
            TurnId = "legacy-intent-turn",
            Request = new ChatRequestDto("Напиши краткий план релиза", "conv-legacy-intent", 12, null),
            Conversation = new ConversationState("conv-legacy-intent"),
            History = Array.Empty<ChatMessageDto>()
        };

        await planner.PlanAsync(context, CancellationToken.None);

        Assert.Equal("legacy", context.IntentSource);
        Assert.Contains("legacy:intent_v1", context.IntentSignals);
    }

    [Fact]
    public async Task ChatTurnPlanner_ForcesResearchExecution_ForExplicitResearchPrompt()
    {
        var classifier = new Mock<IIntentClassifier>();
        classifier
            .Setup(x => x.ClassifyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IntentClassification(
                new IntentAnalysis(IntentType.Unknown, "test-model"),
                0.35,
                "model_first",
                new[] { "test:low_confidence_unknown" }));

        var planner = new ChatTurnPlanner(
            classifier.Object,
            new HybridAmbiguityDetector(),
            new ClarificationPolicy(),
            new IntentTelemetryService(),
            new LatencyBudgetPolicy(),
            new AssumptionCheckPolicy());

        var context = new ChatTurnContext
        {
            TurnId = "planner-research-override-turn",
            Request = new ChatRequestDto("Собери источники по .NET observability", "conv-research-override", 12, null),
            Conversation = new ConversationState("conv-research-override"),
            History = Array.Empty<ChatMessageDto>()
        };

        await planner.PlanAsync(context, CancellationToken.None);

        Assert.False(context.RequiresClarification);
        Assert.Equal(IntentType.Research, context.Intent.Intent);
        Assert.Contains("research_intent_forced_from_prompt", context.UncertaintyFlags);
    }

    [Fact]
    public async Task ChatTurnPlanner_ForcesGenerateIntent_ForExplicitGoldenTemplatePrompt()
    {
        var classifier = new Mock<IIntentClassifier>();
        classifier
            .Setup(x => x.ClassifyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IntentClassification(
                new IntentAnalysis(IntentType.Unknown, "test-model"),
                0.35,
                "model_first",
                new[] { "test:low_confidence_unknown" }));

        var planner = new ChatTurnPlanner(
            classifier.Object,
            new HybridAmbiguityDetector(),
            new ClarificationPolicy(),
            new IntentTelemetryService(),
            new LatencyBudgetPolicy(),
            new AssumptionCheckPolicy());

        var context = new ChatTurnContext
        {
            TurnId = "planner-golden-override-turn",
            Request = new ChatRequestDto("приложение шахматы wpf desktop", "conv-golden-override", 12, null),
            Conversation = new ConversationState("conv-golden-override"),
            History = Array.Empty<ChatMessageDto>()
        };

        await planner.PlanAsync(context, CancellationToken.None);

        Assert.False(context.RequiresClarification);
        Assert.Equal(IntentType.Generate, context.Intent.Intent);
        Assert.True(context.IntentConfidence >= 0.9);
        Assert.Contains("planner:explicit_golden_template_override", context.IntentSignals);
        Assert.Contains("golden_template_intent_forced_from_prompt", context.UncertaintyFlags);
    }

    [Fact]
    public async Task ChatTurnPlanner_PromotesCurrentnessPrompt_ToResearchViaLiveWebPolicy()
    {
        var classifier = new Mock<IIntentClassifier>();
        classifier
            .Setup(x => x.ClassifyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IntentClassification(
                new IntentAnalysis(IntentType.Unknown, "test-model"),
                0.41,
                "model_first",
                new[] { "test:unknown" }));

        var planner = new ChatTurnPlanner(
            classifier.Object,
            new HybridAmbiguityDetector(),
            new ClarificationPolicy(),
            new IntentTelemetryService(),
            latencyBudgetPolicy: new LatencyBudgetPolicy(),
            assumptionCheckPolicy: new AssumptionCheckPolicy(),
            liveWebRequirementPolicy: new LiveWebRequirementPolicy());

        var context = new ChatTurnContext
        {
            TurnId = "planner-live-web-route-turn",
            Request = new ChatRequestDto("What is the current price of BTC today?", "conv-live-web-route", 12, null),
            Conversation = new ConversationState("conv-live-web-route"),
            History = Array.Empty<ChatMessageDto>()
        };

        await planner.PlanAsync(context, CancellationToken.None);

        Assert.Equal(IntentType.Research, context.Intent.Intent);
        Assert.Equal("web_required", context.ResolvedLiveWebRequirement);
        Assert.Equal("finance", context.ResolvedLiveWebReason);
        Assert.Contains("planner:live_web_required", context.IntentSignals);
        Assert.Contains("live_web_required_route_override", context.UncertaintyFlags);
        Assert.True(context.IsFactualPrompt);
    }

    [Fact]
    public async Task ChatTurnPlanner_PromotesLocalFirstBenchmarkLocalOnly_ToResearchWithoutLiveWeb()
    {
        var classifier = new Mock<IIntentClassifier>();
        classifier
            .Setup(x => x.ClassifyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IntentClassification(
                new IntentAnalysis(IntentType.Unknown, "test-model"),
                0.41,
                "model_first",
                new[] { "test:unknown" }));

        var planner = new ChatTurnPlanner(
            classifier.Object,
            new HybridAmbiguityDetector(),
            new ClarificationPolicy(),
            new IntentTelemetryService(),
            latencyBudgetPolicy: new LatencyBudgetPolicy(),
            assumptionCheckPolicy: new AssumptionCheckPolicy(),
            liveWebRequirementPolicy: new LiveWebRequirementPolicy(),
            localFirstBenchmarkPolicy: new LocalFirstBenchmarkPolicy());

        var context = new ChatTurnContext
        {
            TurnId = "planner-benchmark-local-only-turn",
            Request = new ChatRequestDto(
                "Объясни простыми словами, чем отличаются аэробные и анаэробные нагрузки.",
                "conv-benchmark-local-only",
                12,
                """
You are being evaluated as a local-first librarian-research assistant.
Formatting:
## Local Findings
## Web Findings
## Sources
## Analysis
## Conclusion
## Opinion
Answer in Russian.
"""),
            Conversation = new ConversationState("conv-benchmark-local-only"),
            History = Array.Empty<ChatMessageDto>()
        };

        await planner.PlanAsync(context, CancellationToken.None);

        Assert.True(context.IsLocalFirstBenchmarkTurn);
        Assert.Equal(LocalFirstBenchmarkMode.LocalOnly, context.LocalFirstBenchmarkMode);
        Assert.Equal(IntentType.Research, context.Intent.Intent);
        Assert.Equal("no_web_needed", context.ResolvedLiveWebRequirement);
        Assert.Contains("planner:benchmark_local_only", context.IntentSignals);
    }

    [Fact]
    public async Task ChatTurnPlanner_PromotesLocalFirstBenchmarkSparseCase_ToHelpfulWebResearch()
    {
        var classifier = new Mock<IIntentClassifier>();
        classifier
            .Setup(x => x.ClassifyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IntentClassification(
                new IntentAnalysis(IntentType.Unknown, "test-model"),
                0.41,
                "model_first",
                new[] { "test:unknown" }));

        var planner = new ChatTurnPlanner(
            classifier.Object,
            new HybridAmbiguityDetector(),
            new ClarificationPolicy(),
            new IntentTelemetryService(),
            latencyBudgetPolicy: new LatencyBudgetPolicy(),
            assumptionCheckPolicy: new AssumptionCheckPolicy(),
            liveWebRequirementPolicy: new LiveWebRequirementPolicy(),
            localFirstBenchmarkPolicy: new LocalFirstBenchmarkPolicy());

        var context = new ChatTurnContext
        {
            TurnId = "planner-benchmark-sparse-turn",
            Request = new ChatRequestDto(
                "Объясни, насколько убедительны данные о пользе терапии красным светом для восстановления после тренировок.",
                "conv-benchmark-sparse",
                12,
                """
You are being evaluated as a local-first librarian-research assistant.
Workflow:
- Start with local baseline knowledge, use live web cautiously when needed, and explicitly state uncertainty and evidence limits. Aim for at least 2 distinct web sources if they exist.
Formatting:
## Local Findings
## Web Findings
## Sources
## Analysis
## Conclusion
## Opinion
Answer in Russian.
"""),
            Conversation = new ConversationState("conv-benchmark-sparse"),
            History = Array.Empty<ChatMessageDto>()
        };

        await planner.PlanAsync(context, CancellationToken.None);

        Assert.True(context.IsLocalFirstBenchmarkTurn);
        Assert.Equal(LocalFirstBenchmarkMode.WebRecommended, context.LocalFirstBenchmarkMode);
        Assert.Equal(IntentType.Research, context.Intent.Intent);
        Assert.Equal("web_helpful", context.ResolvedLiveWebRequirement);
        Assert.Equal("benchmark_recommended_web", context.ResolvedLiveWebReason);
        Assert.Contains("planner:benchmark_web_recommended", context.IntentSignals);
    }

    [Fact]
    public async Task ChatTurnPlanner_PromotesLocalFirstBenchmarkSupplementCase_ToRequiredWebResearch()
    {
        var classifier = new Mock<IIntentClassifier>();
        classifier
            .Setup(x => x.ClassifyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IntentClassification(
                new IntentAnalysis(IntentType.Unknown, "test-model"),
                0.41,
                "model_first",
                new[] { "test:unknown" }));

        var planner = new ChatTurnPlanner(
            classifier.Object,
            new HybridAmbiguityDetector(),
            new ClarificationPolicy(),
            new IntentTelemetryService(),
            latencyBudgetPolicy: new LatencyBudgetPolicy(),
            assumptionCheckPolicy: new AssumptionCheckPolicy(),
            liveWebRequirementPolicy: new LiveWebRequirementPolicy(),
            localFirstBenchmarkPolicy: new LocalFirstBenchmarkPolicy());

        var context = new ChatTurnContext
        {
            TurnId = "planner-benchmark-supplement-turn",
            Request = new ChatRequestDto(
                "Объясни, как обычно строят профилактику мигрени, а затем проверь, что изменилось или уточнилось в последних клинических рекомендациях.",
                "conv-benchmark-supplement",
                12,
                """
You are being evaluated as a local-first librarian-research assistant.
Workflow:
- Start with local knowledge and local library context, then supplement or verify it with live web evidence before concluding. Aim for at least 2 distinct web sources when web is used.
Formatting:
## Local Findings
## Web Findings
## Sources
## Analysis
## Conclusion
## Opinion
Answer in Russian.
"""),
            Conversation = new ConversationState("conv-benchmark-supplement"),
            History = Array.Empty<ChatMessageDto>()
        };

        await planner.PlanAsync(context, CancellationToken.None);

        Assert.True(context.IsLocalFirstBenchmarkTurn);
        Assert.Equal(LocalFirstBenchmarkMode.WebRequired, context.LocalFirstBenchmarkMode);
        Assert.Equal(IntentType.Research, context.Intent.Intent);
        Assert.Equal("web_required", context.ResolvedLiveWebRequirement);
        Assert.Equal("benchmark_mandatory_web", context.ResolvedLiveWebReason);
        Assert.Contains("planner:benchmark_web_required", context.IntentSignals);
        Assert.Contains("benchmark:mandatory_web", context.LiveWebSignals);
    }

    [Fact]
    public async Task ChatTurnPlanner_DoesNotOverrideProtectedGeneratePrompt_WhenCurrentnessIsIncidental()
    {
        var classifier = new Mock<IIntentClassifier>();
        classifier
            .Setup(x => x.ClassifyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IntentClassification(
                new IntentAnalysis(IntentType.Generate, "test-model"),
                0.84,
                "model_first",
                new[] { "test:generate" }));

        var planner = new ChatTurnPlanner(
            classifier.Object,
            new HybridAmbiguityDetector(),
            new ClarificationPolicy(),
            new IntentTelemetryService(),
            latencyBudgetPolicy: new LatencyBudgetPolicy(),
            assumptionCheckPolicy: new AssumptionCheckPolicy(),
            liveWebRequirementPolicy: new LiveWebRequirementPolicy());

        var context = new ChatTurnContext
        {
            TurnId = "planner-live-web-protected-generate-turn",
            Request = new ChatRequestDto("Create a C# Web API project using the latest .NET version", "conv-live-web-protected-generate", 12, null),
            Conversation = new ConversationState("conv-live-web-protected-generate"),
            History = Array.Empty<ChatMessageDto>()
        };

        await planner.PlanAsync(context, CancellationToken.None);

        Assert.Equal(IntentType.Generate, context.Intent.Intent);
        Assert.Equal("web_required", context.ResolvedLiveWebRequirement);
        Assert.DoesNotContain("planner:live_web_required", context.IntentSignals);
        Assert.DoesNotContain("live_web_required_route_override", context.UncertaintyFlags);
    }

    [Fact]
    public async Task ChatTurnPlanner_ForceSearchMode_OverridesProtectedGenerateRoute()
    {
        var classifier = new Mock<IIntentClassifier>();
        classifier
            .Setup(x => x.ClassifyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IntentClassification(
                new IntentAnalysis(IntentType.Generate, "test-model"),
                0.84,
                "model_first",
                new[] { "test:generate" }));

        var planner = new ChatTurnPlanner(
            classifier.Object,
            new HybridAmbiguityDetector(),
            new ClarificationPolicy(),
            new IntentTelemetryService(),
            latencyBudgetPolicy: new LatencyBudgetPolicy(),
            assumptionCheckPolicy: new AssumptionCheckPolicy(),
            liveWebRequirementPolicy: new LiveWebRequirementPolicy());

        var context = new ChatTurnContext
        {
            TurnId = "planner-force-search-turn",
            Request = new ChatRequestDto(
                "Create a C# Web API project using the latest .NET version",
                "conv-force-search",
                12,
                null,
                LiveWebMode: "force_search"),
            Conversation = new ConversationState("conv-force-search"),
            History = Array.Empty<ChatMessageDto>()
        };

        await planner.PlanAsync(context, CancellationToken.None);

        Assert.Equal(IntentType.Research, context.Intent.Intent);
        Assert.Equal("web_required", context.ResolvedLiveWebRequirement);
        Assert.Equal("user_forced_search", context.ResolvedLiveWebReason);
        Assert.Contains("planner:live_web_forced_by_user", context.IntentSignals);
        Assert.Contains("live_web_force_search_overrode_generate_route", context.UncertaintyFlags);
    }

    [Fact]
    public async Task ChatTurnPlanner_NoWebMode_DemotesResearchIntent_AndSkipsLiveWebPromotion()
    {
        var classifier = new Mock<IIntentClassifier>();
        classifier
            .Setup(x => x.ClassifyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IntentClassification(
                new IntentAnalysis(IntentType.Research, "test-model"),
                0.88,
                "model_first",
                new[] { "test:research" }));

        var planner = new ChatTurnPlanner(
            classifier.Object,
            new HybridAmbiguityDetector(),
            new ClarificationPolicy(),
            new IntentTelemetryService(),
            latencyBudgetPolicy: new LatencyBudgetPolicy(),
            assumptionCheckPolicy: new AssumptionCheckPolicy(),
            liveWebRequirementPolicy: new LiveWebRequirementPolicy());

        var context = new ChatTurnContext
        {
            TurnId = "planner-no-web-turn",
            Request = new ChatRequestDto(
                "What is the current price of BTC today?",
                "conv-no-web",
                12,
                null,
                LiveWebMode: "no_web"),
            Conversation = new ConversationState("conv-no-web"),
            History = Array.Empty<ChatMessageDto>()
        };

        await planner.PlanAsync(context, CancellationToken.None);

        Assert.Equal(IntentType.Unknown, context.Intent.Intent);
        Assert.Equal("no_web_needed", context.ResolvedLiveWebRequirement);
        Assert.Equal("user_disabled_web", context.ResolvedLiveWebReason);
        Assert.Contains("planner:live_web_disabled_by_user", context.IntentSignals);
        Assert.Contains("live_web_disabled_by_user", context.UncertaintyFlags);
    }

    [Fact]
    public async Task HybridIntentClassifier_DetectsResearchIntent_FromRules()
    {
        var model = new Mock<IModelOrchestrator>();
        var resilienceTelemetry = new ChatResilienceTelemetryService();
        var resilience = new ChatResiliencePolicy(NullLogger<ChatResiliencePolicy>.Instance, resilienceTelemetry);
        var classifier = new HybridIntentClassifier(model.Object, resilience, NullLogger<HybridIntentClassifier>.Instance);

        var result = await classifier.ClassifyAsync("Исследуй источники и сравни benchmark по модели", CancellationToken.None);

        Assert.Equal(IntentType.Research, result.Analysis.Intent);
        Assert.True(result.Confidence >= 0.7);
        Assert.Equal("rules", result.Source);
    }

    [Fact]
    public async Task HybridIntentClassifier_OverridesModel_WhenExplicitResearchAndCitationsRequested()
    {
        var model = new Mock<IModelOrchestrator>();
        model.Setup(x => x.AnalyzeIntentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IntentAnalysis(IntentType.Generate, "test-model"));
        var resilienceTelemetry = new ChatResilienceTelemetryService();
        var resilience = new ChatResiliencePolicy(NullLogger<ChatResiliencePolicy>.Instance, resilienceTelemetry);
        var classifier = new HybridIntentClassifier(model.Object, resilience, NullLogger<HybridIntentClassifier>.Instance);

        var result = await classifier.ClassifyAsync("Compare SQL and NoSQL trade-offs with citations", CancellationToken.None);

        Assert.Equal(IntentType.Research, result.Analysis.Intent);
        Assert.True(result.Confidence >= 0.7);
        Assert.DoesNotContain("generation:admission_denied", result.Signals);
    }

    [Fact]
    public void HybridAmbiguityDetector_RequiresSafetyConfirmation_ForDestructivePrompt()
    {
        var detector = new HybridAmbiguityDetector();

        var decision = detector.Analyze("удали все временные файлы");

        Assert.True(decision.IsAmbiguous);
        Assert.Equal(AmbiguityType.SafetyConfirmation, decision.Type);
        Assert.True(decision.Confidence >= 0.9);
    }

    [Fact]
    public void IntentTelemetryService_TracksLowConfidenceRate()
    {
        var telemetry = new IntentTelemetryService();

        telemetry.Record(new IntentClassification(new IntentAnalysis(IntentType.Generate, "m"), 0.82, "rules", new[] { "a" }));
        telemetry.Record(new IntentClassification(new IntentAnalysis(IntentType.Research, "m"), 0.35, "fallback", new[] { "b" }));

        var snapshot = telemetry.GetSnapshot();

        Assert.Equal(2, snapshot.TotalClassifications);
        Assert.True(snapshot.AvgConfidence > 0);
        Assert.True(snapshot.LowConfidenceRate > 0);
        Assert.NotEmpty(snapshot.Sources);
        Assert.NotEmpty(snapshot.Intents);
    }

    [Fact]
    public void ConversationMetricsService_ProducesCitationCoverageAlert()
    {
        var metrics = new ConversationMetricsService();
        metrics.RecordTurn(new ConversationTurnMetric(100, 900, 0, true, false, 0.6, true));
        metrics.RecordTurn(new ConversationTurnMetric(120, 980, 1, true, false, 0.7, true));
        metrics.RecordTurn(new ConversationTurnMetric(140, 1100, 1, true, true, 0.8, true));

        var snapshot = metrics.GetSnapshot();

        Assert.Equal(3, snapshot.TotalTurns);
        Assert.True(snapshot.CitationCoverage < 0.70);
        Assert.Contains(snapshot.Alerts, alert => alert.Contains("Citation coverage", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ConversationMetricsService_UsesClaimBasedCoverage_WhenClaimsProvided()
    {
        var metrics = new ConversationMetricsService();
        metrics.RecordTurn(new ConversationTurnMetric(90, 700, 0, true, false, 0.75, true, VerifiedClaims: 1, TotalClaims: 3));
        metrics.RecordTurn(new ConversationTurnMetric(100, 710, 1, true, true, 0.78, true, VerifiedClaims: 2, TotalClaims: 2));

        var snapshot = metrics.GetSnapshot();

        Assert.Equal(3, snapshot.VerifiedClaims);
        Assert.Equal(5, snapshot.TotalClaims);
        Assert.True(snapshot.CitationCoverage > 0.5 && snapshot.CitationCoverage < 0.7);
    }

    [Fact]
    public void ConversationMetricsService_TracksTtftBreakdown()
    {
        var metrics = new ConversationMetricsService();
        metrics.RecordTurn(new ConversationTurnMetric(
            110,
            900,
            0,
            false,
            false,
            0.72,
            true,
            ModelTtftMs: 70,
            TransportTtftMs: 20,
            EndToEndTtftMs: 110));
        metrics.RecordTurn(new ConversationTurnMetric(
            130,
            920,
            1,
            false,
            false,
            0.74,
            true,
            ModelTtftMs: 90,
            TransportTtftMs: 30,
            EndToEndTtftMs: 130));

        var snapshot = metrics.GetSnapshot();

        Assert.Equal(80, snapshot.AvgModelTtftMs, 2);
        Assert.Equal(25, snapshot.AvgTransportTtftMs, 2);
        Assert.Equal(120, snapshot.AvgEndToEndTtftMs, 2);
    }

    [Fact]
    public void ConversationMetricsService_TracksResearchRoutingCounters()
    {
        var metrics = new ConversationMetricsService();
        metrics.RecordTurn(new ConversationTurnMetric(
            FirstTokenLatencyMs: 100,
            FullResponseLatencyMs: 700,
            ToolCallsCount: 1,
            IsFactualPrompt: true,
            HasCitations: false,
            Confidence: 0.5,
            IsSuccessful: true,
            Intent: "research",
            ResearchClarificationFallback: true));
        metrics.RecordTurn(new ConversationTurnMetric(
            FirstTokenLatencyMs: 90,
            FullResponseLatencyMs: 680,
            ToolCallsCount: 1,
            IsFactualPrompt: true,
            HasCitations: true,
            Confidence: 0.7,
            IsSuccessful: true,
            Intent: "research",
            ResearchClarificationFallback: false));

        var snapshot = metrics.GetSnapshot();

        Assert.Equal(2, snapshot.ResearchRoutedTurns);
        Assert.Equal(1, snapshot.ResearchClarificationFallbackTurns);
    }

    [Fact]
    public void ConversationMetricsService_TracksReasoningEfficiencySeparately()
    {
        var metrics = new ConversationMetricsService();
        metrics.RecordTurn(new ConversationTurnMetric(
            FirstTokenLatencyMs: 140,
            FullResponseLatencyMs: 920,
            ToolCallsCount: 0,
            IsFactualPrompt: false,
            HasCitations: false,
            Confidence: 0.81,
            IsSuccessful: true,
            ExecutionMode: "deep",
            Reasoning: new ReasoningTurnMetric(
                PathActive: true,
                BranchingApplied: true,
                BranchesExplored: 2,
                CandidatesRejected: 1,
                LocalVerificationChecks: 3,
                LocalVerificationPasses: 2,
                LocalVerificationRejects: 1,
                ModelCallsUsed: 2,
                RetrievalChunksUsed: 4,
                ProceduralLessonsUsed: 1,
                ApproximateTokenCost: 180)));
        metrics.RecordTurn(new ConversationTurnMetric(
            FirstTokenLatencyMs: 110,
            FullResponseLatencyMs: 700,
            ToolCallsCount: 0,
            IsFactualPrompt: false,
            HasCitations: false,
            Confidence: 0.77,
            IsSuccessful: true));

        var snapshot = metrics.GetSnapshot();

        Assert.Equal(1, snapshot.Reasoning.Turns);
        Assert.Equal(1, snapshot.Reasoning.BranchingTurns);
        Assert.Equal(0.5, snapshot.Reasoning.BranchingRate, 3);
        Assert.Equal(2, snapshot.Reasoning.AvgBranchesExplored, 3);
        Assert.Equal(1, snapshot.Reasoning.AvgCandidatesRejected, 3);
        Assert.Equal(3, snapshot.Reasoning.LocalVerificationChecks);
        Assert.Equal(2, snapshot.Reasoning.LocalVerificationPasses);
        Assert.Equal(1, snapshot.Reasoning.LocalVerificationRejects);
        Assert.Equal(2d / 3d, snapshot.Reasoning.LocalVerificationPassRate, 3);
        Assert.Equal(2, snapshot.Reasoning.AvgModelCallsUsed, 3);
        Assert.Equal(4, snapshot.Reasoning.AvgRetrievalChunksUsed, 3);
        Assert.Equal(1, snapshot.Reasoning.AvgProceduralLessonsUsed, 3);
        Assert.Equal(180, snapshot.Reasoning.AvgApproximateTokenCost, 3);
    }
}
