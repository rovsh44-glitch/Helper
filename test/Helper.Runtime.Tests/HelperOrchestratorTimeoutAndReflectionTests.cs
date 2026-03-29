using Helper.Runtime.Core;
using Helper.Runtime.Generation;
using Helper.Runtime.Infrastructure;
using Moq;

namespace Helper.Runtime.Tests;

public sealed class HelperOrchestratorTimeoutAndReflectionTests
{
    [Fact]
    public async Task GenerateProjectAsync_StageTimeout_PersistsTimeoutRunArtifacts()
    {
        using var temp = new TempDirectoryScope();
        var outputPath = Path.Combine(temp.Path, "PROJECTS", "timeout_case");

        var routing = new Mock<ITemplateRoutingService>(MockBehavior.Strict);
        routing
            .Setup(x => x.RouteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(async (string _, CancellationToken token) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(5), token);
                return new TemplateRoutingDecision(false, null, 0, Array.Empty<string>(), "slow");
            });

        var timeoutPolicy = new Mock<IGenerationStageTimeoutPolicy>(MockBehavior.Strict);
        timeoutPolicy
            .Setup(x => x.Resolve(It.IsAny<GenerationTimeoutStage>()))
            .Returns(TimeSpan.FromMilliseconds(25));

        var healthReporter = new Mock<IGenerationHealthReporter>(MockBehavior.Strict);
        healthReporter
            .Setup(x => x.AppendAsync(It.IsAny<GenerationRunReport>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var orchestrator = new HelperOrchestrator(
            Mock.Of<IModelOrchestrator>(),
            Mock.Of<IResearchEngine>(),
            Mock.Of<IMaintenanceService>(),
            Mock.Of<IProjectForgeOrchestrator>(),
            null!,
            Mock.Of<IReflectionService>(),
            Mock.Of<IGraphOrchestrator>(),
            Mock.Of<IToolService>(),
            Mock.Of<IConsciousnessService>(),
            Mock.Of<IAutoDebugger>(),
            Mock.Of<IArchitectMutation>(),
            Mock.Of<IExpertConsultant>(),
            Mock.Of<IBlueprintEngine>(),
            Mock.Of<ISurgicalToolbox>(),
            Mock.Of<IPlatformGuard>(),
            Mock.Of<IInternalObserver>(),
            Mock.Of<IIntentBcaster>(),
            Mock.Of<IMetacognitiveAgent>(),
            routing.Object,
            new GenerationMetricsService(),
            new GenerationValidationReportWriter(),
            healthReporter.Object,
            Mock.Of<IFailureEnvelopeFactory>(),
            timeoutPolicy.Object,
            Mock.Of<IFixStrategyRunner>(),
            Mock.Of<IGenerationTemplatePromotionService>());

        var result = await orchestrator.GenerateProjectAsync(
            new GenerationRequest(
                Prompt: "generate generic sample app",
                OutputPath: outputPath));

        Assert.False(result.Success);

        var reportPath = Path.Combine(outputPath, "validation_report.json");
        var rootRunsPath = Path.GetFullPath(Path.Combine(outputPath, "..", "generation_runs.jsonl"));
        Assert.True(File.Exists(reportPath));
        Assert.True(File.Exists(rootRunsPath));

        var reportJson = await File.ReadAllTextAsync(reportPath);
        Assert.Contains("GENERATION_STAGE_TIMEOUT", reportJson, StringComparison.Ordinal);

        var rootRuns = await File.ReadAllTextAsync(rootRunsPath);
        Assert.Contains("GENERATION_STAGE_TIMEOUT", rootRuns, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GenerateProjectAsync_DisabledSuccessReflection_DoesNotInvokeSuccessReview()
    {
        using var temp = new TempDirectoryScope();
        var outputPath = Path.Combine(temp.Path, "PROJECTS", "research_case");

        var previousSuccessReflection = Environment.GetEnvironmentVariable("HELPER_ENABLE_SUCCESS_REFLECTION");
        Environment.SetEnvironmentVariable("HELPER_ENABLE_SUCCESS_REFLECTION", "false");

        try
        {
            var routing = new Mock<ITemplateRoutingService>(MockBehavior.Strict);
            routing
                .Setup(x => x.RouteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new TemplateRoutingDecision(false, null, 0, Array.Empty<string>(), "no_match"));

            var observer = new Mock<IInternalObserver>(MockBehavior.Strict);
            observer
                .Setup(x => x.CaptureSnapshotAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SystemSnapshot(
                    DirectoryTree: "",
                    RecentErrors: new List<string>(),
                    Platform: new PlatformCapabilities(OSPlatform.Windows, '\\', "WPF", "powershell", new List<string>())));

            var bcaster = new Mock<IIntentBcaster>(MockBehavior.Strict);
            bcaster
                .Setup(x => x.BroadcastIntentAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Action<string>?>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var blueprints = new Mock<IBlueprintEngine>(MockBehavior.Strict);
            blueprints
                .Setup(x => x.DesignBlueprintAsync(It.IsAny<string>(), It.IsAny<OSPlatform>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ProjectBlueprint(
                    Name: "ResearchProject",
                    TargetOS: OSPlatform.Windows,
                    Files: new List<SwarmFileDefinition>(),
                    NuGetPackages: new List<string>(),
                    ArchitectureReasoning: "test"));
            blueprints
                .Setup(x => x.ValidateBlueprintAsync(It.IsAny<ProjectBlueprint>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            var modelSelector = new Mock<IModelOrchestrator>(MockBehavior.Strict);
            modelSelector
                .Setup(x => x.AnalyzeIntentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new IntentAnalysis(IntentType.Research, "test-model"));

            var research = new Mock<IResearchEngine>(MockBehavior.Strict);
            research
                .Setup(x => x.HandleResearchModeAsync(
                    It.IsAny<GenerationRequest>(),
                    It.IsAny<IntentAnalysis>(),
                    It.IsAny<Action<string>?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new GenerationResult(
                    Success: true,
                    Files: new List<GeneratedFile> { new("notes.md", "ok") },
                    ProjectPath: outputPath,
                    Errors: new List<BuildError>(),
                    Duration: TimeSpan.FromMilliseconds(50),
                    IsResearch: true));

            var reflection = new Mock<IReflectionService>(MockBehavior.Strict);

            var timeoutPolicy = new Mock<IGenerationStageTimeoutPolicy>(MockBehavior.Strict);
            timeoutPolicy
                .Setup(x => x.Resolve(It.IsAny<GenerationTimeoutStage>()))
                .Returns(TimeSpan.FromSeconds(3));

            var promotion = new Mock<IGenerationTemplatePromotionService>(MockBehavior.Strict);
            promotion
                .Setup(x => x.TryPromoteAsync(
                    It.IsAny<GenerationRequest>(),
                    It.IsAny<GenerationResult>(),
                    It.IsAny<Action<string>?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new TemplatePromotionOutcome(
                    Attempted: false,
                    Success: true,
                    TemplateId: "none",
                    Version: null,
                    Message: "skip",
                    Errors: Array.Empty<string>()));

            var orchestrator = new HelperOrchestrator(
                modelSelector.Object,
                research.Object,
                Mock.Of<IMaintenanceService>(),
                Mock.Of<IProjectForgeOrchestrator>(),
                null!,
                reflection.Object,
                Mock.Of<IGraphOrchestrator>(),
                Mock.Of<IToolService>(),
                Mock.Of<IConsciousnessService>(),
                Mock.Of<IAutoDebugger>(),
                Mock.Of<IArchitectMutation>(),
                Mock.Of<IExpertConsultant>(),
                blueprints.Object,
                Mock.Of<ISurgicalToolbox>(),
                Mock.Of<IPlatformGuard>(),
                observer.Object,
                bcaster.Object,
                Mock.Of<IMetacognitiveAgent>(),
                routing.Object,
                new GenerationMetricsService(),
                Mock.Of<IGenerationValidationReportWriter>(),
                Mock.Of<IGenerationHealthReporter>(),
                Mock.Of<IFailureEnvelopeFactory>(),
                timeoutPolicy.Object,
                Mock.Of<IFixStrategyRunner>(),
                promotion.Object);

            var result = await orchestrator.GenerateProjectAsync(
                new GenerationRequest(
                    Prompt: "research software architecture",
                    OutputPath: outputPath));

            Assert.True(result.Success);
            reflection.Verify(
                x => x.ConductSuccessReviewAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }
        finally
        {
            Environment.SetEnvironmentVariable("HELPER_ENABLE_SUCCESS_REFLECTION", previousSuccessReflection);
        }
    }

    private sealed class TempDirectoryScope : IDisposable
    {
        public TempDirectoryScope()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "helper_orchestrator_timeout_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }
            }
            catch
            {
                // no-op
            }
        }
    }
}

