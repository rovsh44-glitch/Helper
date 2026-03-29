#nullable enable
// Minimal API glue in these partials triggers noisy false positives from Roslyn nullability analysis.
// Keep suppressions local to endpoint registration instead of project-wide.
#pragma warning disable CS8600, CS8601, CS8602, CS8603, CS8604, CS8619, CS8622, CS8632

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Helper.Api.Conversation;
using Helper.Runtime.Core;
using Helper.Runtime.Generation;
using Helper.Runtime.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Helper.Api.Hosting;

public static partial class EndpointRegistrationExtensions
{
	private static void MapSystemAndGovernanceEndpoints(IEndpointRouteBuilder endpoints, ApiRuntimeConfig runtimeConfig)
	{
		endpoints.MapGet("/api/openapi.json", (Func<IResult>)(() => Results.Ok(OpenApiDocumentFactory.Create())));
		endpoints.MapGet("/api/health", (Func<IResult>)(() => Results.Ok(new
		{
			status = "healthy",
			timestamp = DateTime.UtcNow
		})));
		endpoints.MapGet("/api/readiness", (Func<IStartupReadinessService, IConversationPersistenceHealth, IResult>)((IStartupReadinessService readiness, IConversationPersistenceHealth persistence) =>
		{
			StartupReadinessSnapshot snapshot = readiness.GetSnapshot();
			ConversationPersistenceHealthSnapshot persistenceSnapshot = persistence.GetSnapshot();
			bool authReady = true;
			bool readyForChat = snapshot.ReadyForChat && authReady && persistenceSnapshot.Ready;
			List<string> alerts = snapshot.Alerts
				.Concat(persistenceSnapshot.Alerts)
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToList();
			return Results.Ok(new
			{
				status = (!readyForChat && string.Equals(snapshot.Status, "ready", StringComparison.OrdinalIgnoreCase)) ? "starting" : snapshot.Status,
				phase = snapshot.Phase,
				lifecycleState = snapshot.LifecycleState,
				readyForChat = readyForChat,
				listening = snapshot.Listening,
				warmupMode = snapshot.WarmupMode,
				lastTransitionUtc = snapshot.LastTransitionUtc,
				startedAtUtc = snapshot.StartedAtUtc,
				listeningAtUtc = snapshot.ListeningAtUtc,
				minimalReadyAtUtc = snapshot.MinimalReadyAtUtc,
				warmReadyAtUtc = snapshot.WarmReadyAtUtc,
				timeToListeningMs = snapshot.TimeToListeningMs,
				timeToReadyMs = snapshot.TimeToReadyMs,
				timeToWarmReadyMs = snapshot.TimeToWarmReadyMs,
				authReady = authReady,
				persistenceReady = persistenceSnapshot.Ready,
				persistenceLoaded = persistenceSnapshot.Loaded,
				catalogReady = !string.Equals(snapshot.Phase, "booting", StringComparison.OrdinalIgnoreCase) &&
					!string.Equals(snapshot.Phase, "listening", StringComparison.OrdinalIgnoreCase),
				warmupCompleted = string.Equals(snapshot.Phase, "warmup_complete", StringComparison.OrdinalIgnoreCase),
				alerts = alerts
			});
		}));
		endpoints.MapGet("/api/control-plane", (Func<Helper.Api.Backend.ControlPlane.IBackendControlPlane, IResult>)(controlPlane => Results.Ok(controlPlane.GetSnapshot())));
		endpoints.MapGet("/api/capabilities/catalog", (Func<ICapabilityCatalogService, CancellationToken, Task<IResult>>)(async (ICapabilityCatalogService capabilityCatalog, CancellationToken ct) =>
		{
			CapabilityCatalogSnapshot snapshot = await capabilityCatalog.GetSnapshotAsync(ct);
			return Results.Ok(ToCapabilityCatalogSnapshotDto(snapshot));
		}));
		endpoints.MapGet("/api/runtime/logs", (Func<int?, int?, IRuntimeLogService, IResult>)(([FromQuery] int? tail, [FromQuery] int? maxSources, IRuntimeLogService runtimeLogs) =>
		{
			return Results.Ok(runtimeLogs.GetSnapshot(tail ?? 60, maxSources ?? 4));
		}));
		endpoints.MapGet("/api/handshake", (Func<HttpContext, IResult>)delegate(HttpContext context)
		{
			object value;
			ApiPrincipal apiPrincipal = (context.Items.TryGetValue("ApiPrincipal", out value) ? (value as ApiPrincipal) : null);
			return Results.Ok(new
			{
				status = "ok",
				auth = "Bearer session token, X-API-KEY, or access_token",
				requiresKey = false,
				principalType = apiPrincipal?.PrincipalType,
				role = apiPrincipal?.Role,
				scopes = (apiPrincipal?.Scopes?.OrderBy((string x) => x).ToArray() ?? Array.Empty<string>())
			});
		});
		endpoints.MapPost("/api/auth/session", (Func<SessionTokenExchangeRequestDto, HttpContext, IApiAuthorizationService, IApiSessionTokenService, IFeatureFlags, Helper.Api.Backend.Configuration.IBackendOptionsCatalog, IResult>)(([FromBody] SessionTokenExchangeRequestDto? dto, HttpContext context, IApiAuthorizationService authz, IApiSessionTokenService sessionTokens, IFeatureFlags flags, Helper.Api.Backend.Configuration.IBackendOptionsCatalog options) =>
		{
			if (!flags.AuthV2Enabled)
			{
				return Results.Json(new
				{
					success = false,
					error = "Auth v2 session exchange is disabled."
				}, (JsonSerializerOptions?)null, (string?)null, (int?)403);
			}
			string text = (string.IsNullOrWhiteSpace(dto?.ApiKey) ? ApiProgramHelpers.ExtractApiKey(context) : dto.ApiKey);
			ApiPrincipal principal;
			object value;
			if (!string.IsNullOrWhiteSpace(text))
			{
				if (!authz.TryAuthorize(text, out principal))
				{
					return Results.Json(new
					{
						success = false,
						error = "Invalid API key for session exchange."
					}, (JsonSerializerOptions?)null, (string?)null, (int?)401);
				}
			}
			else if (context.Items.TryGetValue("ApiPrincipal", out value) && value is ApiPrincipal apiPrincipal)
			{
				principal = apiPrincipal;
			}
			else
			{
				if (!IsLocalBootstrapAllowed() || !IsLocalRequest(context))
				{
					return Results.Json(new
					{
						success = false,
						error = "Session bootstrap denied. Provide a valid API key or enable local bootstrap."
					}, (JsonSerializerOptions?)null, (string?)null, (int?)401);
				}
				principal = BuildLocalBootstrapPrincipal();
				ILogger logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Auth");
				logger.LogInformation("Issued local bootstrap session. RemoteIp={RemoteIp} CorrelationId={CorrelationId}", context.Connection.RemoteIpAddress?.ToString() ?? "unknown", (!context.Items.TryGetValue("CorrelationId", out object value2)) ? null : value2?.ToString());
			}
			string normalizedSurface;
			string error;
			ApiPrincipal apiPrincipal2 = ApplySessionExchangePolicy(principal, dto?.Surface, dto?.RequestedScopes, out normalizedSurface, out error);
			if (error != null)
			{
				return Results.Json(new
				{
					success = false,
					error = error
				}, (JsonSerializerOptions?)null, (string?)null, (int?)403);
			}
			int num = ResolveSessionTtlMinutes(dto?.TtlMinutes, options);
			SessionTokenIssueResult sessionTokenIssueResult = sessionTokens.Issue(apiPrincipal2, TimeSpan.FromMinutes(num));
			return Results.Ok(new SessionTokenResponseDto(sessionTokenIssueResult.AccessToken, sessionTokenIssueResult.ExpiresAtUtc, Math.Max(1, (int)(sessionTokenIssueResult.ExpiresAtUtc - DateTimeOffset.UtcNow).TotalSeconds), "Bearer", apiPrincipal2.PrincipalType, apiPrincipal2.Role, apiPrincipal2.KeyId, apiPrincipal2.Scopes.OrderBy<string, string>((string x) => x, StringComparer.OrdinalIgnoreCase).ToArray(), normalizedSurface));
		}));
		endpoints.MapGet("/api/auth/keys", (Func<bool, IAuthKeysStore, IResult>)(([FromQuery] bool includeRevoked, IAuthKeysStore keysStore) =>
		{
			AuthKeyMetadataDto[] value = (from x in keysStore.ListKeys(includeRevoked)
				select new AuthKeyMetadataDto(x.KeyId, x.Role, x.Scopes, x.CreatedAtUtc, x.ExpiresAtUtc, x.IsRevoked, x.RevokedAtUtc, x.RevokedReason, x.PrincipalType, x.IsSystemManaged)).ToArray();
			return Results.Ok(value);
		}));
		endpoints.MapPost("/api/auth/keys/rotate", (Func<AuthKeyRotateRequestDto, IAuthKeysStore, IResult>)(([FromBody] AuthKeyRotateRequestDto? dto, IAuthKeysStore keysStore) =>
		{
			AuthKeyIssueResult authKeyIssueResult = keysStore.RotateMachineKey(new AuthKeyRotationRequest(dto?.KeyId, dto?.Role, dto?.Scopes, dto?.TtlDays));
			return Results.Ok(new AuthKeyRotateResponseDto(authKeyIssueResult.KeyId, authKeyIssueResult.ApiKey, authKeyIssueResult.Role, authKeyIssueResult.Scopes, authKeyIssueResult.CreatedAtUtc, authKeyIssueResult.ExpiresAtUtc, authKeyIssueResult.PrincipalType));
		}));
		endpoints.MapPost("/api/auth/keys/{keyId}/revoke", (Func<string, AuthKeyRevokeRequestDto, IAuthKeysStore, IResult>)((string keyId, [FromBody] AuthKeyRevokeRequestDto? dto, IAuthKeysStore keysStore) => (!keysStore.RevokeKey(keyId, dto?.Reason)) ? Results.NotFound(new
		{
			success = false,
			error = "Key not found."
		}) : Results.Ok(new
		{
			success = true,
			keyId = keyId
		})));
		endpoints.MapGet("/api/metrics", (Func<IRequestMetricsService, IConversationMetricsService, IWebResearchTelemetryService, IConversationStageMetricsService, IToolAuditService, IHelpfulnessTelemetryService, IHumanLikeConversationDashboardService, IChatResilienceTelemetryService, IIntentTelemetryService, IPostTurnAuditQueue, IGenerationMetricsService, IRouteTelemetryService, Helper.Api.Backend.ControlPlane.IBackendControlPlane, IResult>)((IRequestMetricsService requestMetrics, IConversationMetricsService conversationMetrics, IWebResearchTelemetryService webResearchTelemetry, IConversationStageMetricsService conversationStages, IToolAuditService toolAudit, IHelpfulnessTelemetryService helpfulness, IHumanLikeConversationDashboardService humanLikeConversation, IChatResilienceTelemetryService resilienceTelemetry, IIntentTelemetryService intentTelemetry, IPostTurnAuditQueue postTurnAudit, IGenerationMetricsService generationMetrics, IRouteTelemetryService routeTelemetry, Helper.Api.Backend.ControlPlane.IBackendControlPlane controlPlane) => Results.Ok(new
		{
			requests = requestMetrics.GetSnapshot(),
			conversations = conversationMetrics.GetSnapshot(),
			webResearch = webResearchTelemetry.GetSnapshot(),
			humanLikeConversation = humanLikeConversation.GetSnapshot(),
			conversationStages = conversationStages.GetSnapshot(),
			tools = toolAudit.GetSnapshot(),
			helpfulness = helpfulness.GetGlobalSnapshot(),
			resilience = resilienceTelemetry.GetSnapshot(),
			intent = intentTelemetry.GetSnapshot(),
			postTurnAudit = postTurnAudit.GetSnapshot(),
			generation = generationMetrics.GetSnapshot(),
			routeTelemetry = routeTelemetry.GetSnapshot(),
			controlPlane = controlPlane.GetSnapshot()
		})));
		endpoints.MapGet("/api/metrics/web-research", (Func<IWebResearchTelemetryService, IResult>)(telemetry =>
		{
			return Results.Ok(telemetry.GetSnapshot());
		}));
		endpoints.MapGet("/api/metrics/human-like-conversation", (Func<int?, IHumanLikeConversationDashboardService, IResult>)(([FromQuery] int? days, IHumanLikeConversationDashboardService dashboard) =>
		{
			return Results.Ok(dashboard.GetSnapshot(days ?? 7));
		}));
		endpoints.MapGet("/api/metrics/tool-audit-consistency", (Func<IConversationMetricsService, IToolAuditService, IResult>)delegate(IConversationMetricsService conversationMetrics, IToolAuditService toolAudit)
		{
			ConversationMetricsSnapshot snapshot = conversationMetrics.GetSnapshot();
			ToolAuditSnapshot snapshot2 = toolAudit.GetSnapshot();
			int totalToolCalls = snapshot.TotalToolCalls;
			int totalCalls = snapshot2.TotalCalls;
			int num = totalCalls - totalToolCalls;
			int num2 = Math.Max(1, (int)Math.Ceiling((double)totalToolCalls * 0.1));
			bool flag = Math.Abs(num) <= num2;
			List<string> list = new List<string>();
			if (!flag)
			{
				list.Add($"Tool-audit mismatch detected: expected ~{totalToolCalls}, actual {totalCalls}, delta {num}.");
			}
			return Results.Ok(new
			{
				consistent = flag,
				expectedToolCalls = totalToolCalls,
				actualToolCalls = totalCalls,
				delta = num,
				tolerance = num2,
				alerts = list
			});
		});
		endpoints.MapGet("/api/metrics/prometheus", (Func<IRequestMetricsService, IConversationMetricsService, IWebResearchTelemetryService, IConversationStageMetricsService, IToolAuditService, IHelpfulnessTelemetryService, IHumanLikeConversationDashboardService, IChatResilienceTelemetryService, IIntentTelemetryService, IPostTurnAuditQueue, IGenerationMetricsService, Helper.Api.Backend.ControlPlane.IBackendControlPlane, IPrometheusMetricsFormatter, IResult>)delegate(IRequestMetricsService requestMetrics, IConversationMetricsService conversationMetrics, IWebResearchTelemetryService webResearchTelemetry, IConversationStageMetricsService conversationStages, IToolAuditService toolAudit, IHelpfulnessTelemetryService helpfulness, IHumanLikeConversationDashboardService humanLikeConversation, IChatResilienceTelemetryService resilienceTelemetry, IIntentTelemetryService intentTelemetry, IPostTurnAuditQueue postTurnAudit, IGenerationMetricsService generationMetrics, Helper.Api.Backend.ControlPlane.IBackendControlPlane controlPlane, IPrometheusMetricsFormatter formatter)
		{
			string content = formatter.Format(requestMetrics.GetSnapshot(), conversationMetrics.GetSnapshot(), webResearchTelemetry.GetSnapshot(), humanLikeConversation.GetSnapshot(), conversationStages.GetSnapshot(), toolAudit.GetSnapshot(), helpfulness.GetGlobalSnapshot(), resilienceTelemetry.GetSnapshot(), intentTelemetry.GetSnapshot(), postTurnAudit.GetSnapshot(), generationMetrics.GetSnapshot(), controlPlane.GetSnapshot());
			return Results.Text(content, "text/plain");
		});
		endpoints.MapPost("/api/metrics/parity-certification", (Func<string, IParityCertificationService, CancellationToken, Task<IResult>>)(async ([FromQuery] string? reportPath, IParityCertificationService certification, CancellationToken ct) =>
		{
			ParityCertificationReport report = await certification.GenerateAsync(reportPath, ct);
			return Results.Ok(new
			{
				success = true,
				generatedAtUtc = report.GeneratedAtUtc,
				ReportPath = report.ReportPath,
				TotalRuns = report.TotalRuns,
				GoldenHitRate = report.GoldenHitRate,
				GenerationSuccessRate = report.GenerationSuccessRate,
				P95ReadySeconds = report.P95ReadySeconds,
				UnknownErrorRate = report.UnknownErrorRate,
				ToolSuccessRatio = report.ToolSuccessRatio,
				Alerts = report.Alerts
			});
		}));
		endpoints.MapPost("/api/metrics/parity-gate", (Func<string, IParityCertificationService, IParityGateEvaluator, CancellationToken, Task<IResult>>)(async ([FromQuery] string? reportPath, IParityCertificationService certification, IParityGateEvaluator gateEvaluator, CancellationToken ct) =>
		{
			ParityCertificationReport report = await certification.GenerateAsync(reportPath, ct);
			ParityGateThresholds thresholds = ParityGateThresholds.FromEnvironment();
			ParityGateDecision decision = gateEvaluator.Evaluate(report, thresholds);
			return Results.Ok(new
			{
				success = decision.Passed,
				report = new { report.GeneratedAtUtc, report.ReportPath, report.TotalRuns, report.GoldenHitRate, report.GenerationSuccessRate, report.P95ReadySeconds, report.UnknownErrorRate, report.ToolSuccessRatio, report.Alerts },
				thresholds = thresholds,
				violations = decision.Violations
			});
		}));
		endpoints.MapPost("/api/metrics/parity-window-gate", (Func<string, int?, IParityWindowGateService, CancellationToken, Task<IResult>>)(async ([FromQuery] string? reportPath, [FromQuery] int? windowDays, IParityWindowGateService windowGate, CancellationToken ct) =>
		{
			ParityWindowGateReport report = await windowGate.EvaluateAsync(windowDays ?? 7, reportPath, ct);
			return Results.Ok(new
			{
				success = report.Passed,
				GeneratedAtUtc = report.GeneratedAtUtc,
				ReportPath = report.ReportPath,
				WindowDays = report.WindowDays,
				AvailableDays = report.AvailableDays,
				WindowComplete = report.WindowComplete,
				Violations = report.Violations,
				days = report.Days.Select((ParityWindowDayResult x) => new
				{
					x.DateUtc,
					x.Passed,
					x.Violations,
					x.Snapshot.GoldenHitRate,
					x.Snapshot.GenerationSuccessRate,
					x.Snapshot.P95ReadySeconds,
					x.Snapshot.UnknownErrorRate,
					x.Snapshot.ToolSuccessRatio
				})
			});
		}));
		endpoints.MapPost("/api/metrics/parity-benchmark", (Func<string, string, string, IGenerationParityBenchmarkService, CancellationToken, Task<IResult>>)(async ([FromQuery] string? goldenCorpusPath, [FromQuery] string? incidentCorpusPath, [FromQuery] string? reportPath, IGenerationParityBenchmarkService benchmark, CancellationToken ct) =>
		{
			string resolvedGolden = (string.IsNullOrWhiteSpace(goldenCorpusPath) ? Path.Combine(runtimeConfig.RootPath, "eval", "golden_template_prompts_ru_en.jsonl") : goldenCorpusPath);
			string resolvedIncident = (string.IsNullOrWhiteSpace(incidentCorpusPath) ? Path.Combine(runtimeConfig.RootPath, "eval", "incident_corpus.jsonl") : incidentCorpusPath);
			GenerationParityBenchmarkReport report = await benchmark.RunAsync(resolvedGolden, resolvedIncident, reportPath, ct);
			return Results.Ok(new
			{
				success = report.Passed,
				GeneratedAtUtc = report.GeneratedAtUtc,
				ReportPath = report.ReportPath,
				GoldenCaseCount = report.GoldenCaseCount,
				GoldenFamilyCount = report.GoldenFamilyCount,
				GoldenHitRate = report.GoldenHitRate,
				IncidentCaseCount = report.IncidentCaseCount,
				IncidentErrorCodeCount = report.IncidentErrorCodeCount,
				IncidentRootCauseClassCount = report.IncidentRootCauseClassCount,
				RootCausePrecision = report.RootCausePrecision,
				UnknownErrorRate = report.UnknownErrorRate,
				DeterministicAutofixCoverageRate = report.DeterministicAutofixCoverageRate,
				Violations = report.Violations
			});
		}));
		endpoints.MapPost("/api/metrics/closed-loop-predictability", (Func<string, string, IClosedLoopPredictabilityService, CancellationToken, Task<IResult>>)(async ([FromQuery] string? incidentCorpusPath, [FromQuery] string? reportPath, IClosedLoopPredictabilityService service, CancellationToken ct) =>
		{
			string resolvedIncident = string.IsNullOrWhiteSpace(incidentCorpusPath)
				? Path.Combine(runtimeConfig.RootPath, "eval", "incident_corpus.jsonl")
				: incidentCorpusPath;
			ClosedLoopPredictabilityReport report = await service.EvaluateAsync(resolvedIncident, reportPath, ct);
			return Results.Ok(new
			{
				success = report.Passed,
				GeneratedAtUtc = report.GeneratedAtUtc,
				ReportPath = report.ReportPath,
				TopIncidentClasses = report.TopIncidentClasses,
				RepeatsPerClass = report.RepeatsPerClass,
				MaxAllowedVariance = report.MaxAllowedVariance,
				Violations = report.Violations
			});
		}));
	}

