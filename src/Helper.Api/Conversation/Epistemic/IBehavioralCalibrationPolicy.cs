namespace Helper.Api.Conversation.Epistemic;

public interface IBehavioralCalibrationPolicy
{
    EpistemicRiskSnapshot BuildSnapshot(ChatTurnContext context);
}
