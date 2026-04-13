#nullable enable
// Minimal API glue in these partials triggers noisy false positives from Roslyn nullability analysis.
// Keep suppressions local to endpoint registration instead of project-wide.
// Minimal API delegate signatures still trigger a narrow set of Roslyn nullability mismatches here.
#pragma warning disable CS8600, CS8619, CS8622

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Helper.Api.Conversation;
using Helper.Runtime.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Helper.Api.Hosting;

public static partial class EndpointRegistrationExtensions
{
    private static void MapConversationChatAndStreamingEndpoints(IEndpointRouteBuilder endpoints)
    {
		endpoints.MapPost("/api/chat", (Func<ChatRequestDto, IChatOrchestrator, IConversationStore, IConversationMetricsService, IHumanLikeConversationDashboardService, IWebResearchTelemetryService, IFeatureFlags, HttpContext, ILoggerFactory, CancellationToken, Task<IResult>>)(async ([FromBody] ChatRequestDto dto, IChatOrchestrator chat, IConversationStore store, IConversationMetricsService metrics, IHumanLikeConversationDashboardService dashboard, IWebResearchTelemetryService webResearchTelemetry, IFeatureFlags flags, HttpContext httpContext, ILoggerFactory loggerFactory, CancellationToken ct) =>
		{
			RecordHumanLikeConversationUserTurn(dashboard, dto.ConversationId);
			if (IsDeterministicSmokePrompt(dto))
			{
				ChatResponseDto smokeResponse = BuildDeterministicSmokeResponse(dto);
				PersistDeterministicSmokeConversation(store, smokeResponse);
				loggerFactory.CreateLogger("Smoke").LogInformation("Deterministic smoke chat completed. ConversationId={ConversationId} TurnId={TurnId}", smokeResponse.ConversationId, smokeResponse.TurnId);
				metrics.RecordTurn(BuildSuccessfulConversationTurnMetric(dto, smokeResponse, 1L, 1L));
				RecordHumanLikeConversationAssistantTurn(dashboard, dto.ConversationId, smokeResponse);
				RecordWebResearchAssistantTurn(webResearchTelemetry, smokeResponse);
				return Results.Ok(smokeResponse);
			}
			if (IsDeterministicLongSmokePrompt(dto))
			{
				ChatResponseDto smokeResponse = BuildDeterministicLongSmokeResponse(dto);
				PersistDeterministicSmokeConversation(store, smokeResponse);
				loggerFactory.CreateLogger("Smoke").LogInformation("Deterministic long smoke chat completed. ConversationId={ConversationId} TurnId={TurnId}", smokeResponse.ConversationId, smokeResponse.TurnId);
				metrics.RecordTurn(BuildSuccessfulConversationTurnMetric(dto, smokeResponse, 1L, 2L));
				RecordHumanLikeConversationAssistantTurn(dashboard, dto.ConversationId, smokeResponse);
				RecordWebResearchAssistantTurn(webResearchTelemetry, smokeResponse);
				return Results.Ok(smokeResponse);
			}

			int num;
			if (!flags.AttachmentsEnabled)
			{
				IReadOnlyList<AttachmentDto> attachments = dto.Attachments;
				num = ((attachments != null && attachments.Count > 0) ? 1 : 0);
			}
			else
			{
				num = 0;
			}
			if (num != 0)
			{
				return Results.Json(new
				{
					success = false,
					error = "Attachments feature is disabled."
				}, (JsonSerializerOptions?)null, (string?)null, (int?)403);
			}
			DateTimeOffset startedAt = DateTimeOffset.UtcNow;
			try
			{
				ChatResponseDto response = await chat.CompleteTurnAsync(dto, ct);
				if (!flags.EnhancedGroundingEnabled || !flags.GroundingV2Enabled)
				{
					response = response with
					{
						GroundingStatus = null,
						CitationCoverage = 0.0,
						VerifiedClaims = 0,
						TotalClaims = 0,
						ClaimGroundings = null,
						UncertaintyFlags = null
					};
				}
				loggerFactory.CreateLogger("Chat").LogInformation("Chat turn completed. ConversationId={ConversationId} TurnId={TurnId} CorrelationId={CorrelationId}", response.ConversationId, response.TurnId, (!httpContext.Items.TryGetValue("CorrelationId", out object cid)) ? null : cid?.ToString());
				if (string.Equals(response.GroundingStatus, "blocked", StringComparison.OrdinalIgnoreCase))
				{
					loggerFactory.CreateLogger("ChatSafety").LogWarning("Safety blocked output. ConversationId={ConversationId} TurnId={TurnId} CorrelationId={CorrelationId}", response.ConversationId, response.TurnId, (!httpContext.Items.TryGetValue("CorrelationId", out object blockedCid)) ? null : blockedCid?.ToString());
				}
				long elapsed = (long)(DateTimeOffset.UtcNow - startedAt).TotalMilliseconds;
				metrics.RecordTurn(BuildSuccessfulConversationTurnMetric(dto, response, elapsed, elapsed));
				RecordHumanLikeConversationAssistantTurn(dashboard, dto.ConversationId, response);
				RecordWebResearchAssistantTurn(webResearchTelemetry, response);
				return Results.Ok(response);
			}
			catch (OperationCanceledException) when (ct.IsCancellationRequested || httpContext.RequestAborted.IsCancellationRequested)
			{
				long elapsed2 = (long)(DateTimeOffset.UtcNow - startedAt).TotalMilliseconds;
				metrics.RecordTurn(new ConversationTurnMetric(elapsed2, elapsed2, 0, IsFactualPrompt(dto.Message), HasCitations: false, 0.0, IsSuccessful: false));
				loggerFactory.CreateLogger("Chat").LogWarning("Chat turn canceled or timed out. ConversationId={ConversationId} CorrelationId={CorrelationId}", dto.ConversationId, (!httpContext.Items.TryGetValue("CorrelationId", out object canceledCid)) ? null : canceledCid?.ToString());
				return Results.Json(new
				{
					success = false,
					error = "Chat turn canceled or timed out."
				}, (JsonSerializerOptions?)null, (string?)null, (int?)408);
			}
			catch (Exception ex)
			{
				long elapsed2 = (long)(DateTimeOffset.UtcNow - startedAt).TotalMilliseconds;
				metrics.RecordTurn(new ConversationTurnMetric(elapsed2, elapsed2, 0, IsFactualPrompt(dto.Message), HasCitations: false, 0.0, IsSuccessful: false));
				loggerFactory.CreateLogger("Chat").LogError(ex, "Chat turn failed with unhandled exception. ConversationId={ConversationId} CorrelationId={CorrelationId}", dto.ConversationId, (!httpContext.Items.TryGetValue("CorrelationId", out object failedCid)) ? null : failedCid?.ToString());
				throw;
			}
		}));
		endpoints.MapPost("/api/chat/stream", (Func<ChatRequestDto, IChatOrchestrator, IConversationStore, IConversationMetricsService, IHumanLikeConversationDashboardService, IWebResearchTelemetryService, IFeatureFlags, Helper.Api.Backend.Configuration.IBackendOptionsCatalog, HttpContext, ILoggerFactory, CancellationToken, Task<IResult>>)(async ([FromBody] ChatRequestDto dto, IChatOrchestrator chat, IConversationStore store, IConversationMetricsService metrics, IHumanLikeConversationDashboardService dashboard, IWebResearchTelemetryService webResearchTelemetry, IFeatureFlags flags, Helper.Api.Backend.Configuration.IBackendOptionsCatalog options, HttpContext httpContext, ILoggerFactory loggerFactory, CancellationToken ct) =>
		{
			RecordHumanLikeConversationUserTurn(dashboard, dto.ConversationId);
			if (IsDeterministicSmokePrompt(dto))
			{
				ChatResponseDto smokeResponse = BuildDeterministicSmokeResponse(dto);
				PersistDeterministicSmokeConversation(store, smokeResponse);
				loggerFactory.CreateLogger("Smoke").LogInformation("Deterministic smoke stream completed. ConversationId={ConversationId} TurnId={TurnId}", smokeResponse.ConversationId, smokeResponse.TurnId);
				await WriteDeterministicSmokeStreamAsync(httpContext, smokeResponse, ct);
				metrics.RecordTurn(BuildSuccessfulConversationTurnMetric(
					dto,
					smokeResponse,
					1L,
					1L,
					modelTtftMs: 0L,
					transportTtftMs: 0L,
					endToEndTtftMs: 1L));
				RecordHumanLikeConversationAssistantTurn(dashboard, dto.ConversationId, smokeResponse);
				RecordWebResearchAssistantTurn(webResearchTelemetry, smokeResponse);
				return Results.Empty;
			}
			if (IsDeterministicLongSmokePrompt(dto))
			{
				ChatResponseDto smokeResponse = BuildDeterministicLongSmokeResponse(dto);
				PersistDeterministicSmokeConversation(store, smokeResponse);
				loggerFactory.CreateLogger("Smoke").LogInformation("Deterministic long smoke stream completed. ConversationId={ConversationId} TurnId={TurnId}", smokeResponse.ConversationId, smokeResponse.TurnId);
				await WriteDeterministicLongSmokeStreamAsync(httpContext, smokeResponse, ct);
				metrics.RecordTurn(BuildSuccessfulConversationTurnMetric(
					dto,
					smokeResponse,
					1L,
					2L,
					modelTtftMs: 0L,
					transportTtftMs: 0L,
					endToEndTtftMs: 1L));
				RecordHumanLikeConversationAssistantTurn(dashboard, dto.ConversationId, smokeResponse);
				RecordWebResearchAssistantTurn(webResearchTelemetry, smokeResponse);
				return Results.Empty;
			}

			if (!flags.StreamingV2Enabled)
			{
				return Results.Json(new
				{
					success = false,
					error = "Streaming v2 is disabled."
				}, (JsonSerializerOptions?)null, (string?)null, (int?)403);
			}
			int num;
			if (!flags.AttachmentsEnabled)
			{
				IReadOnlyList<AttachmentDto> attachments = dto.Attachments;
				num = ((attachments != null && attachments.Count > 0) ? 1 : 0);
			}
			else
			{
				num = 0;
			}
			if (num != 0)
			{
				return Results.Json(new
				{
					success = false,
					error = "Attachments feature is disabled."
				}, (JsonSerializerOptions?)null, (string?)null, (int?)403);
			}
			PrepareSseResponse(httpContext);
			DateTimeOffset startedAt = DateTimeOffset.UtcNow;
			using CancellationTokenSource deadlineCts = CreateStreamDeadlineScope(options, ct);
			CancellationToken effectiveCt = deadlineCts.Token;
			using SemaphoreSlim writeLock = new SemaphoreSlim(1, 1);
			using CancellationTokenSource heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(effectiveCt);
			Task heartbeatTask = RunHeartbeatLoopAsync(httpContext, writeLock, ReadStreamHeartbeatInterval(options), heartbeatCts.Token);
			IResult empty;
			try
			{
				long firstTokenLatencyMs = 0L;
				DateTimeOffset? firstModelTokenAt = null;
				DateTimeOffset? modelStreamStartedAt = null;
				DateTimeOffset? firstTokenFlushedAt = null;
				ChatResponseDto response = null;
				await foreach (TokenChunk chunk in chat.CompleteTurnStreamAsync(dto, effectiveCt).WithCancellation(effectiveCt))
				{
					if (chunk.Type == ChatStreamChunkType.Token && !string.IsNullOrEmpty(chunk.Content))
					{
						if (!firstModelTokenAt.HasValue)
						{
							firstModelTokenAt = chunk.TimestampUtc;
							modelStreamStartedAt = chunk.ModelStreamStartedAtUtc;
						}
						string tokenEvent = JsonSerializer.Serialize(new
						{
							type = "token",
							content = chunk.Content,
							offset = chunk.Offset,
							conversationId = chunk.ConversationId,
							turnId = chunk.TurnId,
							timestamp = chunk.TimestampUtc
						});
						await WriteSseEventAsync(httpContext, writeLock, tokenEvent, effectiveCt);
						if (!firstTokenFlushedAt.HasValue)
						{
							firstTokenFlushedAt = DateTimeOffset.UtcNow;
							firstTokenLatencyMs = (long)(firstTokenFlushedAt.Value - startedAt).TotalMilliseconds;
						}
					}
					else if (chunk.Type == ChatStreamChunkType.Stage && !string.IsNullOrEmpty(chunk.Stage))
					{
						string stageEvent = JsonSerializer.Serialize(new
						{
							type = "stage",
							stage = chunk.Stage,
							offset = chunk.Offset,
							resumeCursor = chunk.ResumeCursor,
							conversationId = chunk.ConversationId,
							turnId = chunk.TurnId,
							timestamp = chunk.TimestampUtc
						});
						await WriteSseEventAsync(httpContext, writeLock, stageEvent, effectiveCt);
					}
					else if (chunk.Type == ChatStreamChunkType.Warning && !string.IsNullOrEmpty(chunk.Content))
					{
						string warningEvent = JsonSerializer.Serialize(new
						{
							type = "warning",
							content = chunk.Content,
							code = chunk.WarningCode,
							offset = chunk.Offset,
							resumeCursor = chunk.ResumeCursor,
							conversationId = chunk.ConversationId,
							turnId = chunk.TurnId,
							timestamp = chunk.TimestampUtc
						});
						await WriteSseEventAsync(httpContext, writeLock, warningEvent, effectiveCt);
					}
					else if (chunk.Type == ChatStreamChunkType.Done && (object)chunk.FinalResponse != null)
					{
						response = chunk.FinalResponse;
					}
				}
				if ((object)response == null)
				{
					throw new InvalidOperationException("Streaming pipeline completed without final response payload.");
				}
				if (!flags.EnhancedGroundingEnabled || !flags.GroundingV2Enabled)
				{
					response = response with
					{
						GroundingStatus = null,
						CitationCoverage = 0.0,
						VerifiedClaims = 0,
						TotalClaims = 0,
						ClaimGroundings = null,
						UncertaintyFlags = null
					};
				}
				loggerFactory.CreateLogger("Chat").LogInformation("Streaming chat turn completed. ConversationId={ConversationId} TurnId={TurnId} CorrelationId={CorrelationId}", response.ConversationId, response.TurnId, (!httpContext.Items.TryGetValue("CorrelationId", out object cid)) ? null : cid?.ToString());
				if (string.Equals(response.GroundingStatus, "blocked", StringComparison.OrdinalIgnoreCase))
				{
					loggerFactory.CreateLogger("ChatSafety").LogWarning("Safety blocked streaming output. ConversationId={ConversationId} TurnId={TurnId} CorrelationId={CorrelationId}", response.ConversationId, response.TurnId, (!httpContext.Items.TryGetValue("CorrelationId", out object blockedCid)) ? null : blockedCid?.ToString());
				}
				string doneEvent = JsonSerializer.Serialize(BuildDoneStreamEvent(response));
				await WriteSseEventAsync(httpContext, writeLock, doneEvent, effectiveCt);
				long fullLatency = (long)(DateTimeOffset.UtcNow - startedAt).TotalMilliseconds;
				metrics.RecordTurn(BuildSuccessfulConversationTurnMetric(
					dto,
					response,
					(firstTokenLatencyMs == 0L) ? fullLatency : firstTokenLatencyMs,
					fullLatency,
					modelTtftMs: (modelStreamStartedAt.HasValue && firstModelTokenAt.HasValue) ? Math.Max(0L, (long)(firstModelTokenAt.Value - modelStreamStartedAt.Value).TotalMilliseconds) : null,
					transportTtftMs: (firstTokenFlushedAt.HasValue && firstModelTokenAt.HasValue) ? Math.Max(0L, (long)(firstTokenFlushedAt.Value - firstModelTokenAt.Value).TotalMilliseconds) : null,
					endToEndTtftMs: (firstTokenLatencyMs > 0) ? firstTokenLatencyMs : null));
				RecordHumanLikeConversationAssistantTurn(dashboard, dto.ConversationId, response);
				RecordWebResearchAssistantTurn(webResearchTelemetry, response);
				empty = Results.Empty;
			}
			catch (OperationCanceledException) when (effectiveCt.IsCancellationRequested || httpContext.RequestAborted.IsCancellationRequested)
			{
				long elapsed = (long)(DateTimeOffset.UtcNow - startedAt).TotalMilliseconds;
				metrics.RecordTurn(new ConversationTurnMetric(elapsed, elapsed, 0, IsFactualPrompt(dto.Message), HasCitations: false, 0.0, IsSuccessful: false));
				loggerFactory.CreateLogger("Chat").LogWarning("Streaming chat turn canceled or timed out. ConversationId={ConversationId} CorrelationId={CorrelationId}", dto.ConversationId, (!httpContext.Items.TryGetValue("CorrelationId", out object canceledCid)) ? null : canceledCid?.ToString());
				empty = Results.Empty;
			}
			catch (Exception ex)
			{
				long elapsed = (long)(DateTimeOffset.UtcNow - startedAt).TotalMilliseconds;
				metrics.RecordTurn(new ConversationTurnMetric(elapsed, elapsed, 0, IsFactualPrompt(dto.Message), HasCitations: false, 0.0, IsSuccessful: false));
				loggerFactory.CreateLogger("Chat").LogError(ex, "Streaming chat turn failed with unhandled exception. ConversationId={ConversationId} CorrelationId={CorrelationId}", dto.ConversationId, (!httpContext.Items.TryGetValue("CorrelationId", out object failedCid)) ? null : failedCid?.ToString());
				throw;
			}
			finally
			{
				heartbeatCts.Cancel();
				await AwaitBackgroundTaskAsync(heartbeatTask);
			}
			return empty;
		}));
    }
}


