namespace Helper.Api.Conversation;

internal static class ResponseCompositionRecoveryModePolicy
{
    public static ResponseCompositionMode Promote(ResponseCompositionMode mode, ChatTurnContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return mode;
    }
}
