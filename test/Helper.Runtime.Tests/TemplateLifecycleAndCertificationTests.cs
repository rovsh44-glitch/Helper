using System.Text.Json;
using Helper.Runtime.Generation;
using Helper.Runtime.Infrastructure;

namespace Helper.Runtime.Tests;

public sealed class TemplateLifecycleAndCertificationTests
{
    [Fact]
    public async Task TemplateLifecycleService_ActivateAndRollback_UpdatesManagerResolution()
    {
        using var temp = new TempDirectoryScope();
        var templateRoot = Path.Combine(temp.Path, "Template_Math");
        var v1 = Path.Combine(templateRoot, "v1");
        var v2 = Path.Combine(templateRoot, "v2");
        Directory.CreateDirectory(v1);
        Directory.CreateDirectory(v2);
        await File.WriteAllTextAsync(Path.Combine(v1, "template.json"), JsonSerializer.Serialize(new
        {
            Id = "Template_Math",
            Name = "Math",
            Description = "v1",
            Language = "csharp",
            Version = "1.0.0"
        }));
        await File.WriteAllTextAsync(Path.Combine(v2, "template.json"), JsonSerializer.Serialize(new
        {
            Id = "Template_Math",
            Name = "Math",
            Description = "v2",
            Language = "csharp",
            Version = "2.0.0"
        }));

        var lifecycle = new TemplateLifecycleService(temp.Path);
        var manager = new ProjectTemplateManager(temp.Path);

        var activateV1 = await lifecycle.ActivateVersionAsync("Template_Math", "1.0.0");
        Assert.True(activateV1.Success);
        var resolvedV1 = await manager.GetTemplateByIdAsync("Template_Math");
        Assert.Equal("1.0.0", resolvedV1?.Version);

        var activateV2 = await lifecycle.ActivateVersionAsync("Template_Math", "2.0.0");
        Assert.True(activateV2.Success);
        var rollback = await lifecycle.RollbackAsync("Template_Math");
        Assert.True(rollback.Success);
        Assert.Equal("1.0.0", rollback.ActiveVersion);

        var resolvedAfterRollback = await manager.GetTemplateByIdAsync("Template_Math");
        Assert.Equal("1.0.0", resolvedAfterRollback?.Version);
    }

    [Fact]
    public async Task ParityCertificationService_GeneratesSnapshotReport_FromRunHistory()
    {
        using var temp = new TempDirectoryScope();
        var runsPath = Path.Combine(temp.Path, "generation_runs.jsonl");
        var now = DateTimeOffset.UtcNow;
        var lines = new[]
        {
            JsonSerializer.Serialize(new
            {
                StartedAtUtc = now.AddSeconds(-30),
                CompletedAtUtc = now.AddSeconds(-10),
                CompileGatePassed = true,
                Errors = Array.Empty<string>()
            }),
            JsonSerializer.Serialize(new
            {
                StartedAtUtc = now.AddSeconds(-60),
                CompletedAtUtc = now.AddSeconds(-20),
                CompileGatePassed = false,
                Errors = new[] { "unknown runtime issue" }
            })
        };
        await File.WriteAllLinesAsync(runsPath, lines);

        var generationMetrics = new GenerationMetricsService();
        generationMetrics.RecordGoldenTemplateRoute(true);
        generationMetrics.RecordGoldenTemplateRoute(false);
        var toolAudit = new ToolAuditService();
        toolAudit.Record(new ToolAuditEntry(DateTimeOffset.UtcNow, "helper.generate", "CHAT_EXECUTE", true));
        toolAudit.Record(new ToolAuditEntry(DateTimeOffset.UtcNow, "helper.generate", "CHAT_EXECUTE", false, "boom"));

        var service = new ParityCertificationService(generationMetrics, toolAudit, temp.Path, GenerationArtifactDiscoveryMode.WorkspaceOnly);
        var reportPath = Path.Combine(temp.Path, "report.md");
        var report = await service.GenerateAsync(reportPath);

        Assert.True(File.Exists(reportPath));
        Assert.Equal(2, report.TotalRuns);
        Assert.True(report.GoldenHitRate > 0 && report.GoldenHitRate < 1);
        Assert.True(report.UnknownErrorRate > 0);
        Assert.NotEmpty(report.Alerts);
    }

