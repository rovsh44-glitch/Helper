using Helper.Runtime.Core;
using Helper.Runtime.Generation;

namespace Helper.Runtime.Tests;

public sealed class ParityGateEvaluatorTests
{
    [Fact]
    public void Evaluate_ReturnsPassed_WhenMetricsMeetThresholdsAndNoAlerts()
    {
        var evaluator = new ParityGateEvaluator();
        var report = new ParityCertificationReport(
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            ReportPath: "doc/report.md",
            TotalRuns: 120,
            GoldenHitRate: 0.93,
            GenerationSuccessRate: 0.97,
            P95ReadySeconds: 18,
            UnknownErrorRate: 0.02,
            ToolSuccessRatio: 0.95,
            Alerts: Array.Empty<string>());
        var thresholds = new ParityGateThresholds(
            MinGoldenHitRate: 0.90,
            MinGenerationSuccessRate: 0.95,
            MaxP95ReadySeconds: 25,
            MaxUnknownErrorRate: 0.05,
            MinToolSuccessRatio: 0.90);

        var decision = evaluator.Evaluate(report, thresholds);

        Assert.True(decision.Passed);
        Assert.Empty(decision.Violations);
    }

    [Fact]
    public void Evaluate_ReturnsViolations_WhenAnyKpiFailsOrAlertsExist()
    {
        var previous = Environment.GetEnvironmentVariable("HELPER_PARITY_GATE_ENFORCE_REPORT_ALERTS");
        Environment.SetEnvironmentVariable("HELPER_PARITY_GATE_ENFORCE_REPORT_ALERTS", "true");

        try
        {
        var evaluator = new ParityGateEvaluator();
        var report = new ParityCertificationReport(
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            ReportPath: "doc/report.md",
            TotalRuns: 30,
            GoldenHitRate: 0.70,
            GenerationSuccessRate: 0.80,
            P95ReadySeconds: 55,
            UnknownErrorRate: 0.40,
            ToolSuccessRatio: 0.50,
            Alerts: new[] { "Tool success ratio below target." });
        var thresholds = new ParityGateThresholds();

        var decision = evaluator.Evaluate(report, thresholds);

        Assert.False(decision.Passed);
        Assert.True(decision.Violations.Count >= 5);
        Assert.Contains(decision.Violations, x => x.Contains("GoldenHitRate", StringComparison.Ordinal));
        Assert.Contains(decision.Violations, x => x.Contains("alerts", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Environment.SetEnvironmentVariable("HELPER_PARITY_GATE_ENFORCE_REPORT_ALERTS", previous);
        }
    }

    [Fact]
    public void Evaluate_ReturnsViolation_WhenGoldenSampleIsInsufficient()
    {
        var evaluator = new ParityGateEvaluator();
        var report = new ParityCertificationReport(
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            ReportPath: "doc/report.md",
            TotalRuns: 42,
            GoldenHitRate: 1.0,
            GenerationSuccessRate: 0.98,
            P95ReadySeconds: 12,
            UnknownErrorRate: 0.01,
            ToolSuccessRatio: 0.96,
            Alerts: Array.Empty<string>(),
            GoldenAttempts: 2,
            GoldenHits: 2,
            MinGoldenAttemptsRequired: 20,
            GoldenSampleInsufficient: true);
        var thresholds = new ParityGateThresholds();

        var decision = evaluator.Evaluate(report, thresholds);

        Assert.False(decision.Passed);
        Assert.Contains(decision.Violations, x => x.Contains("GoldenSampleInsufficient", StringComparison.Ordinal));
        Assert.DoesNotContain(decision.Violations, x => x.Contains("GoldenHitRate", StringComparison.Ordinal));
    }
}

