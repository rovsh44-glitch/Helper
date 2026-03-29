namespace Helper.Api.Conversation;

internal interface IComposerLocalizationResolver
{
    ComposerLocalization Resolve(ChatTurnContext context);
}

internal sealed class ComposerLocalizationResolver : IComposerLocalizationResolver
{
    private readonly ITurnLanguageResolver _turnLanguageResolver;

    public ComposerLocalizationResolver(ITurnLanguageResolver turnLanguageResolver)
    {
        _turnLanguageResolver = turnLanguageResolver;
    }

    public ComposerLocalization Resolve(ChatTurnContext context)
    {
        var enabled = ReadFlag("HELPER_FF_OPERATOR_COMPOSER_LANG_V1", true);
        if (!enabled)
        {
            return ComposerLocalization.English;
        }

        var language = context.ResolvedTurnLanguage
            ?? _turnLanguageResolver.Resolve(context.Conversation.PreferredLanguage, context.Request.Message, context.History);
        return string.Equals(language, "ru", StringComparison.OrdinalIgnoreCase)
            ? ComposerLocalization.Russian
            : ComposerLocalization.English;
    }

    private static bool ReadFlag(string envName, bool fallback)
    {
        var raw = Environment.GetEnvironmentVariable(envName);
        return bool.TryParse(raw, out var enabled) ? enabled : fallback;
    }
}

