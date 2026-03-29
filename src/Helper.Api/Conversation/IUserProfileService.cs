namespace Helper.Api.Conversation;

public interface IUserProfileService
{
    ConversationUserProfile Resolve(ConversationState state);
    void ApplyPreferences(ConversationState state, Helper.Api.Hosting.ConversationPreferenceDto dto);
    ConversationStyleRoute ResolveStyleRoute(ConversationUserProfile profile, ChatTurnContext? context = null);
    string BuildSystemHint(ConversationUserProfile profile, ChatTurnContext? context = null, string? resolvedLanguage = null);
}

