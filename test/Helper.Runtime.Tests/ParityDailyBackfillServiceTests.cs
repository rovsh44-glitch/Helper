using System.Text.Json;
using Helper.Runtime.Generation;

namespace Helper.Runtime.Tests;

public sealed class ParityDailyBackfillServiceTests
{
    [Fact]
    public async Task BackfillAsync_WritesDailySnapshots_FromGenerationRunLogs()
    {
        using var temp = new TempDirectoryScope();
        var runsPath = Path.Combine(temp.Path, "generation_runs.jsonl");
        await File.WriteAllLinesAsync(runsPath, new[]
        {
            JsonSerializer.Serialize(new
            {
                RunId = "d1_hit",
                StartedAtUtc = "2026-03-01T10:00:00Z",
                CompletedAtUtc = "2026-03-01T10:00:20Z",
                CompileGatePassed = true,
                Errors = Array.Empty<string>(),
                GoldenTemplateEligible = true,
                GoldenTemplateMatched = true,
                WorkloadClass = "parity"
            }),
            JsonSerializer.Serialize(new
            {
                RunId = "d1_miss",
                StartedAtUtc = "2026-03-01T11:00:00Z",
                CompletedAtUtc = "2026-03-01T11:00:40Z",
                CompileGatePassed = true,
                Errors = Array.Empty<string>(),
                GoldenTemplateEligible = true,
                GoldenTemplateMatched = false,
                WorkloadClass = "parity"
            }),
            JsonSerializer.Serialize(new
            {
                RunId = "d2_fail",
                StartedAtUtc = "2026-03-02T09:00:00Z",
                CompletedAtUtc = "2026-03-02T09:01:00Z",
                CompileGatePassed = false,
                Errors = new[] { "unknown runtime issue" },
                GoldenTemplateEligible = true,
                GoldenTemplateMatched = false,
                WorkloadClass = "parity"
            }),
            JsonSerializer.Serialize(new
            {
                RunId = "d2_smoke",
                StartedAtUtc = "2026-03-02T09:02:00Z",
                CompletedAtUtc = "2026-03-02T09:02:20Z",
                CompileGatePassed = true,
                Errors = Array.Empty<string>(),
                GoldenTemplateEligible = false,
                GoldenTemplateMatched = false,
                WorkloadClass = "smoke"
            })
        });

        var prevMinAttempts = Environment.GetEnvironmentVariable("HELPER_PARITY_MIN_GOLDEN_ATTEMPTS");
        try
        {
            Environment.SetEnvironmentVariable("HELPER_PARITY_MIN_GOLDEN_ATTEMPTS", "1");
            var service = new ParityDailyBackfillService(temp.Path, GenerationArtifactDiscoveryMode.WorkspaceOnly);
            var report = await service.BackfillAsync(new ParityDailyBackfillRequest(
                WorkloadClasses: "parity",
                OverwriteExisting: true), CancellationToken.None);

            Assert.Equal(2, report.DaysConsidered);
            Assert.Equal(2, report.DaysWritten);

            var day1Path = Path.Combine(temp.Path, "doc", "parity_nightly", "daily", "parity_2026-03-01.json");
            var day2Path = Path.Combine(temp.Path, "doc", "parity_nightly", "daily", "parity_2026-03-02.json");
            Assert.True(File.Exists(day1Path));
            Assert.True(File.Exists(day2Path));

            var day1 = JsonSerializer.Deserialize<ParityDailySnapshot>(await File.ReadAllTextAsync(day1Path));
            var day2 = JsonSerializer.Deserialize<ParityDailySnapshot>(await File.ReadAllTextAsync(day2Path));

            Assert.NotNull(day1);
            Assert.Equal(2, day1!.TotalRuns);
            Assert.Equal(1, day1.GoldenHits);
            Assert.Equal(2, day1.GoldenAttempts);
            Assert.Equal(0.5, day1.GoldenHitRate, 3);
            Assert.Equal(1.0, day1.GenerationSuccessRate, 3);

            Assert.NotNull(day2);
            Assert.Equal(1, day2!.TotalRuns);
            Assert.Equal(0.0, day2.GenerationSuccessRate, 3);
            Assert.True(day2.UnknownErrorRate > 0);
        }
        finally
        {
            Environment.SetEnvironmentVariable("HELPER_PARITY_MIN_GOLDEN_ATTEMPTS", prevMinAttempts);
        }
    }

