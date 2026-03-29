namespace Helper.Runtime.WebResearch;

internal sealed record SearchQueryExpansionDecision(
    string Query,
    IReadOnlyList<string> Trace);

internal interface ISearchQueryExpansionPolicy
{
    SearchQueryExpansionDecision RewriteFreshness(string baseQuery, SearchQueryIntentProfile intentProfile, Ranking.SearchRankingQueryProfile queryProfile);

    SearchQueryExpansionDecision RewriteEvidence(string baseQuery, SearchQueryIntentProfile intentProfile, Ranking.SearchRankingQueryProfile queryProfile);

    SearchQueryExpansionDecision RewriteContradiction(string baseQuery, SearchQueryIntentProfile intentProfile, Ranking.SearchRankingQueryProfile queryProfile);

    SearchQueryExpansionDecision RewriteNarrow(string baseQuery, SearchQueryIntentProfile intentProfile, Ranking.SearchRankingQueryProfile queryProfile);

    SearchQueryExpansionDecision RewriteStepBack(string baseQuery, SearchQueryIntentProfile intentProfile, Ranking.SearchRankingQueryProfile queryProfile);

    SearchQueryExpansionDecision RewriteOfficial(string baseQuery, SearchQueryIntentProfile intentProfile, Ranking.SearchRankingQueryProfile queryProfile);

    SearchQueryExpansionDecision RewritePaperFocus(string baseQuery, SearchQueryIntentProfile intentProfile, Ranking.SearchRankingQueryProfile queryProfile);
}

internal sealed class SearchQueryExpansionPolicy : ISearchQueryExpansionPolicy
{
    private static readonly string[] NarrowingStopWords =
    {
        "latest", "current", "today", "news", "update", "updates", "about",
        "последние", "текущие", "сегодня", "новости", "обновления", "про"
    };

    private static readonly string[] StepBackStopWords =
    {
        "latest", "current", "today", "news", "update", "updates", "official", "current", "latest",
        "последние", "текущие", "сегодня", "новости", "обновления", "официальные", "актуальные"
    };

    public SearchQueryExpansionDecision RewriteFreshness(string baseQuery, SearchQueryIntentProfile intentProfile, Ranking.SearchRankingQueryProfile queryProfile)
    {
        var terms = queryProfile.MedicalEvidenceHeavy
            ? ContainsAny(baseQuery, "guideline", "guidelines", "рекомендац")
                ? Terms(intentProfile.Language, "latest guidance update", "последние обновления рекомендаций")
                : Terms(intentProfile.Language, "latest clinical guideline", "последние клинические рекомендации")
            : intentProfile.OfficialBias
                ? Terms(intentProfile.Language, "official update", "официальное обновление")
                : Terms(intentProfile.Language, "latest update", "последние обновления");

        var rewritten = AppendDistinctTerms(baseQuery, terms);
        return new SearchQueryExpansionDecision(
            rewritten,
            new[]
            {
                $"search_query.rewrite stage=freshness applied={(Changed(baseQuery, rewritten) ? "yes" : "no")} terms={string.Join("|", terms)}",
                $"search_query.rewrite stage=freshness result=\"{Summarize(rewritten)}\""
            });
    }

    public SearchQueryExpansionDecision RewriteEvidence(string baseQuery, SearchQueryIntentProfile intentProfile, Ranking.SearchRankingQueryProfile queryProfile)
    {
        var terms = queryProfile.MedicalEvidenceHeavy
            ? ContainsAny(baseQuery, "intermittent fasting", "time-restricted", "голодание", "состав тела", "мышечная масса", "без потери мышц")
                ? Terms(
                    intentProfile.Language,
                    "lean mass body composition muscle mass fat loss pubmed pmc systematic review meta-analysis randomized trial",
                    "мышечная масса состав тела lean mass body composition pubmed pmc systematic review meta-analysis randomized trial")
                : ContainsAny(baseQuery, "red light", "red-light", "light therapy", "phototherapy", "photobiomodulation", "красн свет", "красным свет", "светотерап", "фототерап", "фотобиомодуляц")
                ? Terms(
                    intentProfile.Language,
                    "photobiomodulation red light therapy muscle recovery exercise performance whole-body pubmed pmc springer systematic review meta-analysis randomized trial",
                    "photobiomodulation red light therapy мышечное восстановление exercise performance whole-body pubmed pmc springer systematic review meta-analysis randomized trial")
                : Terms(intentProfile.Language, "clinical guideline systematic review meta-analysis randomized trial", "клинические рекомендации systematic review meta-analysis randomized trial")
            : Terms(intentProfile.Language, "official guidance evidence review", "официальное руководство обзор доказательств");
        var rewritten = AppendDistinctTerms(baseQuery, terms);
        return new SearchQueryExpansionDecision(
            rewritten,
            new[]
            {
                $"search_query.rewrite stage=evidence applied={(Changed(baseQuery, rewritten) ? "yes" : "no")} terms={string.Join("|", terms)}",
                $"search_query.rewrite stage=evidence result=\"{Summarize(rewritten)}\""
            });
    }

