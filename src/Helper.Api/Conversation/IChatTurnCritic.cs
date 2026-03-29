namespace Helper.Api.Conversation;

public interface IChatTurnCritic
{
    Task CritiqueAsync(ChatTurnContext context, CancellationToken ct);
}