    [Fact]
    public async Task ParityCertificationService_UsesAggregatedProjectLogs_WhenRootLogIsStale()
    {
        using var temp = new TempDirectoryScope();
        var now = DateTimeOffset.UtcNow;
        var staleRootPath = Path.Combine(temp.Path, "generation_runs.jsonl");
        await File.WriteAllLinesAsync(staleRootPath, new[]
        {
            JsonSerializer.Serialize(new
            {
                RunId = "stale_root",
                StartedAtUtc = now.AddDays(-10),
                CompletedAtUtc = now.AddDays(-10).AddSeconds(30),
                CompileGatePassed = false,
                Errors = new[] { "old failure" }
            })
        });

        var projectRunsDir = Path.Combine(temp.Path, "PROJECTS", "demo", "generated_raw");
        Directory.CreateDirectory(projectRunsDir);
        var projectRunsPath = Path.Combine(projectRunsDir, "generation_runs.jsonl");
        await File.WriteAllLinesAsync(projectRunsPath, new[]
        {
            JsonSerializer.Serialize(new
            {
                RunId = "fresh_project",
                StartedAtUtc = now.AddSeconds(-40),
                CompletedAtUtc = now.AddSeconds(-10),
                CompileGatePassed = true,
                Errors = Array.Empty<string>(),
                GoldenTemplateMatched = false
            })
        });

        var generationMetrics = new GenerationMetricsService();
        var toolAudit = new ToolAuditService();
        toolAudit.Record(new ToolAuditEntry(DateTimeOffset.UtcNow, "helper.generate", "CHAT_EXECUTE", true));

        var service = new ParityCertificationService(generationMetrics, toolAudit, temp.Path, GenerationArtifactDiscoveryMode.WorkspaceOnly);
        var report = await service.GenerateAsync(Path.Combine(temp.Path, "report_stale.md"));

        Assert.Equal(1, report.TotalRuns);
        Assert.Equal(1.0, report.GenerationSuccessRate, 3);
    }

    [Fact]
    public async Task ParityCertificationService_UsesPersistedGoldenMatch_FromRunHistory()
    {
        using var temp = new TempDirectoryScope();
        var now = DateTimeOffset.UtcNow;
        var runsPath = Path.Combine(temp.Path, "generation_runs.jsonl");
        await File.WriteAllLinesAsync(runsPath, new[]
        {
            JsonSerializer.Serialize(new
            {
                RunId = "g1",
                StartedAtUtc = now.AddSeconds(-80),
                CompletedAtUtc = now.AddSeconds(-50),
                CompileGatePassed = true,
                Errors = Array.Empty<string>(),
                GoldenTemplateMatched = true,
                GoldenTemplateEligible = true
            }),
            JsonSerializer.Serialize(new
            {
                RunId = "g2",
                StartedAtUtc = now.AddSeconds(-40),
                CompletedAtUtc = now.AddSeconds(-20),
                CompileGatePassed = true,
                Errors = Array.Empty<string>(),
                GoldenTemplateMatched = false,
                GoldenTemplateEligible = true
            })
        });

        var generationMetrics = new GenerationMetricsService();
        var toolAudit = new ToolAuditService();
        toolAudit.Record(new ToolAuditEntry(DateTimeOffset.UtcNow, "helper.generate", "CHAT_EXECUTE", true));

        var service = new ParityCertificationService(generationMetrics, toolAudit, temp.Path, GenerationArtifactDiscoveryMode.WorkspaceOnly);
        var report = await service.GenerateAsync(Path.Combine(temp.Path, "report_golden.md"));

        Assert.Equal(0.5, report.GoldenHitRate, 3);
    }

