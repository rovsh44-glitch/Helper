using Helper.Api.Conversation.InteractionState;

namespace Helper.Api.Conversation;

public enum ClarificationBoundary
{
    None,
    Goal,
    Scope,
    Format,
    Constraints,
    Data,
    Safety
}

public sealed record ClarificationQualityDecision(
    ClarificationBoundary Boundary,
    bool ShouldBlockForClarification,
    string Question,
    string Reason);

public interface IClarificationQualityPolicy
{
    ClarificationQualityDecision BuildDecision(
        AmbiguityDecision ambiguity,
        CollaborationIntentAnalysis collaborationIntent,
        IntentClassification intent,
        int attemptNumber,
        string? resolvedLanguage,
        InteractionPolicyProjection? interactionPolicy = null);
}
