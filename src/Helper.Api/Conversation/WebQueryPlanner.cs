using Helper.Runtime.WebResearch;

namespace Helper.Api.Conversation;

internal interface IConversationWebQueryPlanner
{
    ConversationWebQueryPlan Build(ChatTurnContext context, ConversationUserProfile profile, string baseQuery);
}

internal sealed record ConversationWebQueryPlan(
    string Query,
    bool LocalityApplied,
    bool PreferenceApplied,
    bool VoiceRewriteApplied,
    bool TopicCoreApplied,
    IReadOnlyList<string> Trace);

internal sealed class WebQueryPlanner : IConversationWebQueryPlanner
{
    public WebQueryPlanner(
        ILocationAwareRewritePolicy? locationAwareRewritePolicy = null,
        IVoiceSearchRewritePolicy? voiceSearchRewritePolicy = null,
        IPreferenceAwareRewritePolicy? preferenceAwareRewritePolicy = null,
        ISearchTopicCoreRewritePolicy? searchTopicCoreRewritePolicy = null)
    {
        _locationAwareRewritePolicy = locationAwareRewritePolicy ?? new LocationAwareRewritePolicy();
        _voiceSearchRewritePolicy = voiceSearchRewritePolicy ?? new VoiceSearchRewritePolicy();
        _preferenceAwareRewritePolicy = preferenceAwareRewritePolicy ?? new PreferenceAwareRewritePolicy();
        _searchTopicCoreRewritePolicy = searchTopicCoreRewritePolicy ?? new SearchTopicCoreRewritePolicy();
    }

    private readonly ILocationAwareRewritePolicy _locationAwareRewritePolicy;
    private readonly IVoiceSearchRewritePolicy _voiceSearchRewritePolicy;
    private readonly IPreferenceAwareRewritePolicy _preferenceAwareRewritePolicy;
    private readonly ISearchTopicCoreRewritePolicy _searchTopicCoreRewritePolicy;

    public ConversationWebQueryPlan Build(ChatTurnContext context, ConversationUserProfile profile, string baseQuery)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(profile);

        var normalizedQuery = NormalizeWhitespace(baseQuery);
        if (normalizedQuery.Length == 0)
        {
            return new ConversationWebQueryPlan(
                string.Empty,
                LocalityApplied: false,
                PreferenceApplied: false,
                VoiceRewriteApplied: false,
                TopicCoreApplied: false,
                new[] { "web_query.plan applied=no reason=empty_query" });
        }

        var trace = new List<string>();
        var language = ResolveLanguage(context, profile, normalizedQuery);
        var voiceDecision = ApplyVoiceRewrite(context, normalizedQuery, language);
        trace.AddRange(voiceDecision.Trace);

        var localityDecision = _locationAwareRewritePolicy.Rewrite(voiceDecision.Query, profile, language);
        trace.AddRange(localityDecision.Trace);

        var query = localityDecision.Query;
        var preferenceDecision = _preferenceAwareRewritePolicy.Rewrite(query, profile, language);
        trace.AddRange(preferenceDecision.Trace);
        query = preferenceDecision.Query;
        var topicCoreDecision = ShouldApplyTopicCoreRewrite(context, baseQuery)
            ? _searchTopicCoreRewritePolicy.Rewrite(query)
            : new SearchTopicCoreRewriteDecision(
                query,
                Applied: false,
                new[] { "search_query.rewrite stage=topic_core applied=no reason=continuation_query_composed_upstream" });
        trace.AddRange(topicCoreDecision.Trace);
        query = topicCoreDecision.Query;
        trace.Add($"web_query.final={Summarize(query)}");

        return new ConversationWebQueryPlan(
            query,
            localityDecision.Applied,
            preferenceDecision.Applied,
            voiceDecision.Applied,
            topicCoreDecision.Applied,
            trace);
    }

    private VoiceSearchRewriteDecision ApplyVoiceRewrite(ChatTurnContext context, string query, string language)
    {
        if (!ConversationInputMode.IsVoice(context.Request.InputMode))
        {
            return new VoiceSearchRewriteDecision(
                query,
                false,
                new[] { "web_query.voice applied=no reason=input_mode_text" });
        }

        return _voiceSearchRewritePolicy.Rewrite(query, language);
    }

    private static string ResolveLanguage(ChatTurnContext context, ConversationUserProfile profile, string query)
    {
        var language = context.ResolvedTurnLanguage;
        if (string.Equals(language, "ru", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(language, "en", StringComparison.OrdinalIgnoreCase))
        {
            return language!;
        }

        if (string.Equals(profile.Language, "ru", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(profile.Language, "en", StringComparison.OrdinalIgnoreCase))
        {
            return profile.Language;
        }

        return query.Any(ch => ch is >= '\u0400' and <= '\u04FF') ? "ru" : "en";
    }

    private static bool ShouldApplyTopicCoreRewrite(ChatTurnContext context, string baseQuery)
    {
        return string.Equals(
            NormalizeWhitespace(baseQuery),
            NormalizeWhitespace(context.Request.Message),
            StringComparison.Ordinal);
    }

    private static string NormalizeWhitespace(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return string.Join(
            ' ',
            value
                .Trim()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    private static string Summarize(string value)
    {
        return value.Length <= 180 ? value : value[..180];
    }
}

