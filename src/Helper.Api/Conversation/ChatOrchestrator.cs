using Helper.Api.Backend.Application;
using Helper.Api.Hosting;

namespace Helper.Api.Conversation;

public interface IChatOrchestrator
{
    Task<ChatResponseDto> CompleteTurnAsync(ChatRequestDto request, CancellationToken ct);
    IAsyncEnumerable<TokenChunk> CompleteTurnStreamAsync(ChatRequestDto request, CancellationToken ct);
    Task<ChatResponseDto> ResumeActiveTurnAsync(string conversationId, ChatResumeRequestDto request, CancellationToken ct);
    Task<ChatResponseDto> RegenerateTurnAsync(string conversationId, string turnId, TurnRegenerateRequestDto request, CancellationToken ct);
    Task<(bool Success, string BranchId, string Error)> CreateBranchAsync(string conversationId, string fromTurnId, string? branchId, CancellationToken ct);
    Task<(bool Success, string Error)> ActivateBranchAsync(string conversationId, string branchId, CancellationToken ct);
    Task<(bool Success, int MergedMessages, string Error)> MergeBranchAsync(string conversationId, string sourceBranchId, string targetBranchId, CancellationToken ct);
    Task<ChatResponseDto> RepairConversationAsync(string conversationId, ConversationRepairRequestDto request, CancellationToken ct);
}

public sealed class ChatOrchestrator : IChatOrchestrator
{
    private readonly IConversationCommandDispatcher _dispatcher;

    public ChatOrchestrator(IConversationCommandDispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public Task<ChatResponseDto> CompleteTurnAsync(ChatRequestDto request, CancellationToken ct)
        => _dispatcher.DispatchAsync(new StartTurnCommand(request), ct);

    public IAsyncEnumerable<TokenChunk> CompleteTurnStreamAsync(ChatRequestDto request, CancellationToken ct)
        => _dispatcher.DispatchStreamAsync(new StartTurnCommand(request), ct);

    public Task<ChatResponseDto> ResumeActiveTurnAsync(string conversationId, ChatResumeRequestDto request, CancellationToken ct)
        => _dispatcher.DispatchAsync(new ResumeTurnCommand(conversationId, request), ct);

    public Task<ChatResponseDto> RegenerateTurnAsync(string conversationId, string turnId, TurnRegenerateRequestDto request, CancellationToken ct)
        => _dispatcher.DispatchAsync(new RegenerateTurnCommand(conversationId, turnId, request), ct);

    public Task<(bool Success, string BranchId, string Error)> CreateBranchAsync(string conversationId, string fromTurnId, string? branchId, CancellationToken ct)
        => _dispatcher.DispatchAsync(new CreateBranchCommand(conversationId, new BranchCreateRequestDto(fromTurnId, branchId)), ct);

    public Task<(bool Success, string Error)> ActivateBranchAsync(string conversationId, string branchId, CancellationToken ct)
        => _dispatcher.DispatchAsync(new ActivateBranchCommand(conversationId, branchId), ct);

    public Task<(bool Success, int MergedMessages, string Error)> MergeBranchAsync(string conversationId, string sourceBranchId, string targetBranchId, CancellationToken ct)
        => _dispatcher.DispatchAsync(new MergeBranchCommand(conversationId, new BranchMergeRequestDto(sourceBranchId, targetBranchId)), ct);

    public Task<ChatResponseDto> RepairConversationAsync(string conversationId, ConversationRepairRequestDto request, CancellationToken ct)
        => _dispatcher.DispatchAsync(new RepairTurnCommand(conversationId, request), ct);
}

