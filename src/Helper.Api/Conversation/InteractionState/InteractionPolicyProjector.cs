namespace Helper.Api.Conversation.InteractionState;

public sealed class InteractionPolicyProjector : IInteractionPolicyProjector
{
    public InteractionPolicyProjection Project(ChatTurnContext context, InteractionStateSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(snapshot);

        var preferAnswerFirst = snapshot.FrustrationLevel >= InteractionSignalLevel.Moderate ||
                                snapshot.UrgencyLevel >= InteractionSignalLevel.Moderate ||
                                snapshot.ClarificationToleranceShift < 0;
        var softenClarification = snapshot.ReassuranceNeed >= InteractionSignalLevel.Low ||
                                  snapshot.FrustrationLevel >= InteractionSignalLevel.Moderate;
        var compressStructure = snapshot.OverloadRisk >= InteractionSignalLevel.Moderate;
        var useCalmTone = snapshot.ReassuranceNeed >= InteractionSignalLevel.Low ||
                          snapshot.FrustrationLevel >= InteractionSignalLevel.Low;
        var increaseReassurance = snapshot.ReassuranceNeed >= InteractionSignalLevel.Moderate;
        var narrowRepairScope = snapshot.FrustrationLevel >= InteractionSignalLevel.Moderate ||
                                snapshot.OverloadRisk >= InteractionSignalLevel.Moderate;
        var suppressGenericNextStep = snapshot.OverloadRisk >= InteractionSignalLevel.Moderate ||
                                      snapshot.FrustrationLevel >= InteractionSignalLevel.Moderate;

        return new InteractionPolicyProjection(
            preferAnswerFirst,
            softenClarification,
            compressStructure,
            useCalmTone,
            increaseReassurance,
            narrowRepairScope,
            suppressGenericNextStep);
    }
}
