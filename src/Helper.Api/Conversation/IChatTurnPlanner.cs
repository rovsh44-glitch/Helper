namespace Helper.Api.Conversation;

public interface IChatTurnPlanner
{
    Task PlanAsync(ChatTurnContext context, CancellationToken ct);
}