    [Fact]
    public async Task BackfillAsync_SkipsExistingDailySnapshot_WhenOverwriteDisabled()
    {
        using var temp = new TempDirectoryScope();
        var dailyDir = Path.Combine(temp.Path, "doc", "parity_nightly", "daily");
        Directory.CreateDirectory(dailyDir);
        var existingPath = Path.Combine(dailyDir, "parity_2026-03-01.json");
        var existingSnapshot = new ParityDailySnapshot(
            DateUtc: "2026-03-01",
            GeneratedAtUtc: DateTimeOffset.Parse("2026-03-01T00:00:00Z"),
            TotalRuns: 999,
            GoldenHitRate: 0.99,
            GenerationSuccessRate: 0.99,
            P95ReadySeconds: 1.0,
            UnknownErrorRate: 0.0,
            ToolSuccessRatio: 0.99,
            Alerts: Array.Empty<string>(),
            GoldenAttempts: 999,
            GoldenHits: 998,
            MinGoldenAttemptsRequired: 1,
            GoldenSampleInsufficient: false);
        await File.WriteAllTextAsync(existingPath, JsonSerializer.Serialize(existingSnapshot, new JsonSerializerOptions { WriteIndented = true }));

        var runsPath = Path.Combine(temp.Path, "generation_runs.jsonl");
        await File.WriteAllLinesAsync(runsPath, new[]
        {
            JsonSerializer.Serialize(new
            {
                RunId = "d1_hit",
                StartedAtUtc = "2026-03-01T10:00:00Z",
                CompletedAtUtc = "2026-03-01T10:00:20Z",
                CompileGatePassed = true,
                Errors = Array.Empty<string>(),
                GoldenTemplateEligible = true,
                GoldenTemplateMatched = true,
                WorkloadClass = "parity"
            })
        });

        var prevMinAttempts = Environment.GetEnvironmentVariable("HELPER_PARITY_MIN_GOLDEN_ATTEMPTS");
        try
        {
            Environment.SetEnvironmentVariable("HELPER_PARITY_MIN_GOLDEN_ATTEMPTS", "1");
            var service = new ParityDailyBackfillService(temp.Path, GenerationArtifactDiscoveryMode.WorkspaceOnly);
            var report = await service.BackfillAsync(new ParityDailyBackfillRequest(
                WorkloadClasses: "parity",
                OverwriteExisting: false), CancellationToken.None);

            Assert.Single(report.Days);
            Assert.Equal("skipped_existing", report.Days[0].Action);
            Assert.Equal(0, report.DaysWritten);
            Assert.Equal(1, report.DaysSkipped);

            var after = JsonSerializer.Deserialize<ParityDailySnapshot>(await File.ReadAllTextAsync(existingPath));
            Assert.NotNull(after);
            Assert.Equal(999, after!.TotalRuns);
        }
        finally
        {
            Environment.SetEnvironmentVariable("HELPER_PARITY_MIN_GOLDEN_ATTEMPTS", prevMinAttempts);
        }
    }