    [Fact]
    public async Task ParityCertificationService_UsesGoldenEligibleDenominator_ForHitRate()
    {
        using var temp = new TempDirectoryScope();
        var now = DateTimeOffset.UtcNow;
        var runsPath = Path.Combine(temp.Path, "generation_runs.jsonl");
        await File.WriteAllLinesAsync(runsPath, new[]
        {
            JsonSerializer.Serialize(new
            {
                RunId = "nongolden",
                StartedAtUtc = now.AddSeconds(-120),
                CompletedAtUtc = now.AddSeconds(-110),
                CompileGatePassed = true,
                Errors = Array.Empty<string>(),
                GoldenTemplateMatched = false,
                GoldenTemplateEligible = false
            }),
            JsonSerializer.Serialize(new
            {
                RunId = "golden_hit",
                StartedAtUtc = now.AddSeconds(-90),
                CompletedAtUtc = now.AddSeconds(-80),
                CompileGatePassed = true,
                Errors = Array.Empty<string>(),
                GoldenTemplateMatched = true,
                GoldenTemplateEligible = true
            }),
            JsonSerializer.Serialize(new
            {
                RunId = "golden_miss",
                StartedAtUtc = now.AddSeconds(-60),
                CompletedAtUtc = now.AddSeconds(-50),
                CompileGatePassed = true,
                Errors = Array.Empty<string>(),
                GoldenTemplateMatched = false,
                GoldenTemplateEligible = true
            })
        });

        var previousMinAttempts = Environment.GetEnvironmentVariable("HELPER_PARITY_MIN_GOLDEN_ATTEMPTS");
        try
        {
            Environment.SetEnvironmentVariable("HELPER_PARITY_MIN_GOLDEN_ATTEMPTS", "2");
            var generationMetrics = new GenerationMetricsService();
            var toolAudit = new ToolAuditService();
            toolAudit.Record(new ToolAuditEntry(DateTimeOffset.UtcNow, "helper.generate", "CHAT_EXECUTE", true));

            var service = new ParityCertificationService(generationMetrics, toolAudit, temp.Path, GenerationArtifactDiscoveryMode.WorkspaceOnly);
            var report = await service.GenerateAsync(Path.Combine(temp.Path, "report_eligible.md"));

            Assert.Equal(2, report.GoldenAttempts);
            Assert.Equal(1, report.GoldenHits);
            Assert.Equal(0.5, report.GoldenHitRate, 3);
            Assert.False(report.GoldenSampleInsufficient);
        }
        finally
        {
            Environment.SetEnvironmentVariable("HELPER_PARITY_MIN_GOLDEN_ATTEMPTS", previousMinAttempts);
        }
    }

