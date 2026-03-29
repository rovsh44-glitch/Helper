using System.Text.RegularExpressions;

namespace Helper.Api.Conversation;

internal interface ILocationAwareRewritePolicy
{
    LocationAwareRewriteDecision Rewrite(string query, ConversationUserProfile profile, string? language = null);
}

internal sealed record LocationAwareRewriteDecision(
    string Query,
    bool Applied,
    string Reason,
    string LocalitySource,
    bool PrivacyBoundaryExplicit,
    IReadOnlyList<string> Trace);

internal sealed partial class LocationAwareRewritePolicy : ILocationAwareRewritePolicy
{
    private static readonly string[] LocalRecommendationTokens =
    {
        "restaurant", "restaurants", "hotel", "hotels", "cafe", "coffee", "bar", "bars", "pharmacy", "clinic", "dentist",
        "ресторан", "рестораны", "отель", "отели", "кафе", "кофейня", "бар", "бары", "аптека", "клиника", "стоматолог"
    };

    private static readonly string[] LocalityDependentTokens =
    {
        "near me", "nearby", "around me", "open now", "open today",
        "рядом", "поблизости", "недалеко", "открыто сейчас", "сейчас открыто"
    };

    public LocationAwareRewriteDecision Rewrite(string query, ConversationUserProfile profile, string? language = null)
    {
        var normalizedQuery = NormalizeWhitespace(query);
        if (normalizedQuery.Length == 0)
        {
            return new LocationAwareRewriteDecision(
                normalizedQuery,
                false,
                "empty_query",
                "none",
                PrivacyBoundaryExplicit: true,
                new[] { "web_query.locality applied=no reason=empty_query" });
        }

        if (HasExplicitLocality(normalizedQuery))
        {
            return new LocationAwareRewriteDecision(
                normalizedQuery,
                false,
                "query_has_explicit_locality",
                "query",
                PrivacyBoundaryExplicit: true,
                new[] { "web_query.locality applied=no reason=query_has_explicit_locality source=query" });
        }

        if (!NeedsLocalityRewrite(normalizedQuery))
        {
            return new LocationAwareRewriteDecision(
                normalizedQuery,
                false,
                "not_locality_sensitive",
                "none",
                PrivacyBoundaryExplicit: true,
                new[] { "web_query.locality applied=no reason=not_locality_sensitive" });
        }

        if (string.IsNullOrWhiteSpace(profile.SearchLocalityHint))
        {
            return new LocationAwareRewriteDecision(
                normalizedQuery,
                false,
                "no_explicit_locality_hint",
                "none",
                PrivacyBoundaryExplicit: true,
                new[] { "web_query.locality applied=no reason=no_explicit_locality_hint privacy_boundary=explicit" });
        }

        var resolvedLanguage = ResolveLanguage(language, normalizedQuery);
        var localityHint = profile.SearchLocalityHint!.Trim();
        var rewrittenQuery = InjectLocalityHint(normalizedQuery, localityHint, resolvedLanguage);
        return new LocationAwareRewriteDecision(
            rewrittenQuery,
            !string.Equals(rewrittenQuery, normalizedQuery, StringComparison.Ordinal),
            "profile_locality_hint",
            "profile_hint",
            PrivacyBoundaryExplicit: true,
            new[]
            {
                $"web_query.locality applied=yes reason=profile_locality_hint source=profile_hint locality={localityHint}",
                "web_query.locality privacy_boundary=explicit_hint"
            });
    }

    private static bool NeedsLocalityRewrite(string query)
    {
        return ContainsAny(query, LocalityDependentTokens) || ContainsAny(query, LocalRecommendationTokens);
    }

    private static bool HasExplicitLocality(string query)
    {
        return EnglishLocalityRegex().IsMatch(query) ||
               RussianLocalityRegex().IsMatch(query);
    }

    private static string InjectLocalityHint(string query, string localityHint, string language)
    {
        if (ContainsAny(query, LocalityDependentTokens))
        {
            var replaced = NearMeRegex().Replace(query, $"in {localityHint}");
            replaced = RussianNearMeRegex().Replace(replaced, $"в {localityHint}");
            return NormalizeWhitespace(replaced);
        }

        return NormalizeWhitespace(
            string.Equals(language, "ru", StringComparison.OrdinalIgnoreCase)
                ? $"{query} в {localityHint}"
                : $"{query} in {localityHint}");
    }

    private static string ResolveLanguage(string? language, string query)
    {
        if (string.Equals(language, "ru", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(language, "en", StringComparison.OrdinalIgnoreCase))
        {
            return language!;
        }

        return query.Any(ch => ch is >= '\u0400' and <= '\u04FF') ? "ru" : "en";
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

        return SpaceRegex().Replace(value.Trim(), " ");
    }

    [GeneratedRegex(@"\b(near me|nearby|around me)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex NearMeRegex();

    [GeneratedRegex(@"\b(рядом|поблизости|недалеко)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex RussianNearMeRegex();

    [GeneratedRegex(@"\b(in|within)\s+[\p{L}\p{Nd}][\p{L}\p{Nd}\-]*(?:\s+[\p{L}\p{Nd}][\p{L}\p{Nd}\-]*){0,4}\b|\b(near|around)\s+(?!me\b|here\b)[\p{L}\p{Nd}][\p{L}\p{Nd}\-]*(?:\s+[\p{L}\p{Nd}][\p{L}\p{Nd}\-]*){0,4}\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex EnglishLocalityRegex();

    [GeneratedRegex(@"\b(в|во|около|рядом с|недалеко от)\s+[\p{L}\p{Nd}][\p{L}\p{Nd}\-]*(?:\s+[\p{L}\p{Nd}][\p{L}\p{Nd}\-]*){0,4}\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex RussianLocalityRegex();

    [GeneratedRegex(@"\s+", RegexOptions.Compiled)]
    private static partial Regex SpaceRegex();
}

