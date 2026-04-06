using System.Text.RegularExpressions;

namespace Helper.Runtime.WebResearch;

public interface ISearchTopicCoreRewritePolicy
{
    SearchTopicCoreRewriteDecision Rewrite(string query);
}

public sealed record SearchTopicCoreRewriteDecision(
    string Query,
    bool Applied,
    IReadOnlyList<string> Trace);

public sealed partial class SearchTopicCoreRewritePolicy : ISearchTopicCoreRewritePolicy
{
    private static readonly string[] MetaNoiseTokens =
    {
        "please", "helper", "tell", "give", "provide", "opinion", "your", "simple", "simply",
        "пожалуйста", "хелпер", "предоставь", "мнение", "свое", "своё", "простыми", "словами",
        "изменилось", "уточнилось", "что", "обычно", "мой", "моя", "мое", "моё"
    };

    public SearchTopicCoreRewriteDecision Rewrite(string query)
    {
        var normalized = SearchQueryIntentProfileClassifier.NormalizeWhitespace(query);
        if (normalized.Length == 0)
        {
            return new SearchTopicCoreRewriteDecision(
                string.Empty,
                Applied: false,
                new[] { "search_query.rewrite stage=topic_core applied=no reason=empty_query" });
        }

        var profile = Ranking.SourceAuthorityScorer.BuildQueryProfile(normalized, null);
        var intent = SearchQueryIntentProfileClassifier.Classify(normalized, profile);
        if (!intent.HumanPromptLike && !intent.UrlAnchored)
        {
            return new SearchTopicCoreRewriteDecision(
                normalized,
                Applied: false,
                new[]
                {
                    $"search_query.rewrite stage=topic_core applied=no reason=already_search_like language={intent.Language}",
                    $"search_query.rewrite stage=topic_core result=\"{Summarize(normalized)}\""
                });
        }

        var trace = new List<string>
        {
            $"search_query.rewrite stage=topic_core language={intent.Language}",
            $"search_query.rewrite stage=topic_core prompt_like={(intent.HumanPromptLike ? "yes" : "no")} url_anchored={(intent.UrlAnchored ? "yes" : "no")}"
        };

        var lexical = ApplyLexicalCleanup(normalized, intent.Language);
        trace.Add($"search_query.rewrite stage=lexical_cleanup changed={(Changed(normalized, lexical) ? "yes" : "no")}");

        var topical = ApplyTopicalPhraseRewrite(lexical, intent.Language);
        trace.Add($"search_query.rewrite stage=phrase_rewrite changed={(Changed(lexical, topical) ? "yes" : "no")}");

        var tokenCleaned = CleanupMetaTokens(topical);
        var candidate = tokenCleaned;
        if (CountTokens(candidate) < 3)
        {
            candidate = SearchQueryIntentProfileClassifier.NormalizeWhitespace(topical);
            trace.Add("search_query.rewrite stage=token_cleanup fallback=pretokenized_core");
        }

        if (candidate.Length == 0)
        {
            candidate = normalized;
            trace.Add("search_query.rewrite stage=topic_core fallback=original_query");
        }

        trace.Add($"search_query.rewrite stage=topic_core result=\"{Summarize(candidate)}\"");

        return new SearchTopicCoreRewriteDecision(
            candidate,
            Applied: Changed(normalized, candidate),
            trace);
    }

    private static string ApplyLexicalCleanup(string value, string language)
    {
        var rewritten = UrlRegex().Replace(value, " ");
        rewritten = SpaceRegex().Replace(rewritten, " ").Trim();

        if (string.Equals(language, "ru", StringComparison.OrdinalIgnoreCase))
        {
            rewritten = RussianLeadInstructionRegex().Replace(rewritten, string.Empty, 1);
            rewritten = RussianOutputMetaRegex().Replace(rewritten, " ");
            rewritten = RussianFollowupClauseRegex().Replace(rewritten, " ");
            rewritten = RussianConflictFramingRegex().Replace(rewritten, " ");
        }
        else
        {
            rewritten = EnglishLeadInstructionRegex().Replace(rewritten, string.Empty, 1);
            rewritten = EnglishOutputMetaRegex().Replace(rewritten, " ");
            rewritten = EnglishFollowupClauseRegex().Replace(rewritten, " ");
            rewritten = EnglishConflictFramingRegex().Replace(rewritten, " ");
        }

        return SearchQueryIntentProfileClassifier.NormalizeWhitespace(rewritten);
    }

