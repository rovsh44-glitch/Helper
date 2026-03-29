using System.Text.Json;
using Helper.Runtime.Generation;
using Helper.Runtime.Infrastructure;

namespace Helper.Runtime.Tests;

public sealed class ParityDiscoveryAndOutcomeTests
{
    [Fact]
    public void EnumerateRunHistoryFiles_ReturnsExpectedFiles_ForEachDiscoveryMode()
    {
        using var temp = new TempDirectoryScope();
        var workspaceRoot = temp.Path;
        var workspaceRootLog = CreateLog(workspaceRoot, "generation_runs.jsonl");
        var workspaceProjectLog = CreateLog(Path.Combine(workspaceRoot, "PROJECTS", "demo"), "generation_runs.jsonl");
        var canonicalDataRoot = Path.Combine(workspaceRoot, "HELPER_DATA");
        var canonicalDataRootLog = CreateLog(canonicalDataRoot, "generation_runs.jsonl");
        var canonicalProjectsRootLog = CreateLog(Path.Combine(canonicalDataRoot, "PROJECTS"), "generation_runs.jsonl");

        var workspaceOnly = GenerationArtifactLocator.EnumerateRunHistoryFiles(
            GenerationArtifactDiscoveryOptions.Resolve(
                workspaceRoot: workspaceRoot,
                mode: GenerationArtifactDiscoveryMode.WorkspaceOnly,
                canonicalDataRoot: canonicalDataRoot,
                canonicalProjectsRoot: Path.Combine(canonicalDataRoot, "PROJECTS")));
        var canonicalOnly = GenerationArtifactLocator.EnumerateRunHistoryFiles(
            GenerationArtifactDiscoveryOptions.Resolve(
                workspaceRoot: workspaceRoot,
                mode: GenerationArtifactDiscoveryMode.CanonicalDataRoot,
                canonicalDataRoot: canonicalDataRoot,
                canonicalProjectsRoot: Path.Combine(canonicalDataRoot, "PROJECTS")));
        var legacyOnly = GenerationArtifactLocator.EnumerateRunHistoryFiles(
            GenerationArtifactDiscoveryOptions.Resolve(
                workspaceRoot: workspaceRoot,
                mode: GenerationArtifactDiscoveryMode.LegacyFallback,
                canonicalDataRoot: canonicalDataRoot,
                canonicalProjectsRoot: Path.Combine(canonicalDataRoot, "PROJECTS")));
        var mixed = GenerationArtifactLocator.EnumerateRunHistoryFiles(
            GenerationArtifactDiscoveryOptions.Resolve(
                workspaceRoot: workspaceRoot,
                mode: GenerationArtifactDiscoveryMode.Mixed,
                canonicalDataRoot: canonicalDataRoot,
                canonicalProjectsRoot: Path.Combine(canonicalDataRoot, "PROJECTS")));

        Assert.Equal(
            new[] { workspaceProjectLog, workspaceRootLog }.OrderBy(x => x, StringComparer.OrdinalIgnoreCase),
            workspaceOnly.OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
        Assert.Equal(
            new[] { canonicalDataRootLog, canonicalProjectsRootLog }.OrderBy(x => x, StringComparer.OrdinalIgnoreCase),
            canonicalOnly.OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
        Assert.Equal(
            new[] { workspaceProjectLog, workspaceRootLog }.OrderBy(x => x, StringComparer.OrdinalIgnoreCase),
            legacyOnly.OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
        Assert.Equal(
            new[] { canonicalDataRootLog, canonicalProjectsRootLog, workspaceProjectLog, workspaceRootLog }.OrderBy(x => x, StringComparer.OrdinalIgnoreCase),
            mixed.OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ParityCertificationService_ExplicitWorkspaceRoot_DefaultsToWorkspaceOnly()
    {
        using var temp = new TempDirectoryScope();
        var workspaceRunsPath = Path.Combine(temp.Path, "generation_runs.jsonl");
        var now = DateTimeOffset.UtcNow;
        await File.WriteAllLinesAsync(workspaceRunsPath, new[]
        {
            JsonSerializer.Serialize(new
            {
                RunId = "workspace_success",
                StartedAtUtc = now.AddSeconds(-30),
                CompletedAtUtc = now.AddSeconds(-10),
                CompileGatePassed = true,
                Errors = Array.Empty<string>(),
                GoldenTemplateEligible = true,
                GoldenTemplateMatched = true,
                WorkloadClass = "parity"
            })
        });

        var externalDataRoot = Path.Combine(temp.Path, "external_data");
        var externalProjectsRoot = Path.Combine(externalDataRoot, "PROJECTS");
        Directory.CreateDirectory(externalProjectsRoot);
        await File.WriteAllLinesAsync(Path.Combine(externalProjectsRoot, "generation_runs.jsonl"), new[]
        {
            JsonSerializer.Serialize(new
            {
                RunId = "external_failure",
                StartedAtUtc = now.AddSeconds(-50),
                CompletedAtUtc = now.AddSeconds(-20),
                CompileGatePassed = false,
                Errors = new[] { "compile failure" },
                GoldenTemplateEligible = true,
                GoldenTemplateMatched = true,
                WorkloadClass = "parity"
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
                canonicalDataRoot: externalDataRoot,
                canonicalProjectsRoot: externalProjectsRoot);
            var service = new ParityCertificationService(generationMetrics, toolAudit, discoveryOptions);
            var report = await service.GenerateAsync(Path.Combine(temp.Path, "workspace_only_default.md"));

            Assert.Equal(1, report.TotalRuns);
            Assert.Equal(1.0, report.GenerationSuccessRate, 3);
        }
        finally
        {
            Environment.SetEnvironmentVariable("HELPER_PARITY_MIN_GOLDEN_ATTEMPTS", previousMinAttempts);
        }
    }

    [Fact]
    public async Task ParityCertificationService_ClassifiesTemplateBlockedFailure_AsRoutingMismatch()
    {
        using var temp = new TempDirectoryScope();
        var now = DateTimeOffset.UtcNow;
        await File.WriteAllLinesAsync(Path.Combine(temp.Path, "generation_runs.jsonl"), new[]
        {
            JsonSerializer.Serialize(new
            {
                RunId = "blocked_template_run",
                Prompt = "Generate a PDF to EPUB and EPUB to PDF converter in C#.",
                RoutedTemplateId = "Template_PdfEpubConverter",
                StartedAtUtc = now.AddSeconds(-60),
                CompletedAtUtc = now.AddSeconds(-20),
                CompileGatePassed = false,
                Errors = new[] { "[TEMPLATE_BLOCKED_BY_CERTIFICATION_STATUS] Template excluded by stale certification state." },
                GoldenTemplateEligible = true,
                GoldenTemplateMatched = true,
                WorkloadClass = "parity"
            })
        });

        var previousMinAttempts = Environment.GetEnvironmentVariable("HELPER_PARITY_MIN_GOLDEN_ATTEMPTS");
        try
        {
            Environment.SetEnvironmentVariable("HELPER_PARITY_MIN_GOLDEN_ATTEMPTS", "1");
            var generationMetrics = new GenerationMetricsService();
            var toolAudit = new ToolAuditService();
            toolAudit.Record(new ToolAuditEntry(DateTimeOffset.UtcNow, "helper.generate", "CHAT_EXECUTE", true));

            var reportPath = Path.Combine(temp.Path, "taxonomy_report.md");
            var discoveryOptions = GenerationArtifactDiscoveryOptions.Resolve(temp.Path, GenerationArtifactDiscoveryMode.WorkspaceOnly);
            var service = new ParityCertificationService(generationMetrics, toolAudit, discoveryOptions);
            var report = await service.GenerateAsync(reportPath);
            var markdown = await File.ReadAllTextAsync(reportPath);
            var snapshot = await File.ReadAllTextAsync(Path.ChangeExtension(reportPath, ".json"));

            Assert.Equal(1, report.TotalRuns);
            Assert.Equal(0.0, report.GenerationSuccessRate, 3);
            Assert.Contains("routing_mismatch: 1", markdown, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("blocked_template_run", markdown, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("\"Mode\": \"WorkspaceOnly\"", snapshot, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("\"routing_mismatch\": 1", snapshot, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Environment.SetEnvironmentVariable("HELPER_PARITY_MIN_GOLDEN_ATTEMPTS", previousMinAttempts);
        }
    }

    [Fact]
    public async Task ParityCertificationService_TreatsSemanticFallbackArtifacts_AsFailure()
    {
        using var temp = new TempDirectoryScope();
        var now = DateTimeOffset.UtcNow;
        await File.WriteAllLinesAsync(Path.Combine(temp.Path, "generation_runs.jsonl"), new[]
        {
            JsonSerializer.Serialize(new
            {
                RunId = "semantic_fallback_run",
                StartedAtUtc = now.AddSeconds(-40),
                CompletedAtUtc = now.AddSeconds(-10),
                CompileGatePassed = true,
                Errors = Array.Empty<string>(),
                Warnings = new[] { "fallback method body injected for unresolved symbol." },
                PlaceholderFindings = new[] { "TODO placeholder remained in MainWindow.xaml.cs" },
                GoldenTemplateEligible = true,
                GoldenTemplateMatched = true,
                WorkloadClass = "parity"
            })
        });

        var previousMinAttempts = Environment.GetEnvironmentVariable("HELPER_PARITY_MIN_GOLDEN_ATTEMPTS");
        try
        {
            Environment.SetEnvironmentVariable("HELPER_PARITY_MIN_GOLDEN_ATTEMPTS", "1");
            var generationMetrics = new GenerationMetricsService();
            var toolAudit = new ToolAuditService();
            toolAudit.Record(new ToolAuditEntry(DateTimeOffset.UtcNow, "helper.generate", "CHAT_EXECUTE", true));

            var reportPath = Path.Combine(temp.Path, "semantic_report.md");
            var discoveryOptions = GenerationArtifactDiscoveryOptions.Resolve(temp.Path, GenerationArtifactDiscoveryMode.WorkspaceOnly);
            var service = new ParityCertificationService(generationMetrics, toolAudit, discoveryOptions);
            var report = await service.GenerateAsync(reportPath);
            var markdown = await File.ReadAllTextAsync(reportPath);
            var snapshot = await File.ReadAllTextAsync(Path.ChangeExtension(reportPath, ".json"));

            Assert.Equal(1, report.TotalRuns);
            Assert.Equal(0.0, report.GenerationSuccessRate, 3);
            Assert.Contains("semantic_fallback_overuse: 1", markdown, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Degraded Success Runs: 1", markdown, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Generation success rate below target", markdown, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("\"DegradedSuccessRuns\": 1", snapshot, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Environment.SetEnvironmentVariable("HELPER_PARITY_MIN_GOLDEN_ATTEMPTS", previousMinAttempts);
        }
    }

    private static string CreateLog(string directory, string fileName)
    {
        Directory.CreateDirectory(directory);
        var path = Path.GetFullPath(Path.Combine(directory, fileName));
        File.WriteAllText(path, "{}");
        return path;
    }

    private sealed class TempDirectoryScope : IDisposable
    {
        public TempDirectoryScope()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "helper_parity_discovery_" + Guid.NewGuid().ToString("N"));
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
                // best effort cleanup
            }
        }
    }
}

