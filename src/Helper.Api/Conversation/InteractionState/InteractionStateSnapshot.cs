namespace Helper.Api.Conversation.InteractionState;

public sealed record InteractionStateSnapshot(
    InteractionSignalLevel FrustrationLevel,
    InteractionSignalLevel UrgencyLevel,
    InteractionSignalLevel OverloadRisk,
    InteractionSignalLevel ReassuranceNeed,
    int ClarificationToleranceShift,
    InteractionSignalLevel AssistantPressureRisk,
    IReadOnlyList<string> Signals);