    private static string ApplyTopicalPhraseRewrite(string value, string language)
    {
        var rewritten = value;
        if (string.Equals(language, "ru", StringComparison.OrdinalIgnoreCase))
        {
            if (LooksLikeMixedLanguageTaxDeadlinePrompt(rewritten))
            {
                return "налоговые пороги лимиты сроки отчетности официальные требования";
            }

            if (LooksLikeRussianMigraineGuidelinePrompt(rewritten))
            {
                return "мигрень профилактика клинические рекомендации";
            }

            if (LooksLikeRussianPrediabetesNutritionPrompt(rewritten))
            {
                return "преддиабет питание рацион официальные рекомендации";
            }

            rewritten = RussianCurrentKnowledgeRegex().Replace(rewritten, string.Empty);
            rewritten = RussianBuildGuidanceRegex().Replace(rewritten, string.Empty);
            rewritten = RussianEvidencePersuasivenessRegex().Replace(rewritten, string.Empty);
            rewritten = RussianDoesItHelpRegex().Replace(rewritten, string.Empty);
            rewritten = RussianTaxThresholdsDeadlinesRegex().Replace(rewritten, " налоговые пороги лимиты сроки отчетности filing deadline ");
            rewritten = RussianUserCurrentUseClauseRegex().Replace(rewritten, string.Empty);
            rewritten = RussianIntermittentFastingLeanMassRegex().Replace(rewritten, " intermittent fasting time-restricted eating lean mass body composition weight loss ");
            rewritten = RussianRedLightRecoveryEvidenceRegex().Replace(rewritten, " photobiomodulation red light therapy muscle recovery after training ");
            rewritten = RussianLatestGuidelinesFollowupRegex().Replace(rewritten, " последние ");
            rewritten = RussianLatestGuidelinesClauseRegex().Replace(rewritten, " последние ");
            rewritten = RussianOfficialMeasuresRegex().Replace(rewritten, " официальные меры профилактики ");
            rewritten = RussianSourcePreferenceRegex().Replace(rewritten, " актуальные источники ");
        }
        else
        {
            if (LooksLikeEnglishPrediabetesNutritionPrompt(rewritten))
            {
                return "prediabetes diet nutrition official recommendations";
            }

            rewritten = EnglishCurrentKnowledgeRegex().Replace(rewritten, string.Empty);
            rewritten = EnglishEvidencePersuasivenessRegex().Replace(rewritten, string.Empty);
            rewritten = EnglishIntermittentFastingLeanMassRegex().Replace(rewritten, " intermittent fasting time-restricted eating lean mass body composition weight loss ");
            rewritten = EnglishRedLightRecoveryEvidenceRegex().Replace(rewritten, " photobiomodulation red light therapy muscle recovery after training ");
            rewritten = EnglishLatestGuidelinesFollowupRegex().Replace(rewritten, " latest ");
            rewritten = EnglishLatestGuidelinesClauseRegex().Replace(rewritten, " latest ");
            rewritten = EnglishOfficialMeasuresRegex().Replace(rewritten, " official prevention measures ");
            rewritten = EnglishSourcePreferenceRegex().Replace(rewritten, " current sources ");
        }

        return SearchQueryIntentProfileClassifier.NormalizeWhitespace(rewritten);
    }

