using Helper.Api.Conversation;
using Helper.Api.Hosting;

namespace Helper.Api.Backend.Application;

public interface IConversationCommandDispatcher
{
    Task<ChatResponseDto> DispatchAsync(StartTurnCommand command, CancellationToken ct);
    IAsyncEnumerable<TokenChunk> DispatchStreamAsync(StartTurnCommand command, CancellationToken ct);
    Task<ChatResponseDto> DispatchAsync(ResumeTurnCommand command, CancellationToken ct);
    Task<ChatResponseDto> DispatchAsync(RegenerateTurnCommand command, CancellationToken ct);
    Task<ChatResponseDto> DispatchAsync(RepairTurnCommand command, CancellationToken ct);
    Task<(bool Success, string BranchId, string Error)> DispatchAsync(CreateBranchCommand command, CancellationToken ct);
    Task<(bool Success, string Error)> DispatchAsync(ActivateBranchCommand command, CancellationToken ct);
    Task<(bool Success, int MergedMessages, string Error)> DispatchAsync(MergeBranchCommand command, CancellationToken ct);
}

public sealed class ConversationCommandDispatcher : IConversationCommandDispatcher
{
    private readonly ITurnOrchestrationEngine _turnEngine;
    private readonly IConversationBranchService _branchService;
    private readonly IConversationCommandIdempotencyStore _idempotency;

    public ConversationCommandDispatcher(
        ITurnOrchestrationEngine turnEngine,
        IConversationBranchService branchService,
        IConversationCommandIdempotencyStore idempotency)
    {
        _turnEngine = turnEngine;
        _branchService = branchService;
        _idempotency = idempotency;
    }

    public async Task<ChatResponseDto> DispatchAsync(StartTurnCommand command, CancellationToken ct)
    {
        if (_idempotency.TryGetResponse(command, out ChatResponseDto cached))
        {
            return cached;
        }

        var response = await _turnEngine.StartTurnAsync(command.Request, ct);
        _idempotency.StoreResponse(command, response);
        return response;
    }

    public IAsyncEnumerable<TokenChunk> DispatchStreamAsync(StartTurnCommand command, CancellationToken ct)
        => _turnEngine.StartTurnStreamAsync(command.Request, ct);

    public async Task<ChatResponseDto> DispatchAsync(ResumeTurnCommand command, CancellationToken ct)
    {
        if (_idempotency.TryGetResponse(command, out ChatResponseDto cached))
        {
            return cached;
        }

        var response = await _turnEngine.ResumeTurnAsync(command.ConversationId, command.Request, ct);
        _idempotency.StoreResponse(command, response);
        return response;
    }

    public async Task<ChatResponseDto> DispatchAsync(RegenerateTurnCommand command, CancellationToken ct)
    {
        if (_idempotency.TryGetResponse(command, out ChatResponseDto cached))
        {
            return cached;
        }

        var response = await _turnEngine.RegenerateTurnAsync(command.ConversationId, command.TurnId, command.Request, ct);
        _idempotency.StoreResponse(command, response);
        return response;
    }

    public async Task<ChatResponseDto> DispatchAsync(RepairTurnCommand command, CancellationToken ct)
    {
        if (_idempotency.TryGetResponse(command, out ChatResponseDto cached))
        {
            return cached;
        }

        var response = await _turnEngine.RepairConversationAsync(command.ConversationId, command.Request, ct);
        _idempotency.StoreResponse(command, response);
        return response;
    }

    public Task<(bool Success, string BranchId, string Error)> DispatchAsync(CreateBranchCommand command, CancellationToken ct)
        => _branchService.CreateAsync(command, ct);

    public Task<(bool Success, string Error)> DispatchAsync(ActivateBranchCommand command, CancellationToken ct)
        => _branchService.ActivateAsync(command, ct);

    public Task<(bool Success, int MergedMessages, string Error)> DispatchAsync(MergeBranchCommand command, CancellationToken ct)
        => _branchService.MergeAsync(command, ct);
}

