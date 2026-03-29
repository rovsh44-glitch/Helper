namespace Helper.Api.Conversation;

public interface IChatTurnExecutor
{
    Task ExecuteAsync(ChatTurnContext context, CancellationToken ct);
    IAsyncEnumerable<TokenChunk> ExecuteStreamAsync(ChatTurnContext context, CancellationToken ct);
}