    [Fact]
    public async Task ParityCertificationService_ReportsInsufficientGoldenSample_WhenBelowMinimum()
    {
        using var temp = new TempDirectoryScope();
        var now = DateTimeOffset.UtcNow;
        var runsPath = Path.Combine(temp.Path, "generation_runs.jsonl");
        await File.WriteAllLinesAsync(runsPath, new[]
        {
            JsonSerializer.Serialize(new
            {
                RunId = "golden_only_one",
                StartedAtUtc = now.AddSeconds(-60),
                CompletedAtUtc = now.AddSeconds(-20),
                CompileGatePassed = true,
                Errors = Array.Empty<string>(),
                GoldenTemplateMatched = true,
                GoldenTemplateEligible = true
            })
        });

        var previousMinAttempts = Environment.GetEnvironmentVariable("HELPER_PARITY_MIN_GOLDEN_ATTEMPTS");
        try
        {
            Environment.SetEnvironmentVariable("HELPER_PARITY_MIN_GOLDEN_ATTEMPTS", "3");
            var generationMetrics = new GenerationMetricsService();
            var toolAudit = new ToolAuditService();
            toolAudit.Record(new ToolAuditEntry(DateTimeOffset.UtcNow, "helper.generate", "CHAT_EXECUTE", true));

            var service = new ParityCertificationService(generationMetrics, toolAudit, temp.Path, GenerationArtifactDiscoveryMode.WorkspaceOnly);
            var report = await service.GenerateAsync(Path.Combine(temp.Path, "report_insufficient_sample.md"));

            Assert.True(report.GoldenSampleInsufficient);
            Assert.Contains(report.Alerts, x => x.Contains("insufficient_golden_sample", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Environment.SetEnvironmentVariable("HELPER_PARITY_MIN_GOLDEN_ATTEMPTS", previousMinAttempts);
        }
    }

    [Fact]
    public async Task ParityCertificationService_DoesNotUseLegacyFallback_WhenEligibilityIsExplicitlyFalse()
    {
        using var temp = new TempDirectoryScope();
        var now = DateTimeOffset.UtcNow;
        var runsPath = Path.Combine(temp.Path, "generation_runs.jsonl");
        await File.WriteAllLinesAsync(runsPath, new[]
        {
            JsonSerializer.Serialize(new
            {
                RunId = "nongolden_1",
                StartedAtUtc = now.AddSeconds(-90),
                CompletedAtUtc = now.AddSeconds(-60),
                CompileGatePassed = true,
                Errors = Array.Empty<string>(),
                GoldenTemplateMatched = false,
                GoldenTemplateEligible = false
            }),
            JsonSerializer.Serialize(new
            {
                RunId = "nongolden_2",
                StartedAtUtc = now.AddSeconds(-50),
                CompletedAtUtc = now.AddSeconds(-20),
                CompileGatePassed = true,
                Errors = Array.Empty<string>(),
                GoldenTemplateMatched = false,
                GoldenTemplateEligible = false
            })
        });

        var generationMetrics = new GenerationMetricsService();
        var toolAudit = new ToolAuditService();
        toolAudit.Record(new ToolAuditEntry(DateTimeOffset.UtcNow, "helper.generate", "CHAT_EXECUTE", true));

        var service = new ParityCertificationService(generationMetrics, toolAudit, temp.Path, GenerationArtifactDiscoveryMode.WorkspaceOnly);
        var report = await service.GenerateAsync(Path.Combine(temp.Path, "report_non_golden_only.md"));

        Assert.Equal(0, report.GoldenAttempts);
        Assert.Equal(0, report.GoldenHits);
        Assert.True(report.GoldenSampleInsufficient);
        Assert.Contains(report.Alerts, x => x.Contains("insufficient_golden_sample", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ParityCertificationService_WorkloadFilter_ExcludesSmokeRuns()
    {
        using var temp = new TempDirectoryScope();
        var now = DateTimeOffset.UtcNow;
        var runsPath = Path.Combine(temp.Path, "generation_runs.jsonl");
        await File.WriteAllLinesAsync(runsPath, new[]
        {
            JsonSerializer.Serialize(new
            {
                RunId = "smoke_fail",
                StartedAtUtc = now.AddSeconds(-120),
                CompletedAtUtc = now.AddSeconds(-20),
                CompileGatePassed = false,
                Errors = new[] { "timeout" },
                WorkloadClass = "smoke",
                GoldenTemplateEligible = false,
                GoldenTemplateMatched = false
            }),
            JsonSerializer.Serialize(new
            {
                RunId = "parity_pass",
                StartedAtUtc = now.AddSeconds(-90),
                CompletedAtUtc = now.AddSeconds(-60),
                CompileGatePassed = true,
                Errors = Array.Empty<string>(),
                WorkloadClass = "parity",
                GoldenTemplateEligible = true,
                GoldenTemplateMatched = true
            })
        });

        var previousFilter = Environment.GetEnvironmentVariable("HELPER_PARITY_WORKLOAD_CLASSES");
        var previousMinAttempts = Environment.GetEnvironmentVariable("HELPER_PARITY_MIN_GOLDEN_ATTEMPTS");
        try
        {
            Environment.SetEnvironmentVariable("HELPER_PARITY_WORKLOAD_CLASSES", "parity");
            Environment.SetEnvironmentVariable("HELPER_PARITY_MIN_GOLDEN_ATTEMPTS", "1");
            var generationMetrics = new GenerationMetricsService();
            var toolAudit = new ToolAuditService();
            toolAudit.Record(new ToolAuditEntry(DateTimeOffset.UtcNow, "helper.generate", "CHAT_EXECUTE", true));

            var service = new ParityCertificationService(generationMetrics, toolAudit, temp.Path, GenerationArtifactDiscoveryMode.WorkspaceOnly);
            var report = await service.GenerateAsync(Path.Combine(temp.Path, "report_workload_filter.md"));

            Assert.Equal(1, report.TotalRuns);
            Assert.Equal(1.0, report.GenerationSuccessRate, 3);
            Assert.Equal(1.0, report.GoldenHitRate, 3);
        }
        finally
        {
            Environment.SetEnvironmentVariable("HELPER_PARITY_WORKLOAD_CLASSES", previousFilter);
            Environment.SetEnvironmentVariable("HELPER_PARITY_MIN_GOLDEN_ATTEMPTS", previousMinAttempts);
        }
    }

    [Fact]
    public async Task ParityCertificationService_UsesConfiguredProjectsRoot_WhenRunHistoryLivesUnderDataRoot()
    {
        using var temp = new TempDirectoryScope();
        var projectsRoot = Path.Combine(temp.Path, "HELPER_DATA", "PROJECTS");
        Directory.CreateDirectory(projectsRoot);
        var now = DateTimeOffset.UtcNow;
        var runsPath = Path.Combine(projectsRoot, "generation_runs.jsonl");
        await File.WriteAllLinesAsync(runsPath, new[]
        {
            JsonSerializer.Serialize(new
            {
                RunId = "canonical_root_run",
                StartedAtUtc = now.AddSeconds(-30),
                CompletedAtUtc = now.AddSeconds(-10),
                CompileGatePassed = true,
                Errors = Array.Empty<string>(),
                WorkloadClass = "parity",
                GoldenTemplateEligible = true,
                GoldenTemplateMatched = true
            })
        });

        var previousMinAttempts = Environment.GetEnvironmentVariable("HELPER_PARITY_MIN_GOLDEN_ATTEMPTS");
        try
        {
            Environment.SetEnvironmentVariable("HELPER_PARITY_MIN_GOLDEN_ATTEMPTS", "1");

            var generationMetrics = new GenerationMetricsService();
            var toolAudit = new ToolAuditService();
            toolAudit.Record(new ToolAuditEntry(DateTimeOffset.UtcNow, "helper.generate", "CHAT_EXECUTE", true));

            var discoveryOptions = GenerationArtifactDiscoveryOptions.Resolve(
                workspaceRoot: temp.Path,
                mode: GenerationArtifactDiscoveryMode.CanonicalDataRoot,
                canonicalProjectsRoot: projectsRoot);
            var service = new ParityCertificationService(generationMetrics, toolAudit, discoveryOptions);
            var report = await service.GenerateAsync(Path.Combine(temp.Path, "report_canonical_root.md"));

            Assert.Equal(1, report.TotalRuns);
            Assert.Equal(1.0, report.GenerationSuccessRate, 3);
            Assert.Equal(1.0, report.GoldenHitRate, 3);
        }
        finally
        {
            Environment.SetEnvironmentVariable("HELPER_PARITY_MIN_GOLDEN_ATTEMPTS", previousMinAttempts);
        }
    }

    [Fact]
    public async Task ParityCertificationService_CombinesCanonicalProjectsLog_AndLegacyDataRootLog()
    {
        using var temp = new TempDirectoryScope();
        var dataRoot = Path.Combine(temp.Path, "HELPER_DATA");
        var projectsRoot = Path.Combine(dataRoot, "PROJECTS");
        Directory.CreateDirectory(projectsRoot);
        var now = DateTimeOffset.UtcNow;
        var canonicalRunsPath = Path.Combine(projectsRoot, "generation_runs.jsonl");
        var legacyDataRootRunsPath = Path.Combine(dataRoot, "generation_runs.jsonl");

        await File.WriteAllLinesAsync(canonicalRunsPath, new[]
        {
            JsonSerializer.Serialize(new
            {
                RunId = "canonical_success",
                StartedAtUtc = now.AddSeconds(-40),
                CompletedAtUtc = now.AddSeconds(-10),
                CompileGatePassed = true,
                Errors = Array.Empty<string>(),
                WorkloadClass = "parity",
                GoldenTemplateEligible = true,
                GoldenTemplateMatched = true
            })
        });

        await File.WriteAllLinesAsync(legacyDataRootRunsPath, new[]
        {
            JsonSerializer.Serialize(new
            {
                RunId = "legacy_failure",
                StartedAtUtc = now.AddSeconds(-35),
                CompletedAtUtc = now.AddSeconds(-5),
                CompileGatePassed = false,
                Errors = new[] { "compile failure" },
                WorkloadClass = "parity",
                GoldenTemplateEligible = true,
                GoldenTemplateMatched = true
            })
        });

        var previousDataRoot = Environment.GetEnvironmentVariable("HELPER_DATA_ROOT");
        var previousProjectsRoot = Environment.GetEnvironmentVariable("HELPER_PROJECTS_ROOT");
        var previousMinAttempts = Environment.GetEnvironmentVariable("HELPER_PARITY_MIN_GOLDEN_ATTEMPTS");
        try
        {
            Environment.SetEnvironmentVariable("HELPER_DATA_ROOT", dataRoot);
            Environment.SetEnvironmentVariable("HELPER_PROJECTS_ROOT", projectsRoot);
            Environment.SetEnvironmentVariable("HELPER_PARITY_MIN_GOLDEN_ATTEMPTS", "1");

            var generationMetrics = new GenerationMetricsService();
            var toolAudit = new ToolAuditService();
            toolAudit.Record(new ToolAuditEntry(DateTimeOffset.UtcNow, "helper.generate", "CHAT_EXECUTE", true));

            var discoveryOptions = GenerationArtifactDiscoveryOptions.Resolve(
                workspaceRoot: temp.Path,
                mode: GenerationArtifactDiscoveryMode.CanonicalDataRoot,
                canonicalDataRoot: dataRoot,
                canonicalProjectsRoot: projectsRoot);
            var service = new ParityCertificationService(generationMetrics, toolAudit, discoveryOptions);
            var report = await service.GenerateAsync(Path.Combine(temp.Path, "report_combined_root.md"));

            Assert.Equal(2, report.TotalRuns);
            Assert.Equal(0.5, report.GenerationSuccessRate, 3);
            Assert.Equal(1.0, report.GoldenHitRate, 3);
        }
        finally
        {
            Environment.SetEnvironmentVariable("HELPER_DATA_ROOT", previousDataRoot);
            Environment.SetEnvironmentVariable("HELPER_PROJECTS_ROOT", previousProjectsRoot);
            Environment.SetEnvironmentVariable("HELPER_PARITY_MIN_GOLDEN_ATTEMPTS", previousMinAttempts);
        }
    }

    [Fact]
    public async Task ParityCertificationService_WritesDailySnapshot_ToConfiguredSnapshotRoot()
    {
        using var temp = new TempDirectoryScope();
        var now = DateTimeOffset.UtcNow;
        var runsPath = Path.Combine(temp.Path, "generation_runs.jsonl");
        await File.WriteAllLinesAsync(runsPath, new[]
        {
            JsonSerializer.Serialize(new
            {
                RunId = "preview_parity_run",
                StartedAtUtc = now.AddSeconds(-30),
                CompletedAtUtc = now.AddSeconds(-10),
                CompileGatePassed = true,
                Errors = Array.Empty<string>(),
                WorkloadClass = "preview-parity-day-02",
                GoldenTemplateEligible = true,
                GoldenTemplateMatched = true
            })
        });

        var previousFilter = Environment.GetEnvironmentVariable("HELPER_PARITY_WORKLOAD_CLASSES");
        var previousMinAttempts = Environment.GetEnvironmentVariable("HELPER_PARITY_MIN_GOLDEN_ATTEMPTS");
        var previousSnapshotRoot = Environment.GetEnvironmentVariable("HELPER_PARITY_SNAPSHOT_ROOT");
        try
        {
            var previewSnapshotRoot = Path.Combine(temp.Path, "doc", "parity_preview_day02");
            Environment.SetEnvironmentVariable("HELPER_PARITY_WORKLOAD_CLASSES", "preview-parity-day-02");
            Environment.SetEnvironmentVariable("HELPER_PARITY_MIN_GOLDEN_ATTEMPTS", "1");
            Environment.SetEnvironmentVariable("HELPER_PARITY_SNAPSHOT_ROOT", previewSnapshotRoot);

            var generationMetrics = new GenerationMetricsService();
            var toolAudit = new ToolAuditService();
            toolAudit.Record(new ToolAuditEntry(DateTimeOffset.UtcNow, "helper.generate", "CHAT_EXECUTE", true));

            var service = new ParityCertificationService(generationMetrics, toolAudit, temp.Path, GenerationArtifactDiscoveryMode.WorkspaceOnly);
            await service.GenerateAsync(Path.Combine(temp.Path, "preview_report.md"));

            var dateKey = now.UtcDateTime.ToString("yyyy-MM-dd");
            Assert.True(File.Exists(Path.Combine(previewSnapshotRoot, "daily", $"parity_{dateKey}.json")));
            Assert.False(File.Exists(Path.Combine(temp.Path, "doc", "parity_nightly", "daily", $"parity_{dateKey}.json")));
        }
        finally
        {
            Environment.SetEnvironmentVariable("HELPER_PARITY_WORKLOAD_CLASSES", previousFilter);
            Environment.SetEnvironmentVariable("HELPER_PARITY_MIN_GOLDEN_ATTEMPTS", previousMinAttempts);
            Environment.SetEnvironmentVariable("HELPER_PARITY_SNAPSHOT_ROOT", previousSnapshotRoot);
        }
    }

    private sealed class TempDirectoryScope : IDisposable
    {
        public TempDirectoryScope()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "helper_lifecycle_test_" + Guid.NewGuid().ToString("N"));
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


