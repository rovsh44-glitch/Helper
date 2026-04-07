namespace Helper.Api.Conversation;

public sealed record UserUnderstandingState(
    string? PreferredInteractionMode,
    string DecisionAssertiveness,
    string ClarificationTolerance,
    string CitationPreference,
    string RepairStyle,
    string ReasoningStyle,
    DateTimeOffset UpdatedAtUtc);

public sealed record ProjectUnderstandingState(
    string ProjectId,
    string LastIntent,
    string CollaborationBias,
    DateTimeOffset UpdatedAtUtc);

public sealed record ProjectContextState(
    string ProjectId,
    string? Label = null,
    string? Instructions = null,
    bool MemoryEnabled = true,
    DateTimeOffset UpdatedAtUtc = default,
    IReadOnlyList<string>? ReferenceArtifacts = null)
{
    public static ProjectContextState Empty(string projectId)
    {
        return new(
            ProjectId: projectId,
            UpdatedAtUtc: DateTimeOffset.UtcNow,
            ReferenceArtifacts: Array.Empty<string>());
    }
}

public sealed record PersonalizationProfile(
    string ExplanationDepth = "balanced",
    string DecisionAssertiveness = "balanced",
    string ClarificationTolerance = "balanced",
    string CitationPreference = "adaptive",
    string RepairStyle = "direct_fix",
    string ReasoningStyle = "concise",
    string ReasoningEffort = "balanced",
    string? PersonaBundleId = null)
{
    public static PersonalizationProfile Default { get; } = new();
}

public interface IPersonalizationMergePolicy
{
    PersonalizationProfile Resolve(ConversationState state, ChatTurnContext context);
}

public sealed class PersonalizationMergePolicy : IPersonalizationMergePolicy
{
    public PersonalizationProfile Resolve(ConversationState state, ChatTurnContext context)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(context);

        return new PersonalizationProfile(
            ExplanationDepth: state.DetailLevel,
            DecisionAssertiveness: state.DecisionAssertiveness,
            ClarificationTolerance: state.ClarificationTolerance,
            CitationPreference: state.CitationPreference,
            RepairStyle: state.RepairStyle,
            ReasoningStyle: state.ReasoningStyle,
            ReasoningEffort: "balanced");
    }
}
