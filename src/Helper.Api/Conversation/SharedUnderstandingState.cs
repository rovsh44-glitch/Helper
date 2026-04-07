namespace Helper.Api.Conversation;

public sealed record SharedUnderstandingState(
    string? PreferredInteractionMode,
    bool PrefersDecisiveAction,
    bool AcceptedAssumptionsRecently,
    bool ClarificationWasUnhelpfulRecently,
    bool TemplateResistanceObserved,
    bool PrefersConciseReassurance,
    bool OverloadObservedRecently,
    bool FrustrationObservedRecently,
    DateTimeOffset UpdatedAtUtc);
