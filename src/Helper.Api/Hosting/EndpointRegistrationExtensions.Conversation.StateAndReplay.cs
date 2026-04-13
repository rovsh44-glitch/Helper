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
    private static void MapConversationStateAndReplayEndpoints(IEndpointRouteBuilder endpoints)
    {
		endpoints.MapGet("/api/chat/{conversationId}", (Func<string, IConversationStore, IUserProfileService, IMemoryPolicyService, IMemoryInspectionService, IResult>)delegate(string conversationId, IConversationStore store, IUserProfileService userProfile, IMemoryPolicyService memoryPolicy, IMemoryInspectionService memoryInspection)
		{
			if (!store.TryGet(conversationId, out ConversationState state))
			{
				return Results.NotFound(new
				{
					error = "Conversation not found."
				});
			}
			ConversationUserProfile conversationUserProfile = userProfile.Resolve(state);
			ConversationMemoryPolicySnapshot policySnapshot = memoryPolicy.GetPolicySnapshot(state);
			IReadOnlyList<MemoryInspectionItem> activeItems = memoryInspection.BuildSnapshot(state, DateTimeOffset.UtcNow);
            string[] visibleMemoryTags = activeItems
                .Where(item => item.Type.Equals("long_term", StringComparison.OrdinalIgnoreCase))
                .Select(item => item.Content)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(64)
                .ToArray();
			return Results.Ok(new
			{
				conversationId = state.Id,
				activeBranchId = store.GetActiveBranchId(state),
				branches = store.GetBranchIds(state),
				messages = store.GetRecentMessages(state, store.GetActiveBranchId(state), 200),
				activeTurn = new
				{
					turnId = state.ActiveTurnId,
					startedAt = state.ActiveTurnStartedAt,
					hasPendingResponse = !string.IsNullOrWhiteSpace(state.ActiveTurnUserMessage)
				},
				preferences = new
				{
					longTermMemoryEnabled = policySnapshot.LongTermMemoryEnabled,
					personalMemoryConsentGranted = policySnapshot.PersonalMemoryConsentGranted,
					personalMemoryConsentAt = policySnapshot.PersonalMemoryConsentAt,
					sessionMemoryTtlMinutes = policySnapshot.SessionMemoryTtlMinutes,
					taskMemoryTtlHours = policySnapshot.TaskMemoryTtlHours,
					longTermMemoryTtlDays = policySnapshot.LongTermMemoryTtlDays,
					preferredLanguage = conversationUserProfile.Language,
					detailLevel = conversationUserProfile.DetailLevel,
					formality = conversationUserProfile.Formality,
					domainFamiliarity = conversationUserProfile.DomainFamiliarity,
					preferredStructure = conversationUserProfile.PreferredStructure,
                    warmth = conversationUserProfile.Warmth,
                    enthusiasm = conversationUserProfile.Enthusiasm,
                    directness = conversationUserProfile.Directness,
                    defaultAnswerShape = conversationUserProfile.DefaultAnswerShape,
                    searchLocalityHint = conversationUserProfile.SearchLocalityHint,
                    decisionAssertiveness = conversationUserProfile.DecisionAssertiveness,
                    clarificationTolerance = conversationUserProfile.ClarificationTolerance,
                    citationPreference = conversationUserProfile.CitationPreference,
                    repairStyle = conversationUserProfile.RepairStyle,
                    reasoningStyle = conversationUserProfile.ReasoningStyle,
                    reasoningEffort = conversationUserProfile.ReasoningEffort,
                    projectId = state.ProjectContext?.ProjectId,
                    projectLabel = state.ProjectContext?.Label,
                    projectInstructions = state.ProjectContext?.Instructions,
                    projectMemoryEnabled = state.ProjectContext?.MemoryEnabled,
                    backgroundResearchEnabled = state.BackgroundResearchEnabled,
                    proactiveUpdatesEnabled = state.ProactiveUpdatesEnabled,
					memoryTags = visibleMemoryTags,
					memoryItemsCount = activeItems.Count
				},
                projectContext = state.ProjectContext,
                backgroundTasks = state.BackgroundTasks.Select(task => new
                {
                    id = task.Id,
                    kind = task.Kind,
                    title = task.Title,
                    status = task.Status,
                    createdAtUtc = task.CreatedAtUtc,
                    dueAtUtc = task.DueAtUtc,
                    projectId = task.ProjectId,
                    notes = task.Notes
                }),
                proactiveTopics = state.ProactiveTopics.Select(topic => new
                {
                    id = topic.Id,
                    topic = topic.Topic,
                    frequency = topic.Frequency,
                    enabled = topic.Enabled,
                    createdAtUtc = topic.CreatedAtUtc,
                    projectId = topic.ProjectId
                }),
				branchSummaries = from x in state.BranchSummaries.Values.OrderBy<ConversationBranchSummary, string>((ConversationBranchSummary x) => x.BranchId, StringComparer.OrdinalIgnoreCase)
					select new
					{
						branchId = x.BranchId,
						summary = x.Summary,
						qualityScore = x.QualityScore,
						sourceMessageCount = x.SourceMessageCount,
						updatedAt = x.UpdatedAt
					}
			});
		});
		endpoints.MapPost("/api/chat/{conversationId}/preferences", ConversationPreferenceEndpointHandler.HandleAsync);
		endpoints.MapPost("/api/chat/{conversationId}/background/{taskId}/cancel", (Func<string, string, BackgroundTaskActionRequestDto?, IConversationFollowThroughProcessor, IResult>)((string conversationId, string taskId, [FromBody] BackgroundTaskActionRequestDto? dto, IConversationFollowThroughProcessor processor) =>
		{
			if (!processor.CancelTask(conversationId, taskId, dto?.Reason))
			{
				return Results.NotFound(new
				{
					success = false,
					error = "Background task not found or cannot be canceled."
				});
			}
			return Results.Ok(new
			{
				success = true,
				taskId,
				status = "canceled"
			});
		}));
		endpoints.MapPost("/api/chat/{conversationId}/topics/{topicId}", (Func<string, string, ProactiveTopicActionRequestDto, IConversationFollowThroughProcessor, IResult>)((string conversationId, string topicId, [FromBody] ProactiveTopicActionRequestDto dto, IConversationFollowThroughProcessor processor) =>
		{
			if (!processor.SetTopicEnabled(conversationId, topicId, dto.Enabled))
			{
				return Results.NotFound(new
				{
					success = false,
					error = "Proactive topic not found."
				});
			}
			return Results.Ok(new
			{
				success = true,
				topicId,
				enabled = dto.Enabled
			});
		}));
		endpoints.MapGet("/api/chat/{conversationId}/memory", (Func<string, IConversationStore, IMemoryPolicyService, IFeatureFlags, IMemoryInspectionService, IResult>)delegate(string conversationId, IConversationStore store, IMemoryPolicyService memoryPolicy, IFeatureFlags flags, IMemoryInspectionService memoryInspection)
		{
			if (!flags.MemoryV2Enabled)
			{
				return Results.Json(new
				{
					success = false,
					error = "Memory v2 is disabled."
				}, (JsonSerializerOptions?)null, (string?)null, (int?)403);
			}
			if (!store.TryGet(conversationId, out ConversationState state))
			{
				return Results.NotFound(new
				{
					success = false,
					error = "Conversation not found."
				});
			}
			DateTimeOffset utcNow = DateTimeOffset.UtcNow;
			ConversationMemoryPolicySnapshot policySnapshot = memoryPolicy.GetPolicySnapshot(state);
			IReadOnlyList<MemoryInspectionItem> activeItems = memoryInspection.BuildSnapshot(state, utcNow);
			return Results.Ok(new
			{
				conversationId = state.Id,
				policy = new
				{
					longTermMemoryEnabled = policySnapshot.LongTermMemoryEnabled,
					personalMemoryConsentGranted = policySnapshot.PersonalMemoryConsentGranted,
					personalMemoryConsentAt = policySnapshot.PersonalMemoryConsentAt,
					sessionMemoryTtlMinutes = policySnapshot.SessionMemoryTtlMinutes,
					taskMemoryTtlHours = policySnapshot.TaskMemoryTtlHours,
					longTermMemoryTtlDays = policySnapshot.LongTermMemoryTtlDays
				},
				items = activeItems.Select((MemoryInspectionItem item) => new
				{
					id = item.Id,
					type = item.Type,
					content = item.Content,
                    scope = item.Scope.ToString().ToLowerInvariant(),
                    retention = item.Retention,
                    whyRemembered = item.WhyRemembered,
                    priority = item.Priority,
					createdAt = item.CreatedAt,
					expiresAt = item.ExpiresAt,
					sourceTurnId = item.SourceTurnId,
                    sourceProjectId = item.SourceProjectId,
					isPersonal = item.IsPersonal,
                    userEditable = item.UserEditable
				})
			});
		});
		endpoints.MapDelete("/api/chat/{conversationId}/memory/{memoryId}", (Func<string, string, IConversationStore, IMemoryPolicyService, IFeatureFlags, IResult>)delegate(string conversationId, string memoryId, IConversationStore store, IMemoryPolicyService memoryPolicy, IFeatureFlags flags)
		{
			if (!flags.MemoryV2Enabled)
			{
				return Results.Json(new
				{
					success = false,
					error = "Memory v2 is disabled."
				}, (JsonSerializerOptions?)null, (string?)null, (int?)403);
			}
			if (!store.TryGet(conversationId, out ConversationState state))
			{
				return Results.NotFound(new
				{
					success = false,
					error = "Conversation not found."
				});
			}
			if (!memoryPolicy.DeleteItem(state, memoryId, DateTimeOffset.UtcNow))
			{
				return Results.NotFound(new
				{
					success = false,
					error = "Memory item not found."
				});
			}
			store.MarkUpdated(state);
			return Results.Ok(new
			{
				success = true
			});
		});
		endpoints.MapPost("/api/chat/{conversationId}/resume", (Func<string, ChatResumeRequestDto, IChatOrchestrator, IConversationMetricsService, IHumanLikeConversationDashboardService, IWebResearchTelemetryService, HttpContext, ILoggerFactory, CancellationToken, Task<IResult>>)(async (string conversationId, [FromBody] ChatResumeRequestDto dto, IChatOrchestrator chat, IConversationMetricsService metrics, IHumanLikeConversationDashboardService dashboard, IWebResearchTelemetryService webResearchTelemetry, HttpContext httpContext, ILoggerFactory loggerFactory, CancellationToken ct) =>
		{
			try
			{
				DateTimeOffset startedAt = DateTimeOffset.UtcNow;
				ChatResponseDto response = await chat.ResumeActiveTurnAsync(conversationId, dto, ct);
				loggerFactory.CreateLogger("Chat").LogInformation("Resumed chat turn. ConversationId={ConversationId} TurnId={TurnId} CorrelationId={CorrelationId}", response.ConversationId, response.TurnId, (!httpContext.Items.TryGetValue("CorrelationId", out object cid)) ? null : cid?.ToString());
				long elapsed = (long)(DateTimeOffset.UtcNow - startedAt).TotalMilliseconds;
				metrics.RecordTurn(BuildSuccessfulConversationTurnMetric(
					new ChatRequestDto(string.Empty, conversationId, dto.MaxHistory, dto.SystemInstruction, IdempotencyKey: dto.IdempotencyKey),
					response,
					elapsed,
					elapsed,
					isFactualPromptOverride: true));
				RecordHumanLikeConversationAssistantTurn(dashboard, conversationId, response);
				RecordWebResearchAssistantTurn(webResearchTelemetry, response);
				return Results.Ok(response);
			}
			catch (KeyNotFoundException ex)
			{
				KeyNotFoundException ex2 = ex;
				return Results.NotFound(new
				{
					success = false,
					error = ex2.Message
				});
			}
			catch (InvalidOperationException ex3)
			{
				InvalidOperationException ex4 = ex3;
				return Results.Json(new
				{
					success = false,
					error = ex4.Message
				}, (JsonSerializerOptions?)null, (string?)null, (int?)409);
			}
		}));
		endpoints.MapPost("/api/chat/{conversationId}/turns/{turnId}/regenerate", (Func<string, string, TurnRegenerateRequestDto, IChatOrchestrator, IHumanLikeConversationDashboardService, IWebResearchTelemetryService, IFeatureFlags, CancellationToken, Task<IResult>>)(async (string conversationId, string turnId, [FromBody] TurnRegenerateRequestDto dto, IChatOrchestrator chat, IHumanLikeConversationDashboardService dashboard, IWebResearchTelemetryService webResearchTelemetry, IFeatureFlags flags, CancellationToken ct) =>
		{
			if (!flags.RegenerateEnabled)
			{
				return Results.Json(new
				{
					success = false,
					error = "Regenerate feature is disabled."
				}, (JsonSerializerOptions?)null, (string?)null, (int?)403);
			}
			try
			{
				ChatResponseDto response = await chat.RegenerateTurnAsync(conversationId, turnId, dto, ct);
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
				RecordHumanLikeConversationAssistantTurn(dashboard, conversationId, response);
				RecordWebResearchAssistantTurn(webResearchTelemetry, response);
				return Results.Ok(response);
			}
			catch (KeyNotFoundException ex)
			{
				return Results.NotFound(new
				{
					success = false,
					error = ex.Message
				});
			}
			catch (InvalidOperationException ex2)
			{
				return Results.Json(new
				{
					success = false,
					error = ex2.Message
				}, (JsonSerializerOptions?)null, (string?)null, (int?)409);
			}
		}));
    }

}
