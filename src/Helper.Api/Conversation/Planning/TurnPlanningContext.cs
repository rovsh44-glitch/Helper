namespace Helper.Api.Conversation;

public sealed class TurnPlanningContext
{
    public TurnPlanningContext(ChatTurnContext turn)
    {
        Turn = turn ?? throw new ArgumentNullException(nameof(turn));
        TrimmedMessage = (turn.Request.Message ?? string.Empty).Trim();
    }

    public ChatTurnContext Turn { get; }

    public string TrimmedMessage { get; }
}
