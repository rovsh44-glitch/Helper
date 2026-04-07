using System.Text.RegularExpressions;

namespace Helper.Runtime.WebResearch.Providers;

internal interface ILocalSearchTimeoutCompactionPolicy
{
    LocalSearchTimeoutCompactionDecision Compact(WebSearchPlan plan);
}

internal sealed record LocalSearchTimeoutCompactionDecision(
    string Query,
    bool Applied,
    IReadOnlyList<string> Trace);

internal sealed partial class LocalSearchTimeoutCompactionPolicy : ILocalSearchTimeoutCompactionPolicy
{
    private static readonly string[] GenericStopTokens =
    {
        "please", "helper", "tell", "give", "provide", "your", "my", "simple", "simply", "latest", "current",
        "пожалуйста", "хелпер", "мой", "моя", "мое", "моё", "проверь", "объясни", "оцени", "разбери", "расскажи", "покажи",
        "свежим", "свежие", "свежих", "последние", "последних", "актуальные", "актуальным", "утром", "вечером"
    };

    public LocalSearchTimeoutCompactionDecision Compact(WebSearchPlan plan)
    {
        var normalized = SearchQueryIntentProfileClassifier.NormalizeWhitespace(plan.Query);
        if (normalized.Length == 0)
        {
            return new LocalSearchTimeoutCompactionDecision(
                string.Empty,
                Applied: false,
                new[] { "timeout_compaction applied=no reason=empty_query" });
        }

        var language = SearchQueryIntentProfileClassifier.LooksRussian(normalized) ? "ru" : "en";
        var specialized = ResolveSpecializedCompaction(normalized, language, plan.QueryKind);
        var compacted = specialized ?? ResolveGenericCompaction(normalized, language, plan.QueryKind);

        return new LocalSearchTimeoutCompactionDecision(
            compacted,
            Applied: !string.IsNullOrWhiteSpace(compacted) &&
                     !string.Equals(normalized, compacted, StringComparison.OrdinalIgnoreCase),
            new[]
            {
                $"timeout_retry_compaction query_kind={plan.QueryKind} before=\"{Summarize(normalized)}\" after=\"{Summarize(compacted)}\""
            });
    }

    private static string? ResolveSpecializedCompaction(string query, string language, string queryKind)
    {
        if (LooksLikeMigraineGuidelineQuery(query))
        {
            return queryKind switch
            {
                "freshness" => string.Equals(language, "ru", StringComparison.OrdinalIgnoreCase)
                    ? "мигрень профилактика клинические рекомендации обновления"
                    : "migraine prevention clinical guideline update",
                "evidence" => string.Equals(language, "ru", StringComparison.OrdinalIgnoreCase)
                    ? "мигрень профилактика клинические рекомендации systematic review"
                    : "migraine prevention clinical guideline systematic review",
                _ => string.Equals(language, "ru", StringComparison.OrdinalIgnoreCase)
                    ? "мигрень профилактика клинические рекомендации"
                    : "migraine prevention clinical guideline"
            };
        }

        if (LooksLikePrediabetesNutritionQuery(query))
        {
            return queryKind switch
            {
                "freshness" => string.Equals(language, "ru", StringComparison.OrdinalIgnoreCase)
                    ? "преддиабет питание рацион свежие официальные рекомендации"
                    : "prediabetes diet nutrition latest official guidance",
                "evidence" => string.Equals(language, "ru", StringComparison.OrdinalIgnoreCase)
                    ? "преддиабет питание рацион клинические рекомендации systematic review"
                    : "prediabetes diet nutrition clinical guideline systematic review",
                "official" => string.Equals(language, "ru", StringComparison.OrdinalIgnoreCase)
                    ? "преддиабет питание рацион официальные рекомендации"
                    : "prediabetes diet nutrition official guidance",
                _ => string.Equals(language, "ru", StringComparison.OrdinalIgnoreCase)
                    ? "преддиабет питание рацион рекомендации"
                    : "prediabetes diet nutrition recommendations"
            };
        }

        return null;
    }

    private static string ResolveGenericCompaction(string query, string language, string queryKind)
    {
        var tokens = TokenRegex()
            .Matches(query.ToLowerInvariant())
            .Select(static match => match.Value)
            .Where(token => token.Length >= 3)
            .Where(token => !GenericStopTokens.Contains(token, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(6)
            .ToList();

        if (tokens.Count == 0)
        {
            return query;
        }

        if (string.Equals(queryKind, "freshness", StringComparison.OrdinalIgnoreCase))
        {
            tokens.Add(string.Equals(language, "ru", StringComparison.OrdinalIgnoreCase) ? "обновления" : "updates");
        }
        else if (string.Equals(queryKind, "official", StringComparison.OrdinalIgnoreCase))
        {
            tokens.Add(string.Equals(language, "ru", StringComparison.OrdinalIgnoreCase) ? "официальные" : "official");
        }
        else if (string.Equals(queryKind, "evidence", StringComparison.OrdinalIgnoreCase))
        {
            tokens.Add("guideline");
        }

        return SearchQueryIntentProfileClassifier.NormalizeWhitespace(string.Join(' ', tokens.Distinct(StringComparer.OrdinalIgnoreCase)));
    }

    private static bool LooksLikeMigraineGuidelineQuery(string query)
    {
        return ContainsAny(query, "мигрен", "migraine") &&
               ContainsAny(query, "профилакти", "prevention") &&
               ContainsAny(query, "рекомендац", "guideline", "guidelines");
    }

    private static bool LooksLikePrediabetesNutritionQuery(string query)
    {
        return ContainsAny(
            query,
            "преддиаб", "prediabet", "nutrition", "diet", "meal", "glycem", "glucose", "insulin", "carbohyd", "meal plan",
            "рацион", "питани", "диет", "гликем", "глюкоз", "инсулин", "углевод");
    }

    private static bool ContainsAny(string text, params string[] markers)
    {
        foreach (var marker in markers)
        {
            if (text.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string Summarize(string value)
    {
        if (value.Length <= 180)
        {
            return value;
        }

        return value[..180];
    }

    [GeneratedRegex(@"[\p{L}\p{Nd}]+", RegexOptions.Compiled)]
    private static partial Regex TokenRegex();
}
