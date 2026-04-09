namespace Helper.Api.Conversation;

public sealed record ConversationUserProfile(
    string Language,
    string DetailLevel,
    string Formality,
    string DomainFamiliarity,
    string PreferredStructure,
    string Warmth = "balanced",
    string Enthusiasm = "balanced",
    string Directness = "balanced",
    string DefaultAnswerShape = "auto",
    string? SearchLocalityHint = null,
    string DecisionAssertiveness = "balanced",
    string ClarificationTolerance = "balanced",
    string CitationPreference = "adaptive",
    string RepairStyle = "direct_fix",
    string ReasoningStyle = "concise",
    string ReasoningEffort = "balanced");

