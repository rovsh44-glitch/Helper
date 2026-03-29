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
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Helper.Api.Hosting;

public static partial class EndpointRegistrationExtensions
{
    private static void MapConversationStreamResumeEndpoints(IEndpointRouteBuilder endpoints)
    {
		endpoints.MapPost("/api/chat/{conversationId}/stream/resume", (Func<string, ChatStreamResumeRequestDto, IChatOrchestrator, IConversationStore, IConversationMetricsService, IHumanLikeConversationDashboardService, IWebResearchTelemetryService, IFeatureFlags, Helper.Api.Backend.Configuration.IBackendOptionsCatalog, HttpContext, CancellationToken, Task<IResult>>)(async (string conversationId, [FromBody] ChatStreamResumeRequestDto dto, IChatOrchestrator chat, IConversationStore store, IConversationMetricsService metrics, IHumanLikeConversationDashboardService dashboard, IWebResearchTelemetryService webResearchTelemetry, IFeatureFlags flags, Helper.Api.Backend.Configuration.IBackendOptionsCatalog options, HttpContext httpContext, CancellationToken ct) =>
		{
			if (!flags.StreamingV2Enabled)
			{
				return Results.Json(new
				{
					success = false,
					error = "Streaming v2 is disabled."
				}, (JsonSerializerOptions?)null, (string?)null, (int?)403);
			}
			PrepareSseResponse(httpContext);
			DateTimeOffset startedAt = DateTimeOffset.UtcNow;
			using CancellationTokenSource deadlineCts = CreateStreamDeadlineScope(options, ct);
			CancellationToken effectiveCt = deadlineCts.Token;
			using SemaphoreSlim writeLock = new SemaphoreSlim(1, 1);
			using CancellationTokenSource heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(effectiveCt);
			Task heartbeatTask = RunHeartbeatLoopAsync(httpContext, writeLock, ReadStreamHeartbeatInterval(options), heartbeatCts.Token);
			IResult result;
			try
			{
				ChatResponseDto response;
				bool replayedFromHistory = ChatStreamResumeHelper.TryBuildReplayResponse(store, conversationId, dto, out response);
				if (!replayedFromHistory)
				{
					response = await chat.ResumeActiveTurnAsync(conversationId, new ChatResumeRequestDto(dto.MaxHistory, dto.SystemInstruction, dto.IdempotencyKey), effectiveCt);
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
				string fullResponse = response.Response ?? string.Empty;
				int safeCursor = ChatStreamResumeHelper.NormalizeCursorOffset(fullResponse, dto.CursorOffset);
				long firstTokenLatencyMs = 0L;
				int emittedOffset = safeCursor;
				foreach (string chunk in ChatStreamResumeHelper.SplitRemainingResponse(fullResponse, safeCursor))
				{
					emittedOffset += chunk.Length;
					if (firstTokenLatencyMs == 0)
					{
						firstTokenLatencyMs = (long)(DateTimeOffset.UtcNow - startedAt).TotalMilliseconds;
					}
					string tokenEvent = JsonSerializer.Serialize(new
					{
						type = "token",
						content = chunk,
						offset = emittedOffset,
						conversationId = response.ConversationId,
						turnId = response.TurnId,
						timestamp = DateTimeOffset.UtcNow
					});
					await WriteSseEventAsync(httpContext, writeLock, tokenEvent, effectiveCt);
				}
				string doneEvent = JsonSerializer.Serialize(BuildDoneStreamEvent(response));
				await WriteSseEventAsync(httpContext, writeLock, doneEvent, effectiveCt);
				long fullLatency = (long)(DateTimeOffset.UtcNow - startedAt).TotalMilliseconds;
				var resumeMessage = response.Messages.LastOrDefault((ChatMessageDto m) => m.Role.Equals("user", StringComparison.OrdinalIgnoreCase))?.Content ?? string.Empty;
				metrics.RecordTurn(BuildSuccessfulConversationTurnMetric(
					new ChatRequestDto(resumeMessage, conversationId, dto.MaxHistory, dto.SystemInstruction, IdempotencyKey: dto.IdempotencyKey),
					response,
					(firstTokenLatencyMs == 0L) ? fullLatency : firstTokenLatencyMs,
					fullLatency,
					endToEndTtftMs: (firstTokenLatencyMs == 0L) ? fullLatency : firstTokenLatencyMs));
				RecordHumanLikeConversationAssistantTurn(dashboard, conversationId, response);
				RecordWebResearchAssistantTurn(webResearchTelemetry, response);
				result = Results.Empty;
			}
			catch (OperationCanceledException) when (effectiveCt.IsCancellationRequested || httpContext.RequestAborted.IsCancellationRequested)
			{
				long elapsed = (long)(DateTimeOffset.UtcNow - startedAt).TotalMilliseconds;
				metrics.RecordTurn(new ConversationTurnMetric(elapsed, elapsed, 0, false, HasCitations: false, 0.0, IsSuccessful: false));
				result = Results.Empty;
			}
			catch (KeyNotFoundException ex)
			{
				KeyNotFoundException ex2 = ex;
				result = Results.NotFound(new
				{
					success = false,
					error = ex2.Message
				});
			}
			catch (InvalidOperationException ex3)
			{
				InvalidOperationException ex4 = ex3;
				result = Results.Json(new
				{
					success = false,
					error = ex4.Message
				}, (JsonSerializerOptions?)null, (string?)null, (int?)409);
			}
			finally
			{
				heartbeatCts.Cancel();
				await AwaitBackgroundTaskAsync(heartbeatTask);
			}
			return result;
		}));
    }
}