    private static bool LooksLikeMixedLanguageTaxDeadlinePrompt(string value)
    {
        return value.Contains("налог", StringComparison.OrdinalIgnoreCase) &&
               (value.Contains("threshold", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("deadline", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("reporting", StringComparison.OrdinalIgnoreCase));
    }

    private static bool LooksLikeRussianMigraineGuidelinePrompt(string value)
    {
        return value.Contains("мигрен", StringComparison.OrdinalIgnoreCase) &&
               value.Contains("профилакти", StringComparison.OrdinalIgnoreCase) &&
               value.Contains("рекомендац", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeRussianPrediabetesNutritionPrompt(string value)
    {
        return value.Contains("преддиаб", StringComparison.OrdinalIgnoreCase) &&
               (value.Contains("рацион", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("питани", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("диет", StringComparison.OrdinalIgnoreCase));
    }

    private static bool LooksLikeEnglishPrediabetesNutritionPrompt(string value)
    {
        return value.Contains("prediabet", StringComparison.OrdinalIgnoreCase) &&
               (value.Contains("diet", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("nutrition", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("meal", StringComparison.OrdinalIgnoreCase));
    }

    private static string CleanupMetaTokens(string value)
    {
        var cleaned = value
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static token => token.Trim(',', '.', ':', ';', '!', '?', '"', '\'', '«', '»', '(', ')'))
            .Where(static token => token.Length > 0)
            .Where(token => !MetaNoiseTokens.Contains(token, StringComparer.OrdinalIgnoreCase))
            .ToArray();

        return string.Join(' ', cleaned);
    }

    private static bool Changed(string left, string right)
    {
        return !string.Equals(left, right, StringComparison.Ordinal);
    }

    private static int CountTokens(string value)
    {
        return value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;
    }

    private static string Summarize(string value)
    {
        return value.Length <= 180 ? value : value[..180];
    }

    [GeneratedRegex(@"https?://[^\s\)\]\}>]+", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex UrlRegex();

    [GeneratedRegex(@"^(?:объясни|расскажи|опиши|разбери|проанализируй|сравни|проверь|уточни|покажи|оцени)\b[\s,:-]*", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex RussianLeadInstructionRegex();

    [GeneratedRegex(@"^(?:please\s+)?(?:explain|describe|analyze|review|compare|check|verify|summarize|evaluate|assess)\b[\s,:-]*", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex EnglishLeadInstructionRegex();

    [GeneratedRegex(@"\b(?:простыми\s+словами|предостав(?:ь|ьте)\s+(?:сво[её]\s+)?мнение|сво[её]\s+мнение)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex RussianOutputMetaRegex();

    [GeneratedRegex(@"\b(?:in\s+simple\s+terms|in\s+plain\s+english|provide\s+your\s+opinion|give\s+your\s+opinion|your\s+opinion)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex EnglishOutputMetaRegex();

    [GeneratedRegex(@"\b(?:а\s+затем|затем|после\s+этого)\s+(?:проверь|сверь|уточни(?:,?\s*что)?|посмотри|покажи)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex RussianFollowupClauseRegex();

    [GeneratedRegex(@"\b(?:and\s+then|then|after\s+that)\s+(?:check|verify|clarify|review|look\s+up)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex EnglishFollowupClauseRegex();

    [GeneratedRegex(@"\bесли\s+(?:научные\s+и\s+популярные|источники)\s+источники?\s+расходятся\b|\bесли\s+научные\s+и\s+популярные\s+источники\s+расходятся\b|\bесли\s+источники\s+расходятся\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex RussianConflictFramingRegex();

    [GeneratedRegex(@"\bif\s+(?:scientific\s+and\s+popular|sources)\s+sources?\s+disagree\b|\bif\s+scientific\s+and\s+popular\s+sources\s+disagree\b|\bif\s+sources\s+disagree\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex EnglishConflictFramingRegex();

    [GeneratedRegex(@"\bчто\s+(?:сейчас\s+)?известно\s+о\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex RussianCurrentKnowledgeRegex();

    [GeneratedRegex(@"\bwhat\s+is\s+(?:currently\s+)?known\s+about\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex EnglishCurrentKnowledgeRegex();

    [GeneratedRegex(@"\bкак\s+обычно\s+строят\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex RussianBuildGuidanceRegex();

    [GeneratedRegex(@"\bнасколько\s+убедительны\s+данные\s+о\s+пользе\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex RussianEvidencePersuasivenessRegex();

    [GeneratedRegex(@"\bhow\s+strong\s+is\s+the\s+evidence\s+for\b|\bhow\s+convincing\s+is\s+the\s+evidence\s+for\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex EnglishEvidencePersuasivenessRegex();

    [GeneratedRegex(@"\bпомогает\s+ли\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex RussianDoesItHelpRegex();

    [GeneratedRegex(@"\bналог\w*\s+thresholds?\b|\bthresholds?\b|\breporting\s+deadlines?\b|\bfilling?\s+deadlines?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex RussianTaxThresholdsDeadlinesRegex();

    [GeneratedRegex(@"\bкотор\w*\s+я\s+пользуюсь\s+сегодня\b|\bкотор\w*\s+я\s+использую\s+сегодня\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex RussianUserCurrentUseClauseRegex();

    [GeneratedRegex(@"\bинтервальн\w*\s+голодан\w*.*(?:сниж\w*\s+вес|похуд\w*).*(?:без\s+потер\w+\s+мыш\w*|мыш\w+\s+мас\w*|состав\w+\s+тел\w*)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex RussianIntermittentFastingLeanMassRegex();

    [GeneratedRegex(@"\b(?:терап\w+\s+)?красн\w*\s+свет\w*.*(?:восстановлен\w+).*(?:трениров\w+|нагруз\w+)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex RussianRedLightRecoveryEvidenceRegex();

    [GeneratedRegex(@"\bintermittent\s+fasting.*(?:weight\s+loss|lose\s+weight).*(?:without\s+losing\s+muscle|lean\s+mass|body\s+composition)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex EnglishIntermittentFastingLeanMassRegex();

    [GeneratedRegex(@"\bred\s+light\s+therapy.*(?:recovery|post-workout|after\s+training|exercise)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex EnglishRedLightRecoveryEvidenceRegex();

    [GeneratedRegex(@"\bпроверь,?\s*что\s+изменилось\s+или\s+уточнилось\s+в\s+последних\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex RussianLatestGuidelinesFollowupRegex();

    [GeneratedRegex(@"\bчто\s+изменилось\s+или\s+уточнилось\s+в\s+последних\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex RussianLatestGuidelinesClauseRegex();

    [GeneratedRegex(@"\bcheck\s+what\s+changed\s+or\s+was\s+clarified\s+in\s+the\s+latest\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex EnglishLatestGuidelinesFollowupRegex();

    [GeneratedRegex(@"\bwhat\s+changed\s+or\s+was\s+clarified\s+in\s+the\s+latest\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex EnglishLatestGuidelinesClauseRegex();

    [GeneratedRegex(@"\bкакие\s+официальные\s+меры\s+профилактики\s+рекомендуют(?:\s+на\s+сегодня)?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex RussianOfficialMeasuresRegex();

    [GeneratedRegex(@"\bwhat\s+official\s+prevention\s+measures\s+are\s+recommended(?:\s+today)?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex EnglishOfficialMeasuresRegex();

    [GeneratedRegex(@"\bчто\s+предпочитают\s+актуальные\s+(?:спортивные\s+и\s+клинические|внешние)\s+источники\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex RussianSourcePreferenceRegex();

    [GeneratedRegex(@"\bwhat\s+do\s+current\s+(?:sports\s+and\s+clinical|external)\s+sources\s+prefer\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex EnglishSourcePreferenceRegex();

    [GeneratedRegex(@"\s+", RegexOptions.Compiled)]
    private static partial Regex SpaceRegex();
}

