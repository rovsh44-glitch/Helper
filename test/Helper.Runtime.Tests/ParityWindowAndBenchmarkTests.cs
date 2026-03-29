using System.Text.Json;
using Helper.Runtime.Core;
using Helper.Runtime.Generation;

namespace Helper.Runtime.Tests;

public sealed class ParityWindowAndBenchmarkTests
{
    [Fact]
    public async Task ParityWindowGate_FailsWhenWindowIsIncomplete_AndAllowIncompleteDisabled()
    {
        using var temp = new TempDirectoryScope();
        var dailyDir = Path.Combine(temp.Path, "doc", "parity_nightly", "daily");
        Directory.CreateDirectory(dailyDir);

        await WriteSnapshotAsync(dailyDir, "2026-02-26", 0.95, 0.97, 12.0, 0.01, 0.96);
        await WriteSnapshotAsync(dailyDir, "2026-02-27", 0.96, 0.98, 13.0, 0.01, 0.97);

        var previousAllowIncomplete = Environment.GetEnvironmentVariable("HELPER_PARITY_WINDOW_ALLOW_INCOMPLETE");
        try
        {
            Environment.SetEnvironmentVariable("HELPER_PARITY_WINDOW_ALLOW_INCOMPLETE", "false");
            var gate = new ParityWindowGateService(new ParityGateEvaluator(), temp.Path);
            var report = await gate.EvaluateAsync(7);

            Assert.False(report.Passed);
            Assert.False(report.WindowComplete);
            Assert.Contains(report.Violations, x => x.Contains("Window incomplete", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Environment.SetEnvironmentVariable("HELPER_PARITY_WINDOW_ALLOW_INCOMPLETE", previousAllowIncomplete);
        }
    }

    [Fact]
    public async Task ParityWindowGate_ReadsSnapshots_FromConfiguredSnapshotRoot()
    {
        using var temp = new TempDirectoryScope();
        var previewDailyDir = Path.Combine(temp.Path, "doc", "parity_preview_day02", "daily");
        Directory.CreateDirectory(previewDailyDir);

        await WriteSnapshotAsync(previewDailyDir, "2026-03-07", 0.95, 0.97, 12.0, 0.01, 0.96);

        var previousAllowIncomplete = Environment.GetEnvironmentVariable("HELPER_PARITY_WINDOW_ALLOW_INCOMPLETE");
        var previousSnapshotRoot = Environment.GetEnvironmentVariable("HELPER_PARITY_SNAPSHOT_ROOT");
        try
        {
            Environment.SetEnvironmentVariable("HELPER_PARITY_WINDOW_ALLOW_INCOMPLETE", "false");
            Environment.SetEnvironmentVariable("HELPER_PARITY_SNAPSHOT_ROOT", Path.Combine(temp.Path, "doc", "parity_preview_day02"));

            var gate = new ParityWindowGateService(new ParityGateEvaluator(), temp.Path);
            var report = await gate.EvaluateAsync(1);

            Assert.True(report.WindowComplete);
            Assert.True(report.Passed);
            Assert.Single(report.Days);
            Assert.Equal("2026-03-07", report.Days[0].DateUtc);
        }
        finally
        {
            Environment.SetEnvironmentVariable("HELPER_PARITY_WINDOW_ALLOW_INCOMPLETE", previousAllowIncomplete);
            Environment.SetEnvironmentVariable("HELPER_PARITY_SNAPSHOT_ROOT", previousSnapshotRoot);
        }
    }

    [Fact]
    public async Task GenerationParityBenchmark_ComputesMetrics_FromCorpora()
    {
        using var temp = new TempDirectoryScope();
        var evalDir = Path.Combine(temp.Path, "eval");
        Directory.CreateDirectory(evalDir);

        var goldenPath = Path.Combine(evalDir, "golden.jsonl");
        await File.WriteAllLinesAsync(goldenPath, new[]
        {
            JsonSerializer.Serialize(new GoldenTemplateBenchmarkCase("g1", "build calculator", "Template_Calc")),
            JsonSerializer.Serialize(new GoldenTemplateBenchmarkCase("g2", "build chess", "Template_Chess"))
        });

        var incidentPath = Path.Combine(evalDir, "incident.jsonl");
        await File.WriteAllLinesAsync(incidentPath, new[]
        {
            JsonSerializer.Serialize(new IncidentBenchmarkCase("i1", "CS0246", "The type or namespace name 'Foo' could not be found", "Synthesis", "Dependency")),
            JsonSerializer.Serialize(new IncidentBenchmarkCase("i2", "TIMEOUT", "generation timeout", "Autofix", "Timeout"))
        });

        var service = new GenerationParityBenchmarkService(
            new StubTemplateRoutingService(),
            new StubFailureEnvelopeFactory(),
            temp.Path);
        var report = await service.RunAsync(goldenPath, incidentPath);

        Assert.Equal(2, report.GoldenCaseCount);
        Assert.Equal(1.0, report.GoldenHitRate);
        Assert.Equal(2, report.IncidentCaseCount);
        Assert.Equal(1.0, report.RootCausePrecision);
        Assert.True(File.Exists(report.ReportPath));
    }

    [Fact]
    public async Task ClosedLoopPredictability_GeneratesStableReport()
    {
        using var temp = new TempDirectoryScope();
        var evalDir = Path.Combine(temp.Path, "eval");
        Directory.CreateDirectory(evalDir);

        var incidentPath = Path.Combine(evalDir, "incident.jsonl");
        await File.WriteAllLinesAsync(incidentPath, new[]
        {
            JsonSerializer.Serialize(new IncidentBenchmarkCase("i1", "CS0246", "The type or namespace name 'Foo' could not be found", "Synthesis", "Dependency")),
            JsonSerializer.Serialize(new IncidentBenchmarkCase("i2", "TIMEOUT", "generation timeout", "Autofix", "Timeout")),
            JsonSerializer.Serialize(new IncidentBenchmarkCase("i3", "UNKNOWN", "unknown fail", "Unknown", "Unknown"))
        });

        var service = new ClosedLoopPredictabilityService(temp.Path);
        var report = await service.EvaluateAsync(incidentPath, Path.Combine(temp.Path, "doc", "closed_loop.md"));

        Assert.True(File.Exists(report.ReportPath));
        Assert.NotEmpty(report.Classes);
        Assert.All(report.Classes, x => Assert.True(x.Variance >= 0));
    }

    private static Task WriteSnapshotAsync(
        string dailyDir,
        string date,
        double goldenHitRate,
        double generationSuccessRate,
        double p95ReadySeconds,
        double unknownErrorRate,
        double toolSuccessRatio)
    {
        var snapshot = new ParityDailySnapshot(
            DateUtc: date,
            GeneratedAtUtc: DateTimeOffset.Parse($"{date}T00:00:00Z"),
            TotalRuns: 120,
            GoldenHitRate: goldenHitRate,
            GenerationSuccessRate: generationSuccessRate,
            P95ReadySeconds: p95ReadySeconds,
            UnknownErrorRate: unknownErrorRate,
            ToolSuccessRatio: toolSuccessRatio,
            Alerts: Array.Empty<string>());
        var path = Path.Combine(dailyDir, $"parity_{date}.json");
        var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
        return File.WriteAllTextAsync(path, json);
    }

    private sealed class StubTemplateRoutingService : ITemplateRoutingService
    {
        public Task<TemplateRoutingDecision> RouteAsync(string prompt, CancellationToken ct = default)
        {
            if (prompt.Contains("calculator", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(new TemplateRoutingDecision(true, "Template_Calc", 0.9, new[] { "Template_Calc" }, "stub"));
            }

            if (prompt.Contains("chess", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(new TemplateRoutingDecision(true, "Template_Chess", 0.9, new[] { "Template_Chess" }, "stub"));
            }

            return Task.FromResult(new TemplateRoutingDecision(false, null, 0, Array.Empty<string>(), "stub"));
        }
    }

    private sealed class StubFailureEnvelopeFactory : IFailureEnvelopeFactory
    {
        public IReadOnlyList<FailureEnvelope> FromBuildErrors(
            FailureStage stage,
            string subsystem,
            IReadOnlyList<BuildError> errors,
            string? correlationId = null)
        {
            var envelopes = new List<FailureEnvelope>();
            foreach (var error in errors)
            {
                var cause = string.Equals(error.Code, "CS0246", StringComparison.OrdinalIgnoreCase)
                    ? RootCauseClass.Dependency
                    : string.Equals(error.Code, "TIMEOUT", StringComparison.OrdinalIgnoreCase)
                        ? RootCauseClass.Timeout
                        : RootCauseClass.Unknown;
                envelopes.Add(new FailureEnvelope(
                    stage,
                    subsystem,
                    error.Code,
                    cause,
                    Retryable: true,
                    UserAction: "retry",
                    Evidence: error.Message,
                    CorrelationId: correlationId ?? "stub"));
            }

            return envelopes;
        }

        public FailureEnvelope FromException(
            FailureStage stage,
            string subsystem,
            Exception exception,
            string? correlationId = null)
        {
            return new FailureEnvelope(
                stage,
                subsystem,
                "EXCEPTION",
                RootCauseClass.Runtime,
                Retryable: false,
                UserAction: "inspect",
                Evidence: exception.Message,
                CorrelationId: correlationId ?? "stub");
        }
    }

    private sealed class TempDirectoryScope : IDisposable
    {
        public TempDirectoryScope()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "helper_parity_test_" + Guid.NewGuid().ToString("N"));
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
                // best effort
            }
        }
    }
}

