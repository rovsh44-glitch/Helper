namespace Helper.Api.Conversation.Epistemic;

public interface IEpistemicAnswerModePolicy
{
    EpistemicAnswerMode Resolve(ChatTurnContext context, EpistemicRiskSnapshot snapshot);
}
