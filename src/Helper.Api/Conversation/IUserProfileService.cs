namespace Helper.Api.Conversation;

public interface IUserProfileService
{
    ConversationUserProfile Resolve(ConversationState state);
    ConversationUserProfile ApplyPersonalization(ConversationUserProfile profile, PersonalizationProfile personalization);
    void ApplyPreferences(
        ConversationState state,
        Helper.Api.Hosting.ConversationPreferenceDto dto,
        IReadOnlySet<string>? presentFields = null);
    ConversationStyleRoute ResolveStyleRoute(ConversationUserProfile profile, ChatTurnContext? context = null);
    string BuildSystemHint(ConversationUserProfile profile, ChatTurnContext? context = null, string? resolvedLanguage = null);
}
