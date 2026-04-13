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
    private static void MapConversationBranchAndFeedbackEndpoints(IEndpointRouteBuilder endpoints)
    {
		endpoints.MapPost("/api/chat/{conversationId}/branches", (Func<string, BranchCreateRequestDto, IChatOrchestrator, IFeatureFlags, CancellationToken, Task<IResult>>)(async (string conversationId, [FromBody] BranchCreateRequestDto dto, IChatOrchestrator chat, IFeatureFlags flags, CancellationToken ct) =>
		{
			if (!flags.BranchingEnabled)
			{
				return Results.Json(new
				{
					success = false,
					error = "Branching feature is disabled."
				}, (JsonSerializerOptions?)null, (string?)null, (int?)403);
			}
			(bool Success, string BranchId, string Error) result = await chat.CreateBranchAsync(conversationId, dto.FromTurnId, dto.BranchId, ct);
			return (!result.Success) ? Results.Json(new
			{
				success = false,
				error = result.Error
			}, (JsonSerializerOptions?)null, (string?)null, (int?)409) : Results.Ok(new
			{
				success = true,
				branchId = result.BranchId
			});
		}));
		endpoints.MapPost("/api/chat/{conversationId}/branches/{branchId}/activate", (Func<string, string, IChatOrchestrator, IFeatureFlags, CancellationToken, Task<IResult>>)async delegate(string conversationId, string branchId, IChatOrchestrator chat, IFeatureFlags flags, CancellationToken ct)
		{
			if (!flags.BranchingEnabled)
			{
				return Results.Json(new
				{
					success = false,
					error = "Branching feature is disabled."
				}, (JsonSerializerOptions?)null, (string?)null, (int?)403);
			}
			(bool Success, string Error) result = await chat.ActivateBranchAsync(conversationId, branchId, ct);
			return (!result.Success) ? Results.Json(new
			{
				success = false,
				error = result.Error
			}, (JsonSerializerOptions?)null, (string?)null, (int?)409) : Results.Ok(new
			{
				success = true,
				branchId = branchId
			});
		});
		endpoints.MapGet("/api/chat/{conversationId}/branches/compare", (Func<string, string, string, IConversationStore, IFeatureFlags, IResult>)((string conversationId, [FromQuery] string sourceBranchId, [FromQuery] string targetBranchId, IConversationStore store, IFeatureFlags flags) =>
		{
			if (!flags.BranchingEnabled || !flags.BranchMergeEnabled)
			{
				return Results.Json(new
				{
					success = false,
					error = "Branch compare feature is disabled."
				}, (JsonSerializerOptions?)null, (string?)null, (int?)403);
			}
			if (string.IsNullOrWhiteSpace(sourceBranchId) || string.IsNullOrWhiteSpace(targetBranchId))
			{
				return Results.BadRequest(new
				{
					success = false,
					error = "sourceBranchId and targetBranchId are required."
				});
			}
			if (!store.TryGet(conversationId, out ConversationState state))
			{
				return Results.NotFound(new
				{
					success = false,
					error = "Conversation not found."
				});
			}
			string source = sourceBranchId.Trim();
			string target = targetBranchId.Trim();
			List<ChatMessageDto> list;
			List<ChatMessageDto> list2;
			ConversationBranchSummary value;
			ConversationBranchSummary value2;
			lock (state.SyncRoot)
			{
				if (!state.Branches.ContainsKey(source) || !state.Branches.ContainsKey(target))
				{
					return Results.NotFound(new
					{
						success = false,
						error = "One or both branches do not exist."
					});
				}
				list = (from m in state.Messages
					where string.Equals(m.BranchId ?? "main", source, StringComparison.OrdinalIgnoreCase)
					orderby m.Timestamp
					select m).ToList();
				list2 = (from m in state.Messages
					where string.Equals(m.BranchId ?? "main", target, StringComparison.OrdinalIgnoreCase)
					orderby m.Timestamp
					select m).ToList();
				state.BranchSummaries.TryGetValue(source, out value);
				state.BranchSummaries.TryGetValue(target, out value2);
			}
			HashSet<string> hashSet = (from m in list
				where !string.IsNullOrWhiteSpace(m.TurnId)
				select m.TurnId).ToHashSet<string>(StringComparer.OrdinalIgnoreCase);
			HashSet<string> hashSet2 = (from m in list2
				where !string.IsNullOrWhiteSpace(m.TurnId)
				select m.TurnId).ToHashSet<string>(StringComparer.OrdinalIgnoreCase);
			string[] sharedTurnIds = (from x in hashSet.Intersect<string>(hashSet2, StringComparer.OrdinalIgnoreCase)
				orderby x
				select x).ToArray();
			string[] sourceOnlyTurnIds = (from x in hashSet.Except<string>(hashSet2, StringComparer.OrdinalIgnoreCase)
				orderby x
				select x).ToArray();
			string[] targetOnlyTurnIds = (from x in hashSet2.Except<string>(hashSet, StringComparer.OrdinalIgnoreCase)
				orderby x
				select x).ToArray();
			var sourceOnlyMessages = (from m in list.Where((ChatMessageDto m) => !string.IsNullOrWhiteSpace(m.TurnId) && sourceOnlyTurnIds.Contains<string>(m.TurnId, StringComparer.OrdinalIgnoreCase)).Take(20)
				select new
				{
					role = m.Role,
					turnId = m.TurnId,
					timestamp = m.Timestamp,
					contentPreview = TruncateForPreview(m.Content, 160),
					provenance = new
					{
						toolCalls = (m.ToolCalls?.Count ?? 0),
						citations = (m.Citations?.Count ?? 0),
						attachments = (m.Attachments?.Count ?? 0)
					}
				}).ToArray();
			return Results.Ok(new
			{
				success = true,
				conversationId = conversationId,
				sourceBranchId = source,
				targetBranchId = target,
				sourceSummary = value?.Summary,
				targetSummary = value2?.Summary,
				sourceMessageCount = list.Count,
				targetMessageCount = list2.Count,
				sharedTurnIds = sharedTurnIds,
				sourceOnlyTurnIds = sourceOnlyTurnIds,
				targetOnlyTurnIds = targetOnlyTurnIds,
				sourceOnlyMessages = sourceOnlyMessages
			});
		}));
		endpoints.MapPost("/api/chat/{conversationId}/branches/merge", (Func<string, BranchMergeRequestDto, IChatOrchestrator, IFeatureFlags, CancellationToken, Task<IResult>>)(async (string conversationId, [FromBody] BranchMergeRequestDto dto, IChatOrchestrator chat, IFeatureFlags flags, CancellationToken ct) =>
		{
			if (!flags.BranchingEnabled || !flags.BranchMergeEnabled)
			{
				return Results.Json(new
				{
					success = false,
					error = "Branch merge feature is disabled."
				}, (JsonSerializerOptions?)null, (string?)null, (int?)403);
			}
			(bool Success, int MergedMessages, string Error) result = await chat.MergeBranchAsync(conversationId, dto.SourceBranchId, dto.TargetBranchId, ct);
			return (!result.Success) ? Results.Json(new
			{
				success = false,
				error = result.Error
			}, (JsonSerializerOptions?)null, (string?)null, (int?)409) : Results.Ok(new
			{
				success = true,
				sourceBranchId = dto.SourceBranchId,
				targetBranchId = dto.TargetBranchId,
				mergedMessages = result.MergedMessages
			});
		}));
		endpoints.MapPost("/api/chat/{conversationId}/repair", (Func<string, ConversationRepairRequestDto, IChatOrchestrator, IHumanLikeConversationDashboardService, IWebResearchTelemetryService, IFeatureFlags, CancellationToken, Task<IResult>>)(async (string conversationId, [FromBody] ConversationRepairRequestDto dto, IChatOrchestrator chat, IHumanLikeConversationDashboardService dashboard, IWebResearchTelemetryService webResearchTelemetry, IFeatureFlags flags, CancellationToken ct) =>
		{
			if (!flags.ConversationRepairEnabled)
			{
				return Results.Json(new
				{
					success = false,
					error = "Conversation repair feature is disabled."
				}, (JsonSerializerOptions?)null, (string?)null, (int?)403);
			}
			if (string.IsNullOrWhiteSpace(dto.CorrectedIntent))
			{
				return Results.BadRequest(new
				{
					success = false,
					error = "CorrectedIntent is required."
				});
			}
			dashboard.RecordRepairAttempt(conversationId, dto.TurnId, DateTimeOffset.UtcNow);
			try
			{
				ChatResponseDto response = await chat.RepairConversationAsync(conversationId, dto, ct);
				dashboard.RecordRepairOutcome(succeeded: true, DateTimeOffset.UtcNow);
				RecordHumanLikeConversationAssistantTurn(dashboard, conversationId, response);
				RecordWebResearchAssistantTurn(webResearchTelemetry, response);
				return Results.Ok(response);
			}
			catch (KeyNotFoundException ex)
			{
				dashboard.RecordRepairOutcome(succeeded: false, DateTimeOffset.UtcNow);
				return Results.NotFound(new
				{
					success = false,
					error = ex.Message
				});
			}
			catch (InvalidOperationException ex2)
			{
				dashboard.RecordRepairOutcome(succeeded: false, DateTimeOffset.UtcNow);
				return Results.Json(new
				{
					success = false,
					error = ex2.Message
				}, (JsonSerializerOptions?)null, (string?)null, (int?)409);
			}
		}));
		endpoints.MapPost("/api/chat/{conversationId}/feedback", (Func<string, FeedbackRequestDto, IHelpfulnessTelemetryService, IHumanLikeConversationDashboardService, IResult>)((string conversationId, [FromBody] FeedbackRequestDto dto, IHelpfulnessTelemetryService telemetry, IHumanLikeConversationDashboardService dashboard) =>
		{
			if (dto.Rating < 1 || dto.Rating > 5)
			{
				return Results.BadRequest(new
				{
					success = false,
					error = "Rating must be in range 1..5."
				});
			}
			telemetry.Record(conversationId, dto.TurnId, dto.Rating, dto.Tags, dto.Comment);
			dashboard.RecordFeedback(conversationId, dto.TurnId, dto.Rating, dto.Tags, dto.Comment, DateTimeOffset.UtcNow);
			return Results.Ok(new
			{
				success = true,
				snapshot = telemetry.GetConversationSnapshot(conversationId)
			});
		}));
		endpoints.MapDelete("/api/chat/{conversationId}", (Func<string, IConversationStore, IResult>)((string conversationId, IConversationStore store) => store.Remove(conversationId) ? Results.Ok(new
		{
			success = true
		}) : Results.NotFound(new
		{
			success = false,
			error = "Conversation not found."
		})));
    }
}


