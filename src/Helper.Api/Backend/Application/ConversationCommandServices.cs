using System.Collections.Concurrent;
using Helper.Api.Conversation;
using Helper.Api.Hosting;

namespace Helper.Api.Backend.Application;

public interface IConversationCommandIdempotencyStore
{
    bool TryGetResponse<T>(ConversationCommand command, out T response);
    void StoreResponse<T>(ConversationCommand command, T response);
}

public sealed class ConversationCommandIdempotencyStore : IConversationCommandIdempotencyStore
{
    private readonly ConcurrentDictionary<string, object> _responses = new(StringComparer.OrdinalIgnoreCase);

    public bool TryGetResponse<T>(ConversationCommand command, out T response)
    {
        response = default!;
        var key = BuildKey(command);
        if (key is null || !_responses.TryGetValue(key, out var value) || value is not T typed)
        {
            return false;
        }

        response = typed;
        return true;
    }

    public void StoreResponse<T>(ConversationCommand command, T response)
    {
        var key = BuildKey(command);
        if (key is null)
        {
            return;
        }

        _responses[key] = response!;
    }

    private static string? BuildKey(ConversationCommand command)
    {
        return string.IsNullOrWhiteSpace(command.IdempotencyKey)
            ? null
            : $"{command.GetType().Name}:{command.ConversationId}:{command.IdempotencyKey.Trim()}";
    }
}

public interface IConversationBranchService
{
    Task<(bool Success, string BranchId, string Error)> CreateAsync(CreateBranchCommand command, CancellationToken ct);
    Task<(bool Success, string Error)> ActivateAsync(ActivateBranchCommand command, CancellationToken ct);
    Task<(bool Success, int MergedMessages, string Error)> MergeAsync(MergeBranchCommand command, CancellationToken ct);
}

public sealed class ConversationBranchService : IConversationBranchService
{
    private readonly IConversationStore _store;

    public ConversationBranchService(IConversationStore store)
    {
        _store = store;
    }

    public Task<(bool Success, string BranchId, string Error)> CreateAsync(CreateBranchCommand command, CancellationToken ct)
    {
        if (!_store.TryGet(command.ConversationId, out var state))
        {
            return Task.FromResult((false, string.Empty, "Conversation not found."));
        }

        var success = _store.CreateBranch(state, command.Request.FromTurnId, command.Request.BranchId, out var branchId);
        return Task.FromResult(success
            ? (true, branchId, string.Empty)
            : (false, string.Empty, "Failed to create branch. Turn not found or branch id already exists."));
    }

    public Task<(bool Success, string Error)> ActivateAsync(ActivateBranchCommand command, CancellationToken ct)
    {
        if (!_store.TryGet(command.ConversationId, out var state))
        {
            return Task.FromResult((false, "Conversation not found."));
        }

        try
        {
            _store.SetActiveBranch(state, command.BranchId);
            return Task.FromResult((true, string.Empty));
        }
        catch (InvalidOperationException ex)
        {
            return Task.FromResult((false, ex.Message));
        }
    }

    public Task<(bool Success, int MergedMessages, string Error)> MergeAsync(MergeBranchCommand command, CancellationToken ct)
    {
        if (!_store.TryGet(command.ConversationId, out var state))
        {
            return Task.FromResult((false, 0, "Conversation not found."));
        }

        var success = _store.MergeBranch(
            state,
            command.Request.SourceBranchId,
            command.Request.TargetBranchId,
            out var mergedMessages,
            out var error);
        return Task.FromResult((success, mergedMessages, success ? string.Empty : error ?? "Merge failed."));
    }
}

