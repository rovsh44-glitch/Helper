using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Helper.Runtime.Core;
using Helper.Runtime.Generation;

namespace Helper.Runtime.Infrastructure
{
    public partial class HelperOrchestrator
    {
        private async Task WriteGoldenRouteRunReportAsync(
            GenerationRequest request,
            TemplateRoutingDecision route,
            GenerationResult result,
            TimeSpan routingDuration,
            TimeSpan forgeDuration,
            CancellationToken ct)
        {
            var isGoldenEligible = GoldenTemplateIntentClassifier.HasExplicitGoldenTemplateRequest(request.Prompt);
            var workloadClass = GenerationWorkloadClassifier.Resolve(request.Prompt);
            var completedAtUtc = DateTime.UtcNow;
            var startedAtUtc = completedAtUtc - result.Duration;
            var rawProjectRoot = ResolveRawProjectRoot(result.ProjectPath, request.OutputPath);
            var errorLines = result.Errors.Select(FormatBuildError).Where(static x => !string.IsNullOrWhiteSpace(x)).ToList();
            var projectName = ResolveProjectName(rawProjectRoot, route.TemplateId);
            var runId = BuildRunId(request.SessionId);
            var artifactValidationPassed = false;
            var smokePassed = false;
            IReadOnlyList<TemplateCertificationSmokeScenario> smokeScenarios = Array.Empty<TemplateCertificationSmokeScenario>();
            var stageDurations = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["routing"] = Math.Round(routingDuration.TotalSeconds, 3),
                ["forge"] = Math.Round(forgeDuration.TotalSeconds, 3)
            };

            if (result.Success && Directory.Exists(rawProjectRoot))
            {
                var artifactValidator = new ForgeArtifactValidator();
                var artifact = await artifactValidator.ValidateAsync(rawProjectRoot, result.Errors, ct);
                artifactValidationPassed = artifact.Success;
                if (!artifact.Success && !string.IsNullOrWhiteSpace(artifact.Reason))
                {
                    errorLines.Add($"ArtifactValidation: {artifact.Reason}");
                }

                var metadata = await TemplateMetadataReader.TryLoadAsync(rawProjectRoot, ct);
                smokeScenarios = await TemplateSmokeScenarioRunner.EvaluateAsync(rawProjectRoot, metadata, result.Success, artifact.Success, ct);
                smokePassed = smokeScenarios.All(x => x.Passed);
                if (!smokePassed)
                {
                    errorLines.AddRange(smokeScenarios.Where(x => !x.Passed).Select(x => $"Smoke[{x.Id}]: {x.Details}"));
                }
            }

            var report = new GenerationRunReport(
                RunId: runId,
                Prompt: request.Prompt,
                ModelRoute: "forge/golden-template",
                ProjectName: projectName,
                StartedAtUtc: startedAtUtc,
                CompletedAtUtc: completedAtUtc,
                RawProjectRoot: rawProjectRoot,
                ValidatedProjectRoot: result.Success && artifactValidationPassed && smokePassed ? rawProjectRoot : null,
                FileCount: result.Files.Count,
                MethodCount: 0,
                RetryCount: result.HealAttempts,
                BlueprintAccepted: true,
                CompileGatePassed: result.Success,
                Errors: errorLines,
                Warnings: Array.Empty<string>(),
                RouteMatched: true,
                RoutedTemplateId: route.TemplateId,
                RouteConfidence: route.Confidence,
                GoldenTemplateMatched: true,
                GoldenTemplateEligible: isGoldenEligible,
                WorkloadClass: workloadClass,
                StageDurationsSec: stageDurations,
                ArtifactValidationPassed: artifactValidationPassed,
                SmokePassed: smokePassed,
                SmokeScenarios: smokeScenarios);

            RecordGenerationRouteTelemetry(report);
            await PersistRunReportSafelyAsync(report, ct);
        }

        private static bool HasCompileErrors(IReadOnlyList<BuildError> errors)
            => errors.Any(error =>
                !string.IsNullOrWhiteSpace(error.Code) &&
                (error.Code.StartsWith("CS", StringComparison.OrdinalIgnoreCase) ||
                 error.Code.StartsWith("MSB", StringComparison.OrdinalIgnoreCase)));

        private static string BuildRunId(string? sessionId, string defaultPrefix = "golden_route")
        {
            var prefix = string.IsNullOrWhiteSpace(sessionId) ? defaultPrefix : sessionId.Trim();
            return $"{prefix}_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}";
        }

        private static string ResolveProjectName(string rawProjectRoot, string? fallbackTemplateId)
        {
            var name = Path.GetFileName(rawProjectRoot);
            if (!string.IsNullOrWhiteSpace(name))
            {
                return name;
            }

            return string.IsNullOrWhiteSpace(fallbackTemplateId)
                ? "GoldenTemplateProject"
                : fallbackTemplateId;
        }

        private static string ResolveRawProjectRoot(string projectPath, string fallbackOutputPath)
        {
            if (!string.IsNullOrWhiteSpace(projectPath) && !string.Equals(projectPath, "ERROR", StringComparison.OrdinalIgnoreCase))
            {
                return Path.GetFullPath(projectPath);
            }

            if (!string.IsNullOrWhiteSpace(fallbackOutputPath))
            {
                return Path.GetFullPath(fallbackOutputPath);
            }

            return HelperWorkspacePathResolver.ResolveProjectsPath($"golden_route_failed_{Guid.NewGuid():N}");
        }

