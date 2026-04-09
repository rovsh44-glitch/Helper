using Helper.Api.Backend.Application;
using Helper.Api.Backend.ModelGateway;
using Helper.Api.Conversation;
using Helper.Api.Conversation.InteractionState;
using Helper.Api.Hosting;
using Helper.Runtime.Core;
using Helper.Runtime.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Helper.Runtime.Tests;

public sealed class ConversationMergedPersonalizationPromptTests
{
    [Fact]
    public async Task ChatTurnExecutor_Uses_MergedPersonalization_Profile_In_SystemInstruction()
    {
        var ai = new Mock<AILink>("http://localhost:11434", "qwen");
        string? capturedSystemInstruction = null;
        ai.Setup(a => a.AskAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<string>()))
            .Callback<string, CancellationToken, string?, string?, int, string?>((_, _, _, _, _, instruction) =>
            {
                capturedSystemInstruction = instruction;
            })
            .ReturnsAsync("executor-result");

        var model = new Mock<IModelOrchestrator>();
        var research = new Mock<IResearchService>();
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
        var userProfileService = new UserProfileService();
        var executor = new ChatTurnExecutor(
            ai.Object,
            model.Object,
            research.Object,
            new ShortHorizonResearchCache(),
            resilience,
            orchestrator.Object,
            new ApiRuntimeConfig("root", "projects", "library", "logs", "templates", "test-key"),
            userProfileService);

        var conversation = new ConversationState("profile-merged")
        {
            PersonalizationProfile = PersonalizationProfile.Default
        };
        var plannerStep = new TurnPersonalizationStep(
            userProfileService,
            new TurnLanguageResolver(),
            new StubCollaborationIntentDetector(new CollaborationIntentAnalysis(
                IsGuidanceSeeking: true,
                TrustsBestJudgment: true,
                SeeksDelegatedExecution: false,
                PrefersAnswerOverClarification: true,
                HasHardConstraintLanguage: false,
                PrimaryMode: "guidance",
                Signals: new[] { "guidance_seeking" })),
            new CommunicationQualityPolicy(),
            new PersonalizationMergePolicy(),
            new LocalFirstBenchmarkPolicy(),
            new InteractionStateAnalyzer(),
            new InteractionPolicyProjector());
        var context = new ChatTurnContext
        {
            TurnId = "executor-merged-profile",
            Request = new ChatRequestDto("What current sources compare .NET hosting guidance?", conversation.Id, 10, null),
            Conversation = conversation,
            History = new[]
            {
                new ChatMessageDto("user", "What current sources compare .NET hosting guidance?", DateTimeOffset.UtcNow)
            },
            Intent = new IntentAnalysis(IntentType.Unknown, "test-model")
        };

        await plannerStep.ExecuteAsync(new TurnPlanningContext(context), new TurnPlanningState(), CancellationToken.None);
        await executor.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal("executor-result", context.ExecutionOutput);
        Assert.NotNull(capturedSystemInstruction);
        Assert.Contains("citation_preference=prefer", capturedSystemInstruction, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("clarification_tolerance=low", capturedSystemInstruction, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class StubCollaborationIntentDetector : ICollaborationIntentDetector
    {
        private readonly CollaborationIntentAnalysis _analysis;

        public StubCollaborationIntentDetector(CollaborationIntentAnalysis analysis)
        {
            _analysis = analysis;
        }

        public CollaborationIntentAnalysis Analyze(string? requestMessage, string? resolvedLanguage = null)
        {
            return _analysis;
        }
    }
}
