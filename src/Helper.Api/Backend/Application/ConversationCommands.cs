using Helper.Api.Hosting;

namespace Helper.Api.Backend.Application;

public abstract record ConversationCommand(
    string ConversationId,
    string? IdempotencyKey);

public sealed record StartTurnCommand(ChatRequestDto Request)
    : ConversationCommand(Request.ConversationId ?? string.Empty, Request.IdempotencyKey);

public sealed record ResumeTurnCommand(string ConversationId, ChatResumeRequestDto Request)
    : ConversationCommand(ConversationId, Request.IdempotencyKey);

public sealed record RegenerateTurnCommand(string ConversationId, string TurnId, TurnRegenerateRequestDto Request)
    : ConversationCommand(ConversationId, Request.IdempotencyKey);

public sealed record RepairTurnCommand(string ConversationId, ConversationRepairRequestDto Request)
    : ConversationCommand(ConversationId, Request.IdempotencyKey);

public sealed record CreateBranchCommand(string ConversationId, BranchCreateRequestDto Request)
    : ConversationCommand(ConversationId, Request.IdempotencyKey);

public sealed record MergeBranchCommand(string ConversationId, BranchMergeRequestDto Request)
    : ConversationCommand(ConversationId, Request.IdempotencyKey);

public sealed record ActivateBranchCommand(string ConversationId, string BranchId, string? IdempotencyKey = null)
    : ConversationCommand(ConversationId, IdempotencyKey);