	private static CapabilityCatalogSnapshotDto ToCapabilityCatalogSnapshotDto(CapabilityCatalogSnapshot snapshot)
	{
		return new CapabilityCatalogSnapshotDto(
			snapshot.GeneratedAtUtc,
			snapshot.Models.Select(model => new ModelCapabilityCatalogEntryDto(
				model.CapabilityId,
				model.RouteKey,
				model.ModelClass,
				model.IntendedUse,
				model.LatencyTier,
				model.SupportsStreaming,
				model.SupportsToolUse,
				model.SupportsVision,
				model.FallbackClass,
				model.ConfiguredFallbackModel,
				model.ResolvedModel,
				model.ResolvedModelAvailable,
				model.Notes)).ToArray(),
			snapshot.DeclaredCapabilities.Select(entry => new DeclaredCapabilityCatalogEntryDto(
				entry.CapabilityId,
				entry.SurfaceKind,
				entry.OwnerId,
				entry.DisplayName,
				entry.DeclaredCapability,
				entry.Status,
				entry.OwningGate,
				entry.EvidenceType,
				entry.EvidenceRef,
				entry.Available,
				entry.CertificationRelevant,
				entry.EnabledInCertification,
				entry.Certified,
				entry.HasCriticalAlerts,
				entry.Notes)).ToArray(),
			new CapabilityCatalogSummaryDto(
				snapshot.Summary.TotalDeclaredCapabilities,
				snapshot.Summary.MissingGateOwnership,
				snapshot.Summary.DisabledInCertification,
				snapshot.Summary.Degraded,
				snapshot.Summary.Surfaces.Select(surface => new CapabilityCatalogSurfaceSummaryDto(
					surface.SurfaceKind,
					surface.Total,
					surface.Available,
					surface.Certified,
					surface.MissingGateOwnership,
					surface.DisabledInCertification,
					surface.Degraded)).ToArray()),
			snapshot.Alerts);
	}
}

