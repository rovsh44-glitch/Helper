using Helper.Runtime.Core;

namespace Helper.Api.Conversation;

public interface IProactiveTopicPolicy
{
    bool ShouldRegister(ChatTurnContext context);
}

public sealed class ProactiveTopicPolicy : IProactiveTopicPolicy
{
    public bool ShouldRegister(ChatTurnContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.Intent.Intent == IntentType.Research &&
               context.CollaborationIntent.TrustsBestJudgment &&
               !context.RequiresClarification &&
               !context.Request.Message.Contains("one-off", StringComparison.OrdinalIgnoreCase);
    }
}
