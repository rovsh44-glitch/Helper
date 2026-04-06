namespace Helper.Api.Conversation.InteractionState;

public sealed record InteractionPolicyProjection(
    bool PreferAnswerFirst,
    bool SoftenClarification,
    bool CompressStructure,
    bool UseCalmTone,
    bool IncreaseReassurance,
    bool NarrowRepairScope,
    bool SuppressGenericNextStep);
