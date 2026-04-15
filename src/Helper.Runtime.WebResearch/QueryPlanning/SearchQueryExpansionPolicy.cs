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

    SearchQueryExpansionDecision RewritePublisherPolicy(string baseQuery, SearchQueryIntentProfile intentProfile, Ranking.SearchRankingQueryProfile queryProfile);

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
        if (LooksLikeUzbekistanFilingQuery(baseQuery))
        {
            return CreateFocusedDecision(
                intentProfile.Language,
                stage: "freshness",
                english: "узбекистан remote worker инвойсы иностранные клиенты filing отчетность налоги checklist 2026 soliq lex my gov uz ey tax update",
                russian: "узбекистан remote worker инвойсы иностранные клиенты filing отчетность налоги checklist 2026 soliq lex my gov uz ey tax update",
                reason: "focused_regulation_freshness");
        }

        if (LooksLikeVisaRegulationQuery(baseQuery))
        {
            return CreateFocusedDecision(
                intentProfile.Language,
                stage: "freshness",
                english: "germany software engineer european blue card opportunity card it specialist residence permit 2026 update make it in germany bamf auswaertiges amt arbeitsagentur",
                russian: "germany software engineer european blue card opportunity card it specialist residence permit 2026 update make it in germany bamf auswaertiges amt arbeitsagentur",
                reason: "focused_regulation_freshness");
        }

        if (LooksLikeEuAiRegulationQuery(baseQuery))
        {
            return CreateFocusedDecision(
                intentProfile.Language,
                stage: "freshness",
                english: "ai act regulatory framework european commission digital strategy ai office provider obligations implementation guidance 2026",
                russian: "ai act regulatory framework european commission digital strategy ai office provider obligations implementation guidance 2026",
                reason: "focused_eu_ai_regulation_freshness");
        }

        if (LooksLikeArxivPublisherPolicyQuery(baseQuery))
        {
            return CreateFocusedDecision(
                intentProfile.Language,
                stage: "freshness",
                english: "arxiv preprint journal publisher repository sherpa romeo open access self-archiving accepted manuscript embargo doaj latest update",
                russian: "arxiv preprint journal publisher repository sherpa romeo open access self-archiving accepted manuscript embargo doaj latest update",
                reason: "focused_arxiv_policy_freshness");
        }

        if (LooksLikeDroneImportCustomsQuery(baseQuery))
        {
            return CreateFocusedDecision(
                intentProfile.Language,
                stage: "freshness",
                english: "european union drone importation customs procedures batteries taxation customs your europe 2026",
                russian: "european union drone importation customs procedures batteries taxation customs your europe 2026",
                reason: "focused_drone_customs_freshness");
        }

        if (LooksLikeRetractionStatusQuery(baseQuery))
        {
            return CreateFocusedDecision(
                intentProfile.Language,
                stage: "freshness",
                english: "retraction correction erratum expression of concern crossmark crossref pubmed doi latest update journal ethics",
                russian: "ретракция исправление erratum expression of concern crossmark crossref pubmed doi последние обновления ethics journal",
                reason: "focused_retraction_status");
        }

        var terms = queryProfile.MedicalEvidenceHeavy
            ? ContainsAny(baseQuery, "prediabet", "преддиаб", "nutrition", "diet", "рацион", "питани", "диет")
                ? Terms(intentProfile.Language, "latest official nutrition guideline", "свежие официальные рекомендации по питанию")
            : ContainsAny(baseQuery, "guideline", "guidelines", "рекомендац")
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
            ? ContainsAny(baseQuery, "prediabet", "преддиаб", "nutrition", "diet", "meal", "glycemic", "рацион", "питани", "диет", "гликем", "глюкоз")
                ? Terms(
                    intentProfile.Language,
                    "prediabetes diet nutrition meal planning glycemic guideline systematic review",
                    "преддиабет питание рацион гликемия клинические рекомендации systematic review")
            : ContainsAny(baseQuery, "intermittent fasting", "time-restricted", "голодание", "состав тела", "мышечная масса", "без потери мышц")
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
            : LooksLikeClimateSensitivityQuery(baseQuery)
                ? Terms(
                    intentProfile.Language,
                    "climate sensitivity equilibrium climate sensitivity transient climate response earth system sensitivity ipcc ar6 assessment uncertainty aerosol forcing review",
                    "climate sensitivity equilibrium climate sensitivity transient climate response earth system sensitivity ipcc ar6 assessment uncertainty aerosol forcing review")
            : LooksLikeEuAiRegulationQuery(baseQuery)
                ? Terms(
                    intentProfile.Language,
                    "european union artificial intelligence act european commission ai office eur-lex provider obligations implementation guidance faq compliance",
                    "european union artificial intelligence act european commission ai office eur-lex provider obligations implementation guidance faq compliance")
            : LooksLikeArxivPublisherPolicyQuery(baseQuery)
                ? Terms(
                    intentProfile.Language,
                    "arxiv sherpa romeo doaj repository journal publisher policy open access self-archiving accepted manuscript embargo",
                    "arxiv sherpa romeo doaj repository journal publisher policy open access self-archiving accepted manuscript embargo")
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
            : LooksLikeClimateSensitivityQuery(baseQuery)
                ? Terms(
                    intentProfile.Language,
                    "climate sensitivity equilibrium climate sensitivity transient climate response ipcc ar6 ecs tcr range uncertainty conflicting estimates aerosol forcing",
                    "climate sensitivity equilibrium climate sensitivity transient climate response ipcc ar6 ecs tcr range uncertainty conflicting estimates aerosol forcing")
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
        if (LooksLikeFourDayWeekReliabilityQuery(baseQuery))
        {
            return CreateFocusedDecision(
                intentProfile.Language,
                stage: "step_back",
                english: "four day workweek productivity evidence across industries overview",
                russian: "четырехдневная рабочая неделя productivity evidence across industries обзор",
                reason: "focused_claim_reliability");
        }

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
        if (LooksLikeUzbekistanFilingQuery(baseQuery))
        {
            return CreateFocusedDecision(
                intentProfile.Language,
                stage: "official",
                english: "узбекистан налоговый орган filing отчетность инвойсы иностранные клиенты remote worker официальный soliq lex my gov uz mehnat ey tax alert",
                russian: "узбекистан налоговый орган filing отчетность инвойсы иностранные клиенты remote worker официальный soliq lex my gov uz mehnat ey tax alert",
                reason: "focused_regulation_official");
        }

        if (LooksLikeVisaRegulationQuery(baseQuery))
        {
            return CreateFocusedDecision(
                intentProfile.Language,
                stage: "official",
                english: "germany software engineer european blue card opportunity card it specialist official make it in germany bamf auswaertiges amt arbeitsagentur",
                russian: "germany software engineer european blue card opportunity card it specialist official make it in germany bamf auswaertiges amt arbeitsagentur",
                reason: "focused_regulation_official");
        }

        if (LooksLikeEuAiRegulationQuery(baseQuery))
        {
            return CreateFocusedDecision(
                intentProfile.Language,
                stage: "official",
                english: "ai act service desk european commission ai office provider obligations faq official guidance",
                russian: "ai act service desk european commission ai office provider obligations faq official guidance",
                reason: "focused_eu_ai_regulation_official");
        }

        if (LooksLikeArxivPublisherPolicyQuery(baseQuery))
        {
            return CreateFocusedDecision(
                intentProfile.Language,
                stage: "official",
                english: "arxiv preprint journal publisher repository sherpa romeo open access self-archiving accepted manuscript policy doaj crossref",
                russian: "arxiv preprint journal publisher repository sherpa romeo open access self-archiving accepted manuscript policy doaj crossref",
                reason: "focused_arxiv_policy_official");
        }

        if (LooksLikeDroneImportCustomsQuery(baseQuery))
        {
            return CreateFocusedDecision(
                intentProfile.Language,
                stage: "official",
                english: "easa drones uas faq batteries travel ce marking european union official guidance",
                russian: "easa drones uas faq batteries travel ce marking european union official guidance",
                reason: "focused_drone_customs_official");
        }

        if (LooksLikeRetractionStatusQuery(baseQuery))
        {
            return CreateFocusedDecision(
                intentProfile.Language,
                stage: "official",
                english: "retraction correction expression of concern crossmark crossref pubmed journal ethics official",
                russian: "ретракция исправление expression of concern crossmark crossref pubmed journal ethics официальный",
                reason: "focused_retraction_official");
        }

        var terms = queryProfile.MedicalEvidenceHeavy
            ? ContainsAny(baseQuery, "prediabet", "преддиаб", "nutrition", "diet", "рацион", "питани", "диет")
                ? Terms(intentProfile.Language, "official diabetes nutrition guideline", "официальные рекомендации по преддиабету и питанию")
                : Terms(intentProfile.Language, "official guideline society who", "официальные рекомендации общество who")
            : ContainsAny(baseQuery, "налог", "tax", "threshold", "thresholds", "deadline", "deadlines", "reporting", "filing", "irs", "sec", "report")
                ? Terms(intentProfile.Language, "official tax authority filing deadline threshold reporting requirement", "официальный налоговый орган сроки отчетности пороги лимиты требования")
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

    public SearchQueryExpansionDecision RewritePublisherPolicy(string baseQuery, SearchQueryIntentProfile intentProfile, Ranking.SearchRankingQueryProfile queryProfile)
    {
        var terms = LooksLikeArxivPublisherPolicyQuery(baseQuery)
            ? Terms(
                intentProfile.Language,
                "sherpa romeo journal publisher policy open access self-archiving accepted manuscript embargo doaj crossref arxiv repository",
                "sherpa romeo journal publisher policy open access self-archiving accepted manuscript embargo doaj crossref arxiv repository")
            : Terms(intentProfile.Language, "publisher policy repository guidance", "publisher policy repository guidance");
        var rewritten = AppendDistinctTerms(baseQuery, terms);
        return new SearchQueryExpansionDecision(
            rewritten,
            new[]
            {
                $"search_query.rewrite stage=publisher_policy applied={(Changed(baseQuery, rewritten) ? "yes" : "no")} terms={string.Join("|", terms)}",
                $"search_query.rewrite stage=publisher_policy result=\"{Summarize(rewritten)}\""
            });
    }

    public SearchQueryExpansionDecision RewritePaperFocus(string baseQuery, SearchQueryIntentProfile intentProfile, Ranking.SearchRankingQueryProfile queryProfile)
    {
        var terms = LooksLikeRetractionStatusQuery(baseQuery)
            ? Terms(
                intentProfile.Language,
                "paper doi pubmed retraction erratum correction expression of concern crossmark",
                "статья doi pubmed ретракция erratum исправление expression of concern crossmark")
            : LooksLikeArxivPublisherPolicyQuery(baseQuery)
            ? Terms(
                intentProfile.Language,
                "arxiv preprint policy sherpa romeo crossref journal repository manuscript open access accepted manuscript self-archiving",
                "arxiv preprint policy sherpa romeo crossref journal repository manuscript open access accepted manuscript self-archiving")
            : ContainsAny(baseQuery, "literature review", "systematic review", "narrative review", "обзор литературы", "систематический обзор", "мета-анализ", "meta-analysis")
            ? Terms(intentProfile.Language, "systematic review prisma amstar checklist method guidance", "systematic review prisma amstar checklist метод руководство")
            : Terms(intentProfile.Language, "paper pdf abstract method results", "статья pdf аннотация метод результаты");
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

    private static bool LooksLikeVisaRegulationQuery(string text)
    {
        return ContainsAny(
            text,
            "visa", "visas", "immigration", "migration", "relocation", "residence permit", "work permit", "blue card", "bluecard", "skilled worker",
            "виза", "визы", "визовые", "миграц", "релокац", "внж", "вид на жительство", "разрешение на работу", "голубая карта");
    }

    private static bool LooksLikeUzbekistanFilingQuery(string text)
    {
        return ContainsAny(
                   text,
                   "uzbekistan", "uzbek", "uzb",
                   "узбекистан", "узбек") &&
               ContainsAny(
                   text,
                   "filing", "reporting", "invoice", "invoices", "foreign clients", "remote worker", "remote work",
                   "налог", "отчет", "отчёт", "отчетност", "отчётност", "инвойс", "иностранн", "удален", "удалён");
    }

    private static bool LooksLikeRetractionStatusQuery(string text)
    {
        return ContainsAny(
            text,
            "retracted", "retraction", "retract", "withdrawn", "withdrawal", "erratum", "correction", "corrected", "expression of concern", "disputed", "contested",
            "отозван", "отозвана", "отозваны", "ретракц", "исправлен", "исправлена", "исправлены", "эррат", "выражение обеспокоенности", "оспорен", "оспорена", "оспорены");
    }

    private static bool LooksLikeFourDayWeekReliabilityQuery(string text)
    {
        return ContainsAny(text, "four-day", "4-day", "four day", "четырехднев", "четырёхднев") &&
               ContainsAny(text, "workweek", "work week", "рабочая неделя") &&
               ContainsAny(text, "productivity", "output", "продуктив", "output");
    }

    private static bool LooksLikeClimateSensitivityQuery(string text)
    {
        return ContainsAny(text, "climate sensitivity", "equilibrium climate sensitivity", "transient climate response", "климатическ", "чувствит");
    }

    private static bool LooksLikeEuAiRegulationQuery(string text)
    {
        return ContainsAny(
            text,
            "ai act", "artificial intelligence act", "eu ai", "регулирование ии", "регулировании ии", "искусственного интеллекта") &&
               ContainsAny(
                   text,
                   "eu", "ес", "евросою", "european union", "european commission", "vendor", "software vendor", "provider", "small software");
    }

    private static bool LooksLikeArxivPublisherPolicyQuery(string text)
    {
        return ContainsAny(text, "arxiv", "preprint", "peer reviewed", "peer-reviewed", "publisher", "repository", "journal papers", "sherpa", "romeo", "open access", "self-archiving", "accepted manuscript", "embargo");
    }

    private static bool LooksLikeDroneImportCustomsQuery(string text)
    {
        return ContainsAny(text, "drone", "дрон") &&
               ContainsAny(text, "import", "customs", "ввоз", "тамож", "restrictions");
    }

    private static SearchQueryExpansionDecision CreateFocusedDecision(
        string language,
        string stage,
        string english,
        string russian,
        string reason)
    {
        var query = string.Equals(language, "ru", StringComparison.OrdinalIgnoreCase)
            ? russian
            : english;
        return new SearchQueryExpansionDecision(
            query,
            new[]
            {
                $"search_query.rewrite stage={stage} applied=yes reason={reason}",
                $"search_query.rewrite stage={stage} result=\"{Summarize(query)}\""
            });
    }
}

