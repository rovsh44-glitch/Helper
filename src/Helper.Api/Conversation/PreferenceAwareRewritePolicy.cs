namespace Helper.Api.Conversation;

internal interface IPreferenceAwareRewritePolicy
{
    PreferenceRewriteDecision Rewrite(string query, ConversationUserProfile profile, string language);
}

internal sealed record PreferenceRewriteDecision(
    string Query,
    bool Applied,
    IReadOnlyList<string> Trace);

internal sealed class PreferenceAwareRewritePolicy : IPreferenceAwareRewritePolicy
{
    private static readonly string[] TechnicalTokens =
    {
        "api", "sdk", "docs", "documentation", "guide", "reference", "migration", "release notes", "spec", "specification",
        "grpc", "rest", "dotnet", ".net", "kubernetes", "observability", "retry", "architecture",
        "документац", "гайд", "руководство", "справочник", "миграц", "релиз", "спецификац", "архитектур", "retry", "grpc", "rest"
    };

    public PreferenceRewriteDecision Rewrite(string query, ConversationUserProfile profile, string language)
    {
        if (!LooksTechnicalKnowledgeQuery(query))
        {
            return new PreferenceRewriteDecision(
                query,
                false,
                new[] { "web_query.preference applied=no reason=not_technical_knowledge_query" });
        }

        var suffixes = new List<string>(capacity: 2);
        if (string.Equals(profile.DomainFamiliarity, "novice", StringComparison.OrdinalIgnoreCase) &&
            !ContainsAny(query, new[] { "beginner", "for beginners", "для начинающих" }))
        {
            suffixes.Add(string.Equals(language, "ru", StringComparison.OrdinalIgnoreCase)
                ? "для начинающих"
                : "beginner guide");
        }
        else if (string.Equals(profile.DomainFamiliarity, "expert", StringComparison.OrdinalIgnoreCase) &&
                 !ContainsAny(query, new[] { "official docs", "official documentation", "официальная документация" }))
        {
            suffixes.Add(string.Equals(language, "ru", StringComparison.OrdinalIgnoreCase)
                ? "официальная документация"
                : "official docs");
        }

        if (string.Equals(profile.DetailLevel, "deep", StringComparison.OrdinalIgnoreCase) &&
            !ContainsAny(query, new[] { "deep dive", "detailed", "подробно", "глубоко" }))
        {
            suffixes.Add(string.Equals(language, "ru", StringComparison.OrdinalIgnoreCase)
                ? "подробно"
                : "deep dive");
        }
        else if (string.Equals(profile.DetailLevel, "concise", StringComparison.OrdinalIgnoreCase) &&
                 !ContainsAny(query, new[] { "summary", "brief", "кратко", "коротко" }))
        {
            suffixes.Add(string.Equals(language, "ru", StringComparison.OrdinalIgnoreCase)
                ? "кратко"
                : "summary");
        }

        if (suffixes.Count == 0)
        {
            return new PreferenceRewriteDecision(
                query,
                false,
                new[] { "web_query.preference applied=no reason=no_profile_modifier" });
        }

        var rewritten = NormalizeWhitespace($"{query} {string.Join(" ", suffixes.Take(2))}");
        return new PreferenceRewriteDecision(
            rewritten,
            !string.Equals(rewritten, query, StringComparison.Ordinal),
            new[]
            {
                $"web_query.preference applied=yes modifiers={string.Join(",", suffixes.Take(2))}"
            });
    }

    private static bool LooksTechnicalKnowledgeQuery(string query)
    {
        return ContainsAny(query, TechnicalTokens);
    }

    private static bool ContainsAny(string text, IEnumerable<string> tokens)
    {
        foreach (var token in tokens)
        {
            if (text.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
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
}

