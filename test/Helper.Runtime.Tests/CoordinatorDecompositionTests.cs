using Helper.Api.Backend.Application;
using Helper.Api.Conversation;
using Helper.Runtime.Core;
using Helper.Runtime.Generation;
using Helper.Runtime.Infrastructure;

namespace Helper.Runtime.Tests;

public sealed class CoordinatorDecompositionTests
{
    [Fact]
    public void TurnRouteTelemetryRecorder_RecordsBlockedTurns_WithBlockedQuality()
    {
        var telemetry = new RouteTelemetryService();
        var recorder = new TurnRouteTelemetryRecorder(telemetry);
        var state = new ConversationState("conv-1");
        var risk = new InputRiskScanResult(true, "blocked by policy", new[] { "harmful", "input_blocked" });

        recorder.RecordBlockedTurn(state, risk);

        var recent = Assert.Single(telemetry.GetSnapshot().Recent);
        Assert.Equal("input_risk_blocked", recent.RouteKey);
        Assert.Equal(RouteTelemetryQualities.Blocked, recent.Quality);
        Assert.Equal(RouteTelemetryOutcomes.Blocked, recent.Outcome);
        Assert.Contains("harmful", recent.Signals ?? Array.Empty<string>());
    }

    [Fact]
    public void CompileGateWorkspacePreparer_RejectsWpfApp_WhenProfileIsNotWpfWinExe()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            File.WriteAllText(Path.Combine(tempRoot, "template.json"), """{ "ProjectType": "wpf-app" }""");
            var preparer = new CompileGateWorkspacePreparer();

            var error = preparer.ValidateDeclaredProjectType(
                tempRoot,
                new CompileProjectProfile("net8.0", "Library", UseWpf: false, UseWindowsForms: false, EnableWindowsTargeting: false));

            Assert.NotNull(error);
            Assert.Equal("PROJECT_TYPE_MISMATCH", error!.Code);
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void ParityDailyBackfillMetricsCalculator_RaisesThresholdAlerts()
    {
        var calculator = new ParityDailyBackfillMetricsCalculator();
        var now = DateTimeOffset.UtcNow;
        var entries = new[]
        {
            new ParityBackfillRunEntry(
                "run-1",
                now.AddMinutes(-5),
                now.AddMinutes(-5),
                now.AddMinutes(-4),
                new ParityRunOutcome(ParityRunDisposition.CleanSuccess, false, false, null, null),
                Array.Empty<string>(),
                GoldenTemplateMatched: true,
                GoldenTemplateEligible: true,
                WorkloadClass: GenerationWorkloadClassifier.Parity),
            new ParityBackfillRunEntry(
                "run-2",
                now.AddMinutes(-3),
                now.AddMinutes(-3),
                now.AddMinutes(-2),
                new ParityRunOutcome(ParityRunDisposition.Failure, false, true, ParityFailureCategory.Unknown, "UNKNOWN"),
                new[] { "UNKNOWN ERROR" },
                GoldenTemplateMatched: false,
                GoldenTemplateEligible: true,
                WorkloadClass: GenerationWorkloadClassifier.Parity)
        };

        var metrics = calculator.Compute(entries, minGoldenAttempts: 3, ParityGateThresholds.FromEnvironment());

        Assert.Equal(2, metrics.TotalRuns);
        Assert.Equal(1, metrics.SuccessfulRuns);
        Assert.Contains(metrics.Snapshot.Alerts, alert => alert.Contains("insufficient_golden_sample", StringComparison.Ordinal));
        Assert.Contains(metrics.Snapshot.Alerts, alert => alert.Contains("Generation success rate below target", StringComparison.Ordinal));
    }

    [Fact]
    public void TemplateSmokeScenarioCatalog_ResolvesDefaultDotnetScenarios()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            File.WriteAllText(Path.Combine(tempRoot, "Program.cs"), "public class Program { }");
            var scenarios = TemplateSmokeScenarioCatalog.ResolveScenarioIds(
                tempRoot,
                metadata: null,
                new PolyglotProjectProfile(PolyglotProjectKind.Dotnet, "csharp"));

            Assert.Contains("compile", scenarios);
            Assert.Contains("artifact-validation", scenarios);
            Assert.Contains("dotnet-csproj-exists", scenarios);
            Assert.Contains("csharp-source-exists", scenarios);
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "helper_coord_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void TryDeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Ignore cleanup failures in tests.
        }
    }
}