        private static string FormatBuildError(BuildError error)
            => string.IsNullOrWhiteSpace(error.Code) ? error.Message : $"[{error.Code}] {error.Message}";

        private async Task PersistTimeoutRunReportAsync(
            GenerationRequest request,
            string errorCode,
            string errorMessage,
            GenerationTimeoutStage timeoutStage,
            TimeSpan elapsed,
            CancellationToken ct)
        {
            var completedAtUtc = DateTime.UtcNow;
            var startedAtUtc = completedAtUtc - elapsed;
            var rawProjectRoot = ResolveRawProjectRoot(request.OutputPath, request.OutputPath);
            var projectName = ResolveProjectName(rawProjectRoot, fallbackTemplateId: null);
            var isGoldenEligible = GoldenTemplateIntentClassifier.HasExplicitGoldenTemplateRequest(request.Prompt);
            var workloadClass = GenerationWorkloadClassifier.Resolve(request.Prompt);
            var stageDurations = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["total_elapsed"] = Math.Round(Math.Max(0, elapsed.TotalSeconds), 3)
            };

            var stageKey = timeoutStage.ToString().ToLowerInvariant();
            stageDurations[$"timeout_{stageKey}"] = Math.Round(Math.Max(0, elapsed.TotalSeconds), 3);

            var report = new GenerationRunReport(
                RunId: BuildRunId(request.SessionId, "timeout"),
                Prompt: request.Prompt,
                ModelRoute: "helper/timeout",
                ProjectName: projectName,
                StartedAtUtc: startedAtUtc,
                CompletedAtUtc: completedAtUtc,
                RawProjectRoot: rawProjectRoot,
                ValidatedProjectRoot: null,
                FileCount: 0,
                MethodCount: 0,
                RetryCount: 0,
                BlueprintAccepted: false,
                CompileGatePassed: false,
                Errors: new[] { $"[{errorCode}] {errorMessage}" },
                Warnings: Array.Empty<string>(),
                RouteMatched: null,
                RoutedTemplateId: null,
                RouteConfidence: null,
                GoldenTemplateMatched: null,
                GoldenTemplateEligible: isGoldenEligible,
                WorkloadClass: workloadClass,
                StageDurationsSec: stageDurations);

            RecordGenerationRouteTelemetry(report);
            await PersistRunReportSafelyAsync(report, ct);
        }

        private async Task<GenerationResult> BuildGoldenRouteMismatchResultAsync(
            GenerationRequest request,
            TemplateRoutingDecision route,
            string expectedTemplateId,
            string reason,
            TimeSpan elapsed,
            CancellationToken ct)
        {
            var completedAtUtc = DateTime.UtcNow;
            var startedAtUtc = completedAtUtc - elapsed;
            var rawProjectRoot = ResolveRawProjectRoot(request.OutputPath, request.OutputPath);
            var projectName = ResolveProjectName(rawProjectRoot, expectedTemplateId);
            var isGoldenEligible = GoldenTemplateIntentClassifier.HasExplicitGoldenTemplateRequest(request.Prompt);
            var workloadClass = GenerationWorkloadClassifier.Resolve(request.Prompt);
            var stageDurations = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["routing"] = Math.Round(Math.Max(0, elapsed.TotalSeconds), 3)
            };

            var report = new GenerationRunReport(
                RunId: BuildRunId(request.SessionId, "golden_route_mismatch"),
                Prompt: request.Prompt,
                ModelRoute: "forge/route-mismatch",
                ProjectName: projectName,
                StartedAtUtc: startedAtUtc,
                CompletedAtUtc: completedAtUtc,
                RawProjectRoot: rawProjectRoot,
                ValidatedProjectRoot: null,
                FileCount: 0,
                MethodCount: 0,
                RetryCount: 0,
                BlueprintAccepted: false,
                CompileGatePassed: false,
                Errors: new[] { $"[GOLDEN_ROUTE_MISMATCH] {reason}" },
                Warnings: Array.Empty<string>(),
                RouteMatched: route.Matched,
                RoutedTemplateId: route.TemplateId,
                RouteConfidence: route.Confidence,
                GoldenTemplateMatched: false,
                GoldenTemplateEligible: isGoldenEligible,
                WorkloadClass: workloadClass,
                StageDurationsSec: stageDurations);
            RecordGenerationRouteTelemetry(report);
            await PersistRunReportSafelyAsync(report, ct);

            return new GenerationResult(
                Success: false,
                Files: new List<GeneratedFile>(),
                ProjectPath: rawProjectRoot,
                Errors: new List<BuildError> { new("Router", 0, "GOLDEN_ROUTE_MISMATCH", reason) },
                Duration: elapsed);
        }

        private async Task PersistRunReportSafelyAsync(GenerationRunReport report, CancellationToken ct)
        {
            try
            {
                await _generationReportWriter.WriteAsync(report, ct);
                await _generationHealthReporter.AppendAsync(report, ct);
            }
            catch
            {
            }
        }
    }
}