    [Fact]
    public async Task BackfillAsync_UsesConfiguredProjectsRoot_WhenCanonicalRunsLiveUnderDataRoot()
    {
        using var temp = new TempDirectoryScope();
        var projectsRoot = Path.Combine(temp.Path, "HELPER_DATA", "PROJECTS");
        Directory.CreateDirectory(projectsRoot);
        var runsPath = Path.Combine(projectsRoot, "generation_runs.jsonl");
        await File.WriteAllLinesAsync(runsPath, new[]
        {
            JsonSerializer.Serialize(new
            {
                RunId = "canonical_day_hit",
                StartedAtUtc = "2026-03-03T10:00:00Z",
                CompletedAtUtc = "2026-03-03T10:00:20Z",
                CompileGatePassed = true,
                Errors = Array.Empty<string>(),
                GoldenTemplateEligible = true,
                GoldenTemplateMatched = true,
                WorkloadClass = "parity"
            })
        });

        var prevMinAttempts = Environment.GetEnvironmentVariable("HELPER_PARITY_MIN_GOLDEN_ATTEMPTS");
        try
        {
            Environment.SetEnvironmentVariable("HELPER_PARITY_MIN_GOLDEN_ATTEMPTS", "1");

            var discoveryOptions = GenerationArtifactDiscoveryOptions.Resolve(
                workspaceRoot: temp.Path,
                mode: GenerationArtifactDiscoveryMode.CanonicalDataRoot,
                canonicalProjectsRoot: projectsRoot);
            var service = new ParityDailyBackfillService(discoveryOptions);
            var report = await service.BackfillAsync(new ParityDailyBackfillRequest(
                WorkloadClasses: "parity",
                OverwriteExisting: true), CancellationToken.None);

            Assert.Single(report.Days);
            Assert.Equal(1, report.DaysWritten);

            var dayPath = Path.Combine(temp.Path, "doc", "parity_nightly", "daily", "parity_2026-03-03.json");
            Assert.True(File.Exists(dayPath));

            var day = JsonSerializer.Deserialize<ParityDailySnapshot>(await File.ReadAllTextAsync(dayPath));
            Assert.NotNull(day);
            Assert.Equal(1, day!.TotalRuns);
            Assert.Equal(1.0, day.GenerationSuccessRate, 3);
            Assert.Equal(1.0, day.GoldenHitRate, 3);
        }
        finally
        {
            Environment.SetEnvironmentVariable("HELPER_PARITY_MIN_GOLDEN_ATTEMPTS", prevMinAttempts);
        }
    }

    [Fact]
    public async Task BackfillAsync_WritesSnapshots_ToConfiguredSnapshotRoot()
    {
        using var temp = new TempDirectoryScope();
        var runsPath = Path.Combine(temp.Path, "generation_runs.jsonl");
        await File.WriteAllLinesAsync(runsPath, new[]
        {
            JsonSerializer.Serialize(new
            {
                RunId = "preview_day02_hit",
                StartedAtUtc = "2026-03-08T10:00:00Z",
                CompletedAtUtc = "2026-03-08T10:00:20Z",
                CompileGatePassed = true,
                Errors = Array.Empty<string>(),
                GoldenTemplateEligible = true,
                GoldenTemplateMatched = true,
                WorkloadClass = "preview-parity-day-02"
            })
        });

        var previousMinAttempts = Environment.GetEnvironmentVariable("HELPER_PARITY_MIN_GOLDEN_ATTEMPTS");
        var previousSnapshotRoot = Environment.GetEnvironmentVariable("HELPER_PARITY_SNAPSHOT_ROOT");
        try
        {
            var previewSnapshotRoot = Path.Combine(temp.Path, "doc", "parity_preview_day02");
            Environment.SetEnvironmentVariable("HELPER_PARITY_MIN_GOLDEN_ATTEMPTS", "1");
            Environment.SetEnvironmentVariable("HELPER_PARITY_SNAPSHOT_ROOT", previewSnapshotRoot);

            var service = new ParityDailyBackfillService(temp.Path, GenerationArtifactDiscoveryMode.WorkspaceOnly);
            var report = await service.BackfillAsync(new ParityDailyBackfillRequest(
                WorkloadClasses: "preview-parity-day-02",
                OverwriteExisting: true), CancellationToken.None);

            Assert.Single(report.Days);
            Assert.True(File.Exists(Path.Combine(previewSnapshotRoot, "daily", "parity_2026-03-08.json")));
            Assert.False(File.Exists(Path.Combine(temp.Path, "doc", "parity_nightly", "daily", "parity_2026-03-08.json")));
        }
        finally
        {
            Environment.SetEnvironmentVariable("HELPER_PARITY_MIN_GOLDEN_ATTEMPTS", previousMinAttempts);
            Environment.SetEnvironmentVariable("HELPER_PARITY_SNAPSHOT_ROOT", previousSnapshotRoot);
        }
    }

    private sealed class TempDirectoryScope : IDisposable
    {
        public TempDirectoryScope()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "helper_parity_backfill_test_" + Guid.NewGuid().ToString("N"));
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

