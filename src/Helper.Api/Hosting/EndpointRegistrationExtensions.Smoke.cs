#nullable enable
// Minimal API glue in these partials triggers noisy false positives from Roslyn nullability analysis.
// Keep suppressions local to endpoint registration instead of project-wide.
#pragma warning disable CS8600, CS8601, CS8602, CS8603, CS8604, CS8619, CS8622, CS8632

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Helper.Api.Conversation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Helper.Api.Hosting;

public static partial class EndpointRegistrationExtensions
{
    private const string DeterministicSmokePromptToken = "__HELPER_SMOKE_READY__";
    private const string DeterministicSmokeResponseText = "READY";
    private const string DeterministicLongSmokePromptToken = "__HELPER_SMOKE_LONG_STREAM__";
    private const int DeterministicLongSmokeChunkCount = 96;

    private static void MapSmokeEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/smoke", (Func<IResult>)(() => Results.Ok(BuildDeterministicSmokeDescriptor())));
        endpoints.MapGet("/api/smoke/long", (Func<IResult>)(() => Results.Ok(BuildDeterministicLongSmokeDescriptor())));
        endpoints.MapGet("/api/smoke/stream", (Func<HttpContext, CancellationToken, Task<IResult>>)(async (HttpContext httpContext, CancellationToken ct) =>
        {
            ChatRequestDto request = new ChatRequestDto(DeterministicSmokePromptToken, null, 1, "deterministic_smoke");
            ChatResponseDto response = BuildDeterministicSmokeResponse(request);
            await WriteDeterministicSmokeStreamAsync(httpContext, response, ct);
            return Results.Empty;
        }));
        endpoints.MapGet("/api/smoke/stream/long", (Func<HttpContext, CancellationToken, Task<IResult>>)(async (HttpContext httpContext, CancellationToken ct) =>
        {
            ChatRequestDto request = new ChatRequestDto(DeterministicLongSmokePromptToken, null, 1, "deterministic_smoke_long");
            ChatResponseDto response = BuildDeterministicLongSmokeResponse(request);
            await WriteDeterministicLongSmokeStreamAsync(httpContext, response, ct);
            return Results.Empty;
        }));
    }

    private static bool IsDeterministicSmokePrompt(ChatRequestDto dto)
    {
        return string.Equals(dto.Message?.Trim(), DeterministicSmokePromptToken, StringComparison.Ordinal);
    }

    private static bool IsDeterministicLongSmokePrompt(ChatRequestDto dto)
    {
        return string.Equals(dto.Message?.Trim(), DeterministicLongSmokePromptToken, StringComparison.Ordinal);
    }

    private static object BuildDeterministicSmokeDescriptor()
    {
        return new
        {
            success = true,
            mode = "deterministic_smoke",
            promptToken = DeterministicSmokePromptToken,
            response = DeterministicSmokeResponseText,
            streamEndpoint = "/api/smoke/stream",
            timestamp = DateTimeOffset.UtcNow
        };
    }

    private static object BuildDeterministicLongSmokeDescriptor()
    {
        return new
        {
            success = true,
            mode = "deterministic_smoke_long",
            promptToken = DeterministicLongSmokePromptToken,
            chunkCount = DeterministicLongSmokeChunkCount,
            responseLength = BuildDeterministicLongSmokeResponseText().Length,
            streamEndpoint = "/api/smoke/stream/long",
            timestamp = DateTimeOffset.UtcNow
        };
    }

    private static ChatResponseDto BuildDeterministicSmokeResponse(ChatRequestDto dto)
    {
        string conversationId = string.IsNullOrWhiteSpace(dto.ConversationId) ? Guid.NewGuid().ToString("N") : dto.ConversationId.Trim();
        string turnId = Guid.NewGuid().ToString("N");
        string branchId = string.IsNullOrWhiteSpace(dto.BranchId) ? "main" : dto.BranchId.Trim();
        DateTimeOffset timestamp = DateTimeOffset.UtcNow;

        ChatMessageDto userMessage = new ChatMessageDto(
            "user",
            dto.Message,
            timestamp,
            TurnId: turnId,
            BranchId: branchId,
            Attachments: dto.Attachments);

        ChatMessageDto assistantMessage = new ChatMessageDto(
            "assistant",
            DeterministicSmokeResponseText,
            timestamp,
            TurnId: turnId,
            BranchId: branchId,
            ToolCalls: Array.Empty<string>(),
            Citations: Array.Empty<string>());

        return new ChatResponseDto(
            conversationId,
            DeterministicSmokeResponseText,
            new[] { userMessage, assistantMessage },
            timestamp,
            Confidence: 1.0,
            Sources: Array.Empty<string>(),
            TurnId: turnId,
            ToolCalls: Array.Empty<string>(),
            RequiresConfirmation: false,
            NextStep: null,
            GroundingStatus: "deterministic_smoke",
            CitationCoverage: 0.0,
            VerifiedClaims: 0,
            TotalClaims: 0,
            UncertaintyFlags: Array.Empty<string>(),
            BranchId: branchId,
            AvailableBranches: new[] { branchId },
            ClaimGroundings: Array.Empty<ClaimGrounding>(),
            ExecutionMode: "deterministic_smoke",
            BudgetExceeded: false,
            EstimatedTokensGenerated: DeterministicSmokeResponseText.Length,
            Intent: "smoke",
            IntentConfidence: 1.0);
    }

    private static ChatResponseDto BuildDeterministicLongSmokeResponse(ChatRequestDto dto)
    {
        string responseText = BuildDeterministicLongSmokeResponseText();
        string conversationId = string.IsNullOrWhiteSpace(dto.ConversationId) ? Guid.NewGuid().ToString("N") : dto.ConversationId.Trim();
        string turnId = Guid.NewGuid().ToString("N");
        string branchId = string.IsNullOrWhiteSpace(dto.BranchId) ? "main" : dto.BranchId.Trim();
        DateTimeOffset timestamp = DateTimeOffset.UtcNow;

        ChatMessageDto userMessage = new ChatMessageDto(
            "user",
            dto.Message,
            timestamp,
            TurnId: turnId,
            BranchId: branchId,
            Attachments: dto.Attachments);

        ChatMessageDto assistantMessage = new ChatMessageDto(
            "assistant",
            responseText,
            timestamp,
            TurnId: turnId,
            BranchId: branchId,
            ToolCalls: Array.Empty<string>(),
            Citations: Array.Empty<string>());

        return new ChatResponseDto(
            conversationId,
            responseText,
            new[] { userMessage, assistantMessage },
            timestamp,
            Confidence: 1.0,
            Sources: Array.Empty<string>(),
            TurnId: turnId,
            ToolCalls: Array.Empty<string>(),
            RequiresConfirmation: false,
            NextStep: null,
            GroundingStatus: "deterministic_smoke_long",
            CitationCoverage: 0.0,
            VerifiedClaims: 0,
            TotalClaims: 0,
            UncertaintyFlags: Array.Empty<string>(),
            BranchId: branchId,
            AvailableBranches: new[] { branchId },
            ClaimGroundings: Array.Empty<ClaimGrounding>(),
            ExecutionMode: "deterministic_smoke_long",
            BudgetProfile: "chat_light",
            BudgetExceeded: false,
            EstimatedTokensGenerated: responseText.Length,
            Intent: "smoke_long",
            IntentConfidence: 1.0);
    }

    private static void PersistDeterministicSmokeConversation(IConversationStore store, ChatResponseDto response)
    {
        ConversationState state = store.GetOrCreate(response.ConversationId);
        foreach (ChatMessageDto message in response.Messages)
        {
            store.AddMessage(state, message);
        }
    }

    private static async Task WriteDeterministicSmokeStreamAsync(HttpContext httpContext, ChatResponseDto response, CancellationToken ct)
    {
        PrepareSseResponse(httpContext);
        using SemaphoreSlim writeLock = new SemaphoreSlim(1, 1);

        string tokenEvent = JsonSerializer.Serialize(new
        {
            type = "token",
            content = response.Response,
            offset = response.Response.Length,
            conversationId = response.ConversationId,
            turnId = response.TurnId,
            timestamp = response.Timestamp
        });

        await WriteSseEventAsync(httpContext, writeLock, tokenEvent, ct);

        string doneEvent = JsonSerializer.Serialize(BuildDoneStreamEvent(response));
        await WriteSseEventAsync(httpContext, writeLock, doneEvent, ct);
    }

    private static async Task WriteDeterministicLongSmokeStreamAsync(HttpContext httpContext, ChatResponseDto response, CancellationToken ct)
    {
        PrepareSseResponse(httpContext);
        using SemaphoreSlim writeLock = new SemaphoreSlim(1, 1);
        int offset = 0;
        DateTimeOffset timestamp = DateTimeOffset.UtcNow;

        foreach (string chunk in BuildDeterministicLongSmokeChunks())
        {
            offset += chunk.Length;
            string tokenEvent = JsonSerializer.Serialize(new
            {
                type = "token",
                content = chunk,
                offset,
                conversationId = response.ConversationId,
                turnId = response.TurnId,
                timestamp
            });

            await WriteSseEventAsync(httpContext, writeLock, tokenEvent, ct);
        }

        string doneEvent = JsonSerializer.Serialize(BuildDoneStreamEvent(response));
        await WriteSseEventAsync(httpContext, writeLock, doneEvent, ct);
    }

    private static string BuildDeterministicLongSmokeResponseText()
    {
        return string.Concat(BuildDeterministicLongSmokeChunks());
    }

    private static IReadOnlyList<string> BuildDeterministicLongSmokeChunks()
    {
        return Enumerable
            .Range(1, DeterministicLongSmokeChunkCount)
            .Select(index => $"chunk-{index:000} ")
            .ToArray();
    }
}

