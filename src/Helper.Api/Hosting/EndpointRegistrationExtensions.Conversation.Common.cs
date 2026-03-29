#nullable enable
#pragma warning disable CS8600, CS8601, CS8602, CS8603, CS8604, CS8619, CS8622, CS8632

using System;
using Helper.Api.Conversation;
using Helper.Runtime.Core;
using Microsoft.AspNetCore.Http;

namespace Helper.Api.Hosting;

public static partial class EndpointRegistrationExtensions
{
    private static bool AttachmentsBlocked(ChatRequestDto dto, IFeatureFlags flags)
        => !flags.AttachmentsEnabled && (dto.Attachments?.Count ?? 0) > 0;

    private static ChatResponseDto ApplyGroundingPolicy(ChatResponseDto response, IFeatureFlags flags)
    {
        if (flags.EnhancedGroundingEnabled && flags.GroundingV2Enabled)
        {
            return response;
        }

        return response with
        {
            GroundingStatus = null,
            CitationCoverage = 0.0,
            VerifiedClaims = 0,
            TotalClaims = 0,
            ClaimGroundings = null,
            UncertaintyFlags = null
        };
    }

    private static IResult FeatureDisabled(string message)
        => Results.Json(new
        {
            success = false,
            error = message
        }, (System.Text.Json.JsonSerializerOptions?)null, (string?)null, 403);

    private static IResult ConflictResult(string message)
        => Results.Json(new
        {
            success = false,
            error = message
        }, (System.Text.Json.JsonSerializerOptions?)null, (string?)null, 409);

    private static IResult NotFoundResult(string message)
        => Results.NotFound(new
        {
            success = false,
            error = message
        });

    private static ConversationTurnMetric BuildSuccessfulConversationTurnMetric(
        ChatRequestDto request,
        ChatResponseDto response,
        long firstTokenLatencyMs,
        long fullResponseLatencyMs,
        bool? isFactualPromptOverride = null,
        long? modelTtftMs = null,
        long? transportTtftMs = null,
        long? endToEndTtftMs = null)
    {
        return new ConversationTurnMetric(
            FirstTokenLatencyMs: firstTokenLatencyMs,
            FullResponseLatencyMs: fullResponseLatencyMs,
            ToolCallsCount: response.ToolCalls?.Count ?? 0,
            IsFactualPrompt: isFactualPromptOverride ?? IsFactualPrompt(request.Message),
            HasCitations: (response.Sources?.Count ?? 0) > 0,
            Confidence: response.Confidence,
            IsSuccessful: true,
            VerifiedClaims: response.VerifiedClaims,
            TotalClaims: response.TotalClaims,
            ModelTtftMs: modelTtftMs,
            TransportTtftMs: transportTtftMs,
            EndToEndTtftMs: endToEndTtftMs,
            ExecutionMode: response.ExecutionMode,
            BudgetExceeded: response.BudgetExceeded,
            Intent: response.Intent,
            ResearchClarificationFallback: IsResearchClarificationFallback(response),
            Reasoning: BuildReasoningTurnMetric(response),
            Style: BuildStyleTurnMetric(response));
    }

    private static void RecordHumanLikeConversationUserTurn(IHumanLikeConversationDashboardService dashboard, string? conversationId)
    {
        ArgumentNullException.ThrowIfNull(dashboard);
        dashboard.RecordUserTurn(conversationId, DateTimeOffset.UtcNow);
    }

    private static void RecordHumanLikeConversationAssistantTurn(
        IHumanLikeConversationDashboardService dashboard,
        string? requestConversationId,
        ChatResponseDto response)
    {
        ArgumentNullException.ThrowIfNull(dashboard);
        ArgumentNullException.ThrowIfNull(response);
        dashboard.RecordAssistantTurn(response.ConversationId ?? requestConversationId, response, response.Timestamp);
    }

    private static void RecordWebResearchAssistantTurn(IWebResearchTelemetryService telemetry, ChatResponseDto response)
    {
        ArgumentNullException.ThrowIfNull(telemetry);
        ArgumentNullException.ThrowIfNull(response);
        telemetry.RecordResponse(response);
    }

    private static ReasoningTurnMetric? BuildReasoningTurnMetric(ChatResponseDto response)
    {
        var reasoning = response.ReasoningMetrics;
        if (reasoning is null || !reasoning.PathActive)
        {
            return null;
        }

        return new ReasoningTurnMetric(
            PathActive: true,
            BranchingApplied: reasoning.BranchingApplied,
            BranchesExplored: Math.Max(0, reasoning.BranchesExplored),
            CandidatesRejected: Math.Max(0, reasoning.CandidatesRejected),
            LocalVerificationChecks: Math.Max(0, reasoning.LocalVerificationChecks),
            LocalVerificationPasses: Math.Max(0, reasoning.LocalVerificationPasses),
            LocalVerificationRejects: Math.Max(0, reasoning.LocalVerificationRejects),
            ModelCallsUsed: Math.Max(0, reasoning.ModelCallsUsed),
            RetrievalChunksUsed: Math.Max(0, reasoning.RetrievalChunksUsed),
            ProceduralLessonsUsed: Math.Max(0, reasoning.ProceduralLessonsUsed),
            ApproximateTokenCost: Math.Max(0, reasoning.ApproximateTokenCost));
    }

    private static ConversationStyleTurnMetric? BuildStyleTurnMetric(ChatResponseDto response)
    {
        var style = response.StyleTelemetry;
        if (style is null)
        {
            return null;
        }

        return new ConversationStyleTurnMetric(
            LeadPhraseFingerprint: style.LeadPhraseFingerprint,
            MixedLanguageDetected: style.MixedLanguageDetected,
            GenericClarificationDetected: style.GenericClarificationDetected,
            GenericNextStepDetected: style.GenericNextStepDetected,
            MemoryAckTemplateDetected: style.MemoryAckTemplateDetected,
            SourceFingerprint: style.SourceFingerprint);
    }
}

