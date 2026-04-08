namespace Helper.Api.Conversation;

public interface IPersonalizationMergePolicy
{
    PersonalizationProfile Resolve(ConversationState state, ChatTurnContext? context = null);
}

public sealed class PersonalizationMergePolicy : IPersonalizationMergePolicy
{
    public PersonalizationProfile Resolve(ConversationState state, ChatTurnContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(state);

        lock (state.SyncRoot)
        {
            var profile = state.PersonalizationProfile ?? PersonalizationProfile.Default;

            if (context?.CollaborationIntent.PrefersAnswerOverClarification == true &&
                string.Equals(profile.ClarificationTolerance, "balanced", StringComparison.OrdinalIgnoreCase))
            {
                profile = profile with { ClarificationTolerance = "low" };
            }

            if (context?.IsFactualPrompt == true &&
                string.Equals(profile.CitationPreference, "adaptive", StringComparison.OrdinalIgnoreCase))
            {
                profile = profile with { CitationPreference = "prefer" };
            }

            return profile;
        }
    }
}
