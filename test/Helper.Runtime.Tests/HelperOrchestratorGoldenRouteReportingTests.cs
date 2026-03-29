using Helper.Runtime.Core;
using Helper.Runtime.Generation;
using Helper.Runtime.Infrastructure;
using Moq;

namespace Helper.Runtime.Tests;

public sealed class HelperOrchestratorGoldenRouteReportingTests
{
    [Fact]
    public async Task GenerateProjectAsync_GoldenRouteMatch_WritesPersistedGoldenRunReport()
    {
        using var temp = new TempDirectoryScope();
        var forgeOutput = Path.Combine(temp.Path, "PROJECTS", "FORGE_OUTPUT", "Template_Calculator_abc123");
        Directory.CreateDirectory(forgeOutput);

        var routing = new Mock<ITemplateRoutingService>(MockBehavior.Strict);
        routing
            .Setup(x => x.RouteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TemplateRoutingDecision(
                Matched: true,
                TemplateId: "Template_Calculator",
                Confidence: 0.97,
                Candidates: new[] { "Template_Calculator" },
                Reason: "rule_match"));

        var forge = new Mock<IProjectForgeOrchestrator>(MockBehavior.Strict);
        forge
            .Setup(x => x.ForgeProjectAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Action<string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GenerationResult(
                Success: true,
                Files: new List<GeneratedFile>
                {
                    new GeneratedFile("Program.cs", "namespace Demo; public static class Program { }")
                },
                ProjectPath: forgeOutput,
                Errors: new List<BuildError>(),
                Duration: TimeSpan.FromSeconds(2)));

        GenerationRunReport? capturedReport = null;
        var reportWriter = new Mock<IGenerationValidationReportWriter>(MockBehavior.Strict);
        reportWriter
            .Setup(x => x.WriteAsync(It.IsAny<GenerationRunReport>(), It.IsAny<CancellationToken>()))
            .Callback<GenerationRunReport, CancellationToken>((report, _) => capturedReport = report)
            .Returns(Task.CompletedTask);

        var healthReporter = new Mock<IGenerationHealthReporter>(MockBehavior.Strict);
        healthReporter
            .Setup(x => x.AppendAsync(It.IsAny<GenerationRunReport>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var timeoutPolicy = new Mock<IGenerationStageTimeoutPolicy>(MockBehavior.Strict);
        timeoutPolicy
            .Setup(x => x.Resolve(It.IsAny<GenerationTimeoutStage>()))
            .Returns(TimeSpan.FromMinutes(1));

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
                TemplateId: "Template_Calculator",
                Version: null,
                Message: "disabled",
                Errors: Array.Empty<string>()));

        var metrics = new GenerationMetricsService();
        var routeTelemetry = new RouteTelemetryService();
        var orchestrator = new HelperOrchestrator(
            Mock.Of<IModelOrchestrator>(),
            Mock.Of<IResearchEngine>(),
            Mock.Of<IMaintenanceService>(),
            forge.Object,
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
            metrics,
            reportWriter.Object,
            healthReporter.Object,
            Mock.Of<IFailureEnvelopeFactory>(),
            timeoutPolicy.Object,
            Mock.Of<IFixStrategyRunner>(),
            promotion.Object,
            routeTelemetry);

        var request = new GenerationRequest(
            Prompt: "Сгенерируй калькулятор",
            OutputPath: Path.Combine(temp.Path, "PROJECTS", "req_01"));
        var result = await orchestrator.GenerateProjectAsync(request);

        Assert.True(result.Success);
        Assert.NotNull(capturedReport);
        Assert.True(capturedReport!.RouteMatched);
        Assert.True(capturedReport.GoldenTemplateMatched);
        Assert.False(capturedReport.GoldenTemplateEligible);
        Assert.Equal("Template_Calculator", capturedReport.RoutedTemplateId);
        Assert.Equal("forge/golden-template", capturedReport.ModelRoute);
        Assert.True(capturedReport.StageDurationsSec?.ContainsKey("routing"));
        Assert.True(capturedReport.StageDurationsSec?.ContainsKey("forge"));
        Assert.Equal(forgeOutput, capturedReport.RawProjectRoot);

        var snapshot = metrics.GetSnapshot();
        Assert.Equal(1, snapshot.GenerationRunsTotal);
        Assert.Equal(1, snapshot.GenerationGoldenTemplateHitTotal);
        Assert.Contains(routeTelemetry.GetSnapshot().Recent, entry =>
            entry.OperationKind == RouteTelemetryOperationKinds.GenerationRun &&
            entry.RouteKey == "template_calculator" &&
            entry.ModelRoute == "forge/golden-template");

        reportWriter.Verify(
            x => x.WriteAsync(It.IsAny<GenerationRunReport>(), It.IsAny<CancellationToken>()),
            Times.Once);
        healthReporter.Verify(
            x => x.AppendAsync(It.IsAny<GenerationRunReport>(), It.IsAny<CancellationToken>()),
            Times.Once);
        forge.VerifyAll();
        routing.VerifyAll();
    }

    private sealed class TempDirectoryScope : IDisposable
    {
        public TempDirectoryScope()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "helper_orchestrator_route_" + Guid.NewGuid().ToString("N"));
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