    public SearchQueryExpansionDecision RewriteContradiction(string baseQuery, SearchQueryIntentProfile intentProfile, Ranking.SearchRankingQueryProfile queryProfile)
    {
        var terms = queryProfile.MedicalEvidenceHeavy
            ? ContainsAny(baseQuery, "intermittent fasting", "time-restricted", "голодание", "состав тела", "мышечная масса", "без потери мышц")
                ? Terms(intentProfile.Language, "lean mass body composition pubmed pmc systematic review meta-analysis randomized trial guideline", "мышечная масса состав тела lean mass body composition pubmed pmc systematic review meta-analysis randomized trial guideline")
                : Terms(intentProfile.Language, "systematic review meta-analysis randomized trial guideline", "systematic review meta-analysis randomized trial guideline")
            : intentProfile.OfficialBias
                ? Terms(intentProfile.Language, "conflicting reports official guidance", "противоречивые сообщения официальные рекомендации")
                : Terms(intentProfile.Language, "conflicting sources", "противоречивые источники");
        var rewritten = AppendDistinctTerms(baseQuery, terms);
        return new SearchQueryExpansionDecision(
            rewritten,
            new[]
            {
                $"search_query.rewrite stage=contradiction applied={(Changed(baseQuery, rewritten) ? "yes" : "no")} terms={string.Join("|", terms)}",
                $"search_query.rewrite stage=contradiction result=\"{Summarize(rewritten)}\""
            });
    }

    public SearchQueryExpansionDecision RewriteNarrow(string baseQuery, SearchQueryIntentProfile intentProfile, Ranking.SearchRankingQueryProfile queryProfile)
    {
        var narrowed = baseQuery
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => !NarrowingStopWords.Contains(token, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(queryProfile.EvidenceHeavy ? 8 : 10)
            .ToArray();

        var rewritten = narrowed.Length == 0
            ? string.Empty
            : string.Join(' ', narrowed);

        if (string.Equals(rewritten, baseQuery, StringComparison.OrdinalIgnoreCase))
        {
            rewritten = string.Empty;
        }

        return new SearchQueryExpansionDecision(
            rewritten,
            new[]
            {
                $"search_query.rewrite stage=narrow applied={(!string.IsNullOrWhiteSpace(rewritten) ? "yes" : "no")}",
                $"search_query.rewrite stage=narrow result=\"{Summarize(rewritten)}\""
            });
    }

    public SearchQueryExpansionDecision RewriteStepBack(string baseQuery, SearchQueryIntentProfile intentProfile, Ranking.SearchRankingQueryProfile queryProfile)
    {
        var tokens = baseQuery
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => !StepBackStopWords.Contains(token, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(queryProfile.EvidenceHeavy ? 6 : 7)
            .ToList();

        var overviewToken = string.Equals(intentProfile.Language, "ru", StringComparison.OrdinalIgnoreCase)
            ? "обзор"
            : "overview";
        if (!tokens.Contains(overviewToken, StringComparer.OrdinalIgnoreCase))
        {
            tokens.Add(overviewToken);
        }

        var rewritten = SearchQueryIntentProfileClassifier.NormalizeWhitespace(string.Join(' ', tokens));
        if (string.Equals(rewritten, baseQuery, StringComparison.OrdinalIgnoreCase))
        {
            rewritten = string.Empty;
        }

        return new SearchQueryExpansionDecision(
            rewritten,
            new[]
            {
                $"search_query.rewrite stage=step_back applied={(!string.IsNullOrWhiteSpace(rewritten) ? "yes" : "no")}",
                $"search_query.rewrite stage=step_back result=\"{Summarize(rewritten)}\""
            });
    }

    public SearchQueryExpansionDecision RewriteOfficial(string baseQuery, SearchQueryIntentProfile intentProfile, Ranking.SearchRankingQueryProfile queryProfile)
    {
        var terms = queryProfile.MedicalEvidenceHeavy
            ? Terms(intentProfile.Language, "official guideline society who", "официальные рекомендации общество who")
            : Terms(intentProfile.Language, "official source guidance", "официальный источник руководство");
        var rewritten = AppendDistinctTerms(baseQuery, terms);
        return new SearchQueryExpansionDecision(
            rewritten,
            new[]
            {
                $"search_query.rewrite stage=official applied={(Changed(baseQuery, rewritten) ? "yes" : "no")} terms={string.Join("|", terms)}",
                $"search_query.rewrite stage=official result=\"{Summarize(rewritten)}\""
            });
    }

    public SearchQueryExpansionDecision RewritePaperFocus(string baseQuery, SearchQueryIntentProfile intentProfile, Ranking.SearchRankingQueryProfile queryProfile)
    {
        var terms = Terms(intentProfile.Language, "paper pdf abstract method results", "статья pdf аннотация метод результаты");
        var rewritten = AppendDistinctTerms(baseQuery, terms);
        return new SearchQueryExpansionDecision(
            rewritten,
            new[]
            {
                $"search_query.rewrite stage=paper_focus applied={(Changed(baseQuery, rewritten) ? "yes" : "no")} terms={string.Join("|", terms)}",
                $"search_query.rewrite stage=paper_focus result=\"{Summarize(rewritten)}\""
            });
    }

    private static string[] Terms(string language, string english, string russian)
    {
        return string.Equals(language, "ru", StringComparison.OrdinalIgnoreCase)
            ? russian.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : english.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static string AppendDistinctTerms(string baseQuery, IEnumerable<string> terms)
    {
        var tokens = baseQuery
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        foreach (var term in terms)
        {
            if (!tokens.Contains(term, StringComparer.OrdinalIgnoreCase))
            {
                tokens.Add(term);
            }
        }

        return SearchQueryIntentProfileClassifier.NormalizeWhitespace(string.Join(' ', tokens));
    }

    private static bool ContainsAny(string text, params string[] tokens)
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

    private static bool Changed(string left, string right)
    {
        return !string.Equals(left, right, StringComparison.Ordinal);
    }

    private static string Summarize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Length <= 180 ? value : value[..180];
    }
}

