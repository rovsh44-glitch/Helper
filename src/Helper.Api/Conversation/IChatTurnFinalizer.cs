namespace Helper.Api.Conversation;

public interface IChatTurnFinalizer
{
    Task FinalizeAsync(ChatTurnContext context, CancellationToken ct);
}

