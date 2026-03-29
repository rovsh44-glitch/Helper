using Helper.Api.Conversation;
using Helper.Api.Hosting;
using Helper.Runtime.Core;

namespace Helper.Api.Backend.Application;

public sealed record TurnExecutionCheckpoint(
    string TurnId,
    string BranchId,
    string UserMessage,
    TurnExecutionState Stage,
    string PartialResponse,
    int CursorOffset,
    IntentAnalysis Intent,
    double IntentConfidence,
    bool RequiresClarification,
    bool RequiresConfirmation,
    string? ClarifyingQuestion,
    bool IsFactualPrompt,
    TurnExecutionMode ExecutionMode,
    TurnBudgetProfile BudgetProfile,
    bool BudgetExceeded,
    int EstimatedTokensGenerated,
    IReadOnlyList<string> Sources,
    IReadOnlyList<string> ToolCalls,
    IReadOnlyList<string> UncertaintyFlags,
    DateTimeOffset UpdatedAtUtc);

public interface ITurnCheckpointManager
{
    void SavePlannedCheckpoint(ConversationState state, ChatTurnContext context, string branchId);
    void SaveExecutionProgress(ConversationState state, ChatTurnContext context, string branchId, string partialResponse, int cursorOffset);
    bool TryHydrate(ConversationState state, ChatRequestDto request, out ChatTurnContext context);
    void Clear(ConversationState state);
}

public sealed class TurnCheckpointManager : ITurnCheckpointManager
{
    public void SavePlannedCheckpoint(ConversationState state, ChatTurnContext context, string branchId)
    {
        SaveExecutionProgress(state, context, branchId, context.ExecutionOutput, context.EstimatedTokensGenerated);
    }

    public void SaveExecutionProgress(ConversationState state, ChatTurnContext context, string branchId, string partialResponse, int cursorOffset)
    {
        lock (state.SyncRoot)
        {
            state.ActiveTurnId = context.TurnId;
            state.ActiveTurnUserMessage = context.Request.Message.Trim();
            state.ActiveTurnStartedAt = context.StartedAt;
            state.ActiveTurnCheckpoint = new TurnExecutionCheckpoint(
                TurnId: context.TurnId,
                BranchId: branchId,
                UserMessage: context.Request.Message.Trim(),
                Stage: context.ExecutionState,
                PartialResponse: partialResponse ?? string.Empty,
                CursorOffset: Math.Max(0, cursorOffset),
                Intent: context.Intent,
                IntentConfidence: context.IntentConfidence,
                RequiresClarification: context.RequiresClarification,
                RequiresConfirmation: context.RequiresConfirmation,
                ClarifyingQuestion: context.ClarifyingQuestion,
                IsFactualPrompt: context.IsFactualPrompt,
                ExecutionMode: context.ExecutionMode,
                BudgetProfile: context.BudgetProfile,
                BudgetExceeded: context.BudgetExceeded,
                EstimatedTokensGenerated: context.EstimatedTokensGenerated,
                Sources: context.Sources.ToArray(),
                ToolCalls: context.ToolCalls.ToArray(),
                UncertaintyFlags: context.UncertaintyFlags.ToArray(),
                UpdatedAtUtc: DateTimeOffset.UtcNow);
        }
    }

    public bool TryHydrate(ConversationState state, ChatRequestDto request, out ChatTurnContext context)
    {
        TurnExecutionCheckpoint? checkpoint;
        lock (state.SyncRoot)
        {
            checkpoint = state.ActiveTurnCheckpoint;
        }

        if (checkpoint is null)
        {
            context = default!;
            return false;
        }

        context = new ChatTurnContext
        {
            TurnId = checkpoint.TurnId,
            Request = request with
            {
                ConversationId = state.Id,
                BranchId = checkpoint.BranchId
            },
            Conversation = state,
            History = Array.Empty<ChatMessageDto>(),
            StartedAt = state.ActiveTurnStartedAt ?? checkpoint.UpdatedAtUtc,
            ExecutionState = checkpoint.Stage,
            ExecutionOutput = checkpoint.PartialResponse,
            FinalResponse = checkpoint.PartialResponse,
            Intent = checkpoint.Intent,
            IntentConfidence = checkpoint.IntentConfidence,
            RequiresClarification = checkpoint.RequiresClarification,
            RequiresConfirmation = checkpoint.RequiresConfirmation,
            ClarifyingQuestion = checkpoint.ClarifyingQuestion,
            IsFactualPrompt = checkpoint.IsFactualPrompt,
            ExecutionMode = checkpoint.ExecutionMode,
            BudgetProfile = checkpoint.BudgetProfile,
            BudgetExceeded = checkpoint.BudgetExceeded,
            EstimatedTokensGenerated = checkpoint.EstimatedTokensGenerated
        };
        context.ExecutionTrace.Clear();
        context.ExecutionTrace.Add(checkpoint.Stage);
        context.Sources.AddRange(checkpoint.Sources);
        context.ToolCalls.AddRange(checkpoint.ToolCalls);
        context.UncertaintyFlags.AddRange(checkpoint.UncertaintyFlags);
        return true;
    }

    public void Clear(ConversationState state)
    {
        lock (state.SyncRoot)
        {
            state.ActiveTurnId = null;
            state.ActiveTurnUserMessage = null;
            state.ActiveTurnStartedAt = null;
            state.ActiveTurnCheckpoint = null;
        }
    }
}

