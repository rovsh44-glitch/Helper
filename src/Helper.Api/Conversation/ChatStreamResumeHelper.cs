using Helper.Api.Hosting;

namespace Helper.Api.Conversation;

public static class ChatStreamResumeHelper
{
    public static bool TryBuildReplayResponse(
        IConversationStore store,
        string conversationId,
        ChatStreamResumeRequestDto request,
        out ChatResponseDto response)
    {
        response = default!;
        if (!store.TryGet(conversationId, out var state))
        {
            return false;
        }

        var activeBranch = store.GetActiveBranchId(state);
        ChatMessageDto? assistant;
        lock (state.SyncRoot)
        {
            if (state.ActiveTurnCheckpoint is { } checkpoint &&
                (string.IsNullOrWhiteSpace(request.TurnId) || string.Equals(checkpoint.TurnId, request.TurnId, StringComparison.OrdinalIgnoreCase)) &&
                !string.IsNullOrWhiteSpace(checkpoint.PartialResponse))
            {
                response = new ChatResponseDto(
                    state.Id,
                    checkpoint.PartialResponse,
                    store.GetRecentMessages(state, checkpoint.BranchId, request.MaxHistory ?? 20),
                    checkpoint.UpdatedAtUtc,
                    Confidence: 0.5,
                    Sources: checkpoint.Sources,
                    TurnId: checkpoint.TurnId,
                    ToolCalls: checkpoint.ToolCalls,
                    RequiresConfirmation: checkpoint.RequiresConfirmation,
                    NextStep: "Resume the turn to continue from the persisted checkpoint.",
                    GroundingStatus: checkpoint.Stage.ToString().ToLowerInvariant(),
                    CitationCoverage: 0,
                    VerifiedClaims: 0,
                    TotalClaims: 0,
                    UncertaintyFlags: checkpoint.UncertaintyFlags,
                    BranchId: checkpoint.BranchId,
                    AvailableBranches: store.GetBranchIds(state));
                return true;
            }

            assistant = state.Messages
                .Where(m =>
                    m.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase) &&
                    (string.IsNullOrWhiteSpace(request.TurnId) || string.Equals(m.TurnId, request.TurnId, StringComparison.OrdinalIgnoreCase)))
                .LastOrDefault();
        }

        if (assistant is null || string.IsNullOrEmpty(assistant.Content))
        {
            return false;
        }

        var branchId = assistant.BranchId ?? activeBranch;
        response = new ChatResponseDto(
            state.Id,
            assistant.Content,
            store.GetRecentMessages(state, branchId, request.MaxHistory ?? 20),
            assistant.Timestamp,
            Confidence: 0.7,
            Sources: assistant.Citations,
            TurnId: assistant.TurnId,
            ToolCalls: assistant.ToolCalls,
            RequiresConfirmation: false,
            NextStep: null,
            GroundingStatus: null,
            CitationCoverage: 0,
            VerifiedClaims: 0,
            TotalClaims: 0,
            UncertaintyFlags: null,
            BranchId: branchId,
            AvailableBranches: store.GetBranchIds(state));
        return true;
    }

    public static int NormalizeCursorOffset(string fullResponse, int cursorOffset)
    {
        return Math.Clamp(cursorOffset, 0, fullResponse.Length);
    }

    public static IEnumerable<string> SplitRemainingResponse(string fullResponse, int cursorOffset, int chunkSize = 96)
    {
        if (string.IsNullOrEmpty(fullResponse))
        {
            yield break;
        }

        var safeCursor = NormalizeCursorOffset(fullResponse, cursorOffset);
        if (safeCursor >= fullResponse.Length)
        {
            yield break;
        }

        var remaining = fullResponse.Substring(safeCursor);
        var size = Math.Clamp(chunkSize, 24, 512);
        for (var i = 0; i < remaining.Length; i += size)
        {
            var length = Math.Min(size, remaining.Length - i);
            yield return remaining.Substring(i, length);
        }
    }
}

