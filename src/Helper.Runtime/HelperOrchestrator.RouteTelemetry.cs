using System;
using System.Linq;
using Helper.Runtime.Core;
using Helper.Runtime.Generation;

namespace Helper.Runtime.Infrastructure
{
    public partial class HelperOrchestrator
    {
        private void RecordGenerationRouteTelemetry(GenerationRunReport report)
        {
            if (_routeTelemetry is null)
            {
                return;
            }

            var degradationReason = ResolveGenerationDegradationReason(report);
            var quality = ResolveGenerationQuality(report, degradationReason);
            var outcome = quality switch
            {
                RouteTelemetryQualities.Failed => RouteTelemetryOutcomes.Failed,
                RouteTelemetryQualities.Degraded => RouteTelemetryOutcomes.Degraded,
                _ => RouteTelemetryOutcomes.Completed
            };

            _routeTelemetry.Record(new RouteTelemetryEvent(
                RecordedAtUtc: DateTimeOffset.UtcNow,
                Channel: RouteTelemetryChannels.Generation,
                OperationKind: RouteTelemetryOperationKinds.GenerationRun,
                RouteKey: report.RoutedTemplateId ?? report.ModelRoute,
                Quality: quality,
                Outcome: outcome,
                Confidence: report.RouteConfidence,
                ModelRoute: report.ModelRoute,
                CorrelationId: report.RunId,
                WorkloadClass: report.WorkloadClass,
                DegradationReason: degradationReason,
                RouteMatched: report.RouteMatched ?? false,
                CompileGatePassed: report.CompileGatePassed,
                ArtifactValidationPassed: report.ArtifactValidationPassed,
                SmokePassed: report.SmokePassed,
                GoldenTemplateEligible: report.GoldenTemplateEligible,
                GoldenTemplateMatched: report.GoldenTemplateMatched,
                Signals: report.Errors.Take(3).ToArray()));
        }

        private void RecordGenerationRouteTelemetry(
            GenerationRequest request,
            TemplateRoutingDecision route,
            GenerationResult result,
            string modelRoute,
            string? degradationReason = null)
        {
            if (_routeTelemetry is null)
            {
                return;
            }

            var report = new GenerationRunReport(
                RunId: BuildRunId(request.SessionId, "runtime_route"),
                Prompt: request.Prompt,
                ModelRoute: modelRoute,
                ProjectName: Path.GetFileName(result.ProjectPath),
                StartedAtUtc: DateTime.UtcNow - result.Duration,
                CompletedAtUtc: DateTime.UtcNow,
                RawProjectRoot: result.ProjectPath,
                ValidatedProjectRoot: result.Success ? result.ProjectPath : null,
                FileCount: result.Files.Count,
                MethodCount: 0,
                RetryCount: result.HealAttempts,
                BlueprintAccepted: result.Success,
                CompileGatePassed: result.Success,
                Errors: result.Errors.Select(FormatBuildError).ToArray(),
                Warnings: Array.Empty<string>(),
                RouteMatched: route.Matched,
                RoutedTemplateId: route.TemplateId,
                RouteConfidence: route.Confidence,
                GoldenTemplateMatched: route.Matched,
                GoldenTemplateEligible: GoldenTemplateIntentClassifier.HasExplicitGoldenTemplateRequest(request.Prompt),
                WorkloadClass: GenerationWorkloadClassifier.Resolve(request.Prompt),
                ArtifactValidationPassed: null,
                SmokePassed: null,
                SmokeScenarios: null);

            if (!string.IsNullOrWhiteSpace(degradationReason))
            {
                report = report with { Errors = report.Errors.Concat(new[] { $"[ROUTE_TELEMETRY] {degradationReason}" }).ToArray() };
            }

            RecordGenerationRouteTelemetry(report);
        }

        private static string ResolveGenerationQuality(GenerationRunReport report, string? degradationReason)
        {
            if (!report.CompileGatePassed)
            {
                return RouteTelemetryQualities.Failed;
            }

            if (!string.IsNullOrWhiteSpace(degradationReason))
            {
                return RouteTelemetryQualities.Degraded;
            }

            if ((report.RouteConfidence ?? 0) >= 0.85)
            {
                return RouteTelemetryQualities.High;
            }

            if ((report.RouteConfidence ?? 0) >= 0.60)
            {
                return RouteTelemetryQualities.Medium;
            }

            if ((report.RouteConfidence ?? 0) > 0)
            {
                return RouteTelemetryQualities.Low;
            }

            return RouteTelemetryQualities.Unknown;
        }

        private static string? ResolveGenerationDegradationReason(GenerationRunReport report)
        {
            if (!report.RouteMatched.GetValueOrDefault())
            {
                return "route_unmatched";
            }

            if (!report.CompileGatePassed)
            {
                return "compile_failed";
            }

            if (report.ArtifactValidationPassed == false)
            {
                return "artifact_validation_failed";
            }

            if (report.SmokePassed == false)
            {
                return "smoke_failed";
            }

            if (report.Errors.Any(error => error.Contains("TIMEOUT", StringComparison.OrdinalIgnoreCase)))
            {
                return "timeout";
            }

            return null;
        }
    }
}

