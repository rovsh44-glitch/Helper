using System.Text.RegularExpressions;

namespace Helper.Runtime.WebResearch.Ranking;

internal readonly record struct SearchRankingQueryProfile(
    bool DocumentationHeavy,
    bool CurrentnessHeavy,
    bool ComparisonHeavy,
    bool LocalityHeavy,
    bool OfficialBias,
    bool EvidenceHeavy,
    bool MedicalEvidenceHeavy,
    bool RegulationFreshnessHeavy);

internal sealed record SourceAuthorityAssessment(
    double Score,
    string Label,
    bool IsAuthoritative,
    IReadOnlyList<string> Reasons);

internal interface ISourceAuthorityScorer
{
    SourceAuthorityAssessment Evaluate(WebSearchPlan plan, WebSearchDocument document);
}

internal sealed class SourceAuthorityScorer : ISourceAuthorityScorer
{
    private static readonly string[] CyrillicSuffixes =
    {
        "иями", "ями", "ами", "иях", "ого", "ему", "ыми", "ими", "ией", "ов", "ев", "ей", "ах", "ях",
        "ию", "ью", "ия", "ья", "ие", "ые", "ий", "ый", "ой", "ая", "яя", "ое", "ее", "ую", "юю", "ом", "ем", "ам", "ям",
        "ы", "и", "а", "я", "е", "о", "у", "ю", "ь"
    };

    private static readonly string[] LatinSuffixes =
    {
        "ization", "isation", "ations", "ation", "ments", "ment", "ality", "ities", "ously", "ness", "less",
        "ized", "ised", "ative", "ical", "ance", "ence", "tion", "sion", "able", "ible", "ists", "isms", "istic",
        "ing", "ers", "ies", "ied", "ed", "es", "s"
    };

    public SourceAuthorityAssessment Evaluate(WebSearchPlan plan, WebSearchDocument document)
    {
        if (!Uri.TryCreate(document.Url, UriKind.Absolute, out var uri))
        {
            return new SourceAuthorityAssessment(0d, "invalid_uri", IsAuthoritative: false, new[] { "invalid_uri" });
        }

        var queryProfile = BuildQueryProfile(plan.Query, plan.QueryKind);
        var medicalConflictHeavy = queryProfile.MedicalEvidenceHeavy &&
                                   (queryProfile.ComparisonHeavy ||
                                    plan.QueryKind.Equals("evidence", StringComparison.OrdinalIgnoreCase) ||
                                    plan.QueryKind.Equals("contradiction", StringComparison.OrdinalIgnoreCase));
        var trustProfile = LowTrustDomainRegistry.Resolve(uri.Host);
        var reasons = new List<string>();
        var score = 0.48d;

        if (uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            score += 0.04d;
            reasons.Add("https");
        }

        if (trustProfile.AuthorityBoost != 0d)
        {
            score += trustProfile.AuthorityBoost;
            reasons.Add($"host_profile:{trustProfile.Label}");
        }

        if (queryProfile.DocumentationHeavy && LooksLikeReferenceDocument(uri, document))
        {
            score += 0.18d;
            reasons.Add("reference_document_match");
        }

        if (queryProfile.OfficialBias && trustProfile.IsAuthoritative)
        {
            score += 0.10d;
            reasons.Add("official_bias_match");
        }

        if (queryProfile.EvidenceHeavy && LooksLikeEvidenceOrReferenceSource(uri, document))
        {
            score += 0.12d;
            reasons.Add("evidence_source_match");
        }

        if (queryProfile.MedicalEvidenceHeavy && LooksLikeMedicalEvidenceSource(uri, document))
        {
            score += 0.18d;
            reasons.Add("medical_evidence_match");
        }

        if (queryProfile.MedicalEvidenceHeavy && LooksLikePrimaryMedicalResearchSource(uri, document))
        {
            score += 0.12d;
            reasons.Add("primary_medical_research_match");
        }

        if (medicalConflictHeavy && trustProfile.Label is "major_medical_clinic" or "medical_research_index" or "medical_research_fulltext" or "evidence_review" or "clinical_reference" or "clinical_reference_society" or "academic_index")
        {
            score += 0.08d;
            reasons.Add("medical_conflict_authority_match");
        }

        if (queryProfile.CurrentnessHeavy && LooksLikeTimelySource(uri, document))
        {
            score += 0.08d;
            reasons.Add("timely_source_match");
        }

        if (queryProfile.EvidenceHeavy && LooksLikeLowSignalInteractivePage(uri, document))
        {
            score -= 0.18d;
            reasons.Add("interactive_or_ugc_mismatch");
        }

        if (queryProfile.MedicalEvidenceHeavy && LooksLikeLifestyleHealthMediaSource(uri, document))
        {
            score -= medicalConflictHeavy ? 0.24d : 0.14d;
            reasons.Add("lifestyle_health_media_mismatch");
        }

        if (queryProfile.MedicalEvidenceHeavy && LooksLikeMedicalOpinionBlogSource(uri, document))
        {
            score -= medicalConflictHeavy ? 0.24d : 0.16d;
            reasons.Add("medical_opinion_blog_mismatch");
        }

        if (queryProfile.MedicalEvidenceHeavy && LooksLikeSecondaryResearchAggregatorSource(uri, document))
        {
            score -= medicalConflictHeavy ? 0.22d : 0.14d;
            reasons.Add("secondary_research_aggregator_mismatch");
        }

        if (queryProfile.MedicalEvidenceHeavy && LooksLikeCommercialWellnessVendorSource(uri, document))
        {
            score -= medicalConflictHeavy ? 0.28d : 0.20d;
            reasons.Add("commercial_health_vendor_mismatch");
        }

        if (!string.IsNullOrWhiteSpace(uri.Query) && !IsCleanQueryString(uri.Query))
        {
            score -= 0.08d;
            reasons.Add("tracking_params");
        }

        if (LooksLikeIndexOrCategoryPage(uri.AbsolutePath))
        {
            score -= 0.06d;
            reasons.Add("index_or_category_path");
        }

        return new SourceAuthorityAssessment(
            Score: Math.Clamp(score, 0d, 1d),
            Label: trustProfile.Label,
            IsAuthoritative: trustProfile.IsAuthoritative,
            Reasons: reasons);
    }

    internal static SearchRankingQueryProfile BuildQueryProfile(string? query, string? queryKind)
    {
        var text = (query ?? string.Empty).Trim();
        var recoveryTherapyEvidenceHeavy = LooksLikeRecoveryTherapyEvidenceQuery(text);
        var evidenceBoundaryHeavy = LooksLikeEvidenceBoundaryQuery(text);
        var retractionStatusHeavy = LooksLikeRetractionStatusQuery(text);
        var documentationHeavy = ContainsAny(
            text,
            "docs", "documentation", "reference", "api", "sdk", "release notes", "support", "version",
            "документац", "справк", "api", "sdk", "релиз", "версия", "поддержк");
        var currentnessHeavy = ContainsAny(
            text,
            "latest", "current", "today", "right now", "news", "update", "price", "forecast", "score", "schedule", "fresh", "freshness",
            "послед", "текущ", "сегодня", "сейчас", "новост", "обновл", "цена", "погод", "счёт", "распис", "свеж");
        var comparisonHeavy = ContainsAny(
            text,
            "compare", "versus", " vs ", "difference", "сравни", "сопостав", "разниц");
        var localityHeavy = ContainsAny(
            text,
            "near me", "near ", "nearby", "open now", "open today", "local",
            "рядом", "поблизости", "открыто", "открыт сейчас", "локаль");
        var evidenceHeavy = ContainsAny(
            text,
            "guideline", "guidelines", "recommendation", "recommendations", "clinical", "trial", "study",
            "systematic review", "meta-analysis", "consensus", "practice advisory", "public health",
            "клиничес", "рекомендац", "испытан", "исследован", "систематичес", "мета-анализ", "консенсус",
            "профилактик", "лечение", "вакцин", "вспышк", "эпидеми", "официальные меры", "официальные рекомендации",
            "prediabetes", "nutrition", "diet", "meal", "glycemic", "glucose", "insulin", "carbohydrate",
            "преддиаб", "рацион", "питани", "диет", "гликем", "глюкоз", "инсулин", "углевод") ||
            retractionStatusHeavy ||
            evidenceBoundaryHeavy ||
            recoveryTherapyEvidenceHeavy;
        var regulationFreshnessHeavy = LooksLikeRegulationFreshnessQuery(text);
        var medicalEvidenceHeavy = ContainsAny(
            text,
            "migraine", "measles", "vaccine", "vaccination", "outbreak", "disease", "treatment", "prevention",
            "guideline", "clinical", "muscle", "weight loss", "trial", "fasting", "time-restricted eating",
            "prediabetes", "nutrition", "diet", "meal", "glycemic", "glucose", "insulin", "carbohydrate",
            "мигрен", "корь", "вакцин", "вакцинац", "вспышк", "болезн", "лечение", "профилактик", "мышц", "мышеч", "вес", "голодан",
            "преддиаб", "рацион", "питани", "диет", "гликем", "глюкоз", "инсулин", "углевод") ||
            recoveryTherapyEvidenceHeavy;
        currentnessHeavy = currentnessHeavy || LooksLikeTimeBoundYearQuery(text);
        var officialBias = documentationHeavy ||
                           evidenceHeavy ||
                           regulationFreshnessHeavy ||
                           retractionStatusHeavy ||
                           ContainsAny(
                               text,
                               "official", "guidance", "sec", "ftc", "irs", "act", "law", "regulation",
                               "european commission", "ai office", "eur-lex", "easa", "customs",
                               "официаль", "руководств", "закон", "регуляц", "минздрав", "всемирная организация здравоохранения", "who", "тамож");

        if (string.Equals(queryKind, "freshness", StringComparison.OrdinalIgnoreCase))
        {
            currentnessHeavy = true;
            officialBias = true;
        }

        if (string.Equals(queryKind, "publisher_policy", StringComparison.OrdinalIgnoreCase))
        {
            evidenceHeavy = true;
            officialBias = true;
        }

        return new SearchRankingQueryProfile(
            documentationHeavy,
            currentnessHeavy,
            comparisonHeavy,
            localityHeavy,
            officialBias,
            evidenceHeavy,
            medicalEvidenceHeavy,
            regulationFreshnessHeavy);
    }

    internal static double ComputeQueryOverlapRatio(string? query, string? text)
    {
        var queryTokens = TokenizeForRanking(RemoveUrls(query ?? string.Empty));
        if (queryTokens.Count == 0)
        {
            return 0d;
        }

        var textTokens = TokenizeForRanking(text ?? string.Empty);
        if (textTokens.Count == 0)
        {
            return 0d;
        }

        var overlap = CountApproximateMatches(queryTokens, textTokens);
        return overlap / (double)queryTokens.Count;
    }

    internal static IReadOnlySet<string> TokenizeForRanking(string input)
    {
        return input
            .Replace('ё', 'е')
            .ToLowerInvariant()
            .Split(new[] { ' ', '\t', '\r', '\n', '.', ',', ';', ':', '!', '?', '(', ')', '[', ']', '{', '}', '/', '\\', '-', '_', '"', '\'', '«', '»' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeTokenForRanking)
            .Where(static token => token.Length >= 3)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static bool LooksLikeReferenceDocument(Uri uri, WebSearchDocument document)
    {
        return uri.Host.StartsWith("docs.", StringComparison.OrdinalIgnoreCase) ||
               uri.Host.StartsWith("developer.", StringComparison.OrdinalIgnoreCase) ||
               uri.AbsolutePath.Contains("/docs", StringComparison.OrdinalIgnoreCase) ||
               uri.AbsolutePath.Contains("/reference", StringComparison.OrdinalIgnoreCase) ||
               uri.AbsolutePath.Contains("/releases", StringComparison.OrdinalIgnoreCase) ||
               uri.AbsolutePath.Contains("/support", StringComparison.OrdinalIgnoreCase) ||
               ContainsAny(
                   $"{document.Title} {document.Snippet}",
                   "documentation", "api reference", "release notes", "support", "official docs",
                   "документац", "справк", "релиз", "поддержк");
    }

    private static bool LooksLikeTimelySource(Uri uri, WebSearchDocument document)
    {
        return uri.AbsolutePath.Contains("/news", StringComparison.OrdinalIgnoreCase) ||
               uri.AbsolutePath.Contains("/blog", StringComparison.OrdinalIgnoreCase) ||
               uri.AbsolutePath.Contains("/press", StringComparison.OrdinalIgnoreCase) ||
               uri.AbsolutePath.Contains("/release", StringComparison.OrdinalIgnoreCase) ||
               ContainsAny(
                   $"{document.Title} {document.Snippet}",
                   "latest", "today", "current", "announced", "release", "update", "fresh",
                   "послед", "сегодня", "текущ", "обновл", "релиз", "свеж");
    }

    private static bool LooksLikeEvidenceOrReferenceSource(Uri uri, WebSearchDocument document)
    {
        return LooksLikeReferenceDocument(uri, document) ||
               LooksLikeMedicalEvidenceSource(uri, document) ||
               ContainsAny(
                   $"{document.Title} {document.Snippet}",
                   "guideline", "guidelines", "recommendation", "recommendations", "clinical", "consensus", "fact sheet",
                   "клиничес", "рекомендац", "консенсус", "профилактик", "вакцин", "вспышк");
    }

    private static bool LooksLikeMedicalEvidenceSource(Uri uri, WebSearchDocument document)
    {
        return uri.Host.EndsWith(".gov", StringComparison.OrdinalIgnoreCase) ||
               uri.Host.EndsWith(".edu", StringComparison.OrdinalIgnoreCase) ||
               uri.Host.Contains("who.int", StringComparison.OrdinalIgnoreCase) ||
               uri.Host.Contains("nih.gov", StringComparison.OrdinalIgnoreCase) ||
               uri.Host.Contains("cochrane.org", StringComparison.OrdinalIgnoreCase) ||
               uri.Host.Contains("mayoclinic.org", StringComparison.OrdinalIgnoreCase) ||
               uri.Host.Contains("medlineplus.gov", StringComparison.OrdinalIgnoreCase) ||
               uri.Host.Contains("nhs.uk", StringComparison.OrdinalIgnoreCase) ||
               uri.Host.Contains("nice.org.uk", StringComparison.OrdinalIgnoreCase) ||
               LooksLikeAcademicPublisherArticleSource(uri, document) ||
               uri.Host.Contains("consultant.ru", StringComparison.OrdinalIgnoreCase) ||
               uri.Host.Contains("garant.ru", StringComparison.OrdinalIgnoreCase) ||
               uri.Host.Contains("cyberleninka.ru", StringComparison.OrdinalIgnoreCase) ||
               ContainsAny(
                   $"{uri.AbsolutePath} {document.Title} {document.Snippet}",
                   "guideline", "clinical", "fact-sheets", "fact-sheet", "recommendation", "disease", "treatment", "prevention",
                   "clinical-guideline", "guidances", "migraine", "measles", "vaccin",
                   "клиничес", "рекомендац", "лечение", "профилактик", "мигрен", "корь", "вакцин", "вспыш");
    }

    private static bool LooksLikePrimaryMedicalResearchSource(Uri uri, WebSearchDocument document)
    {
        return uri.Host.Contains("pubmed.ncbi.nlm.nih.gov", StringComparison.OrdinalIgnoreCase) ||
               uri.Host.Contains("pmc.ncbi.nlm.nih.gov", StringComparison.OrdinalIgnoreCase) ||
               uri.Host.Contains("cochrane.org", StringComparison.OrdinalIgnoreCase) ||
               LooksLikeAcademicPublisherArticleSource(uri, document) ||
               ContainsAny(
                   $"{uri.AbsolutePath} {document.Title} {document.Snippet}",
                   "systematic review", "meta-analysis", "randomized", "trial", "cohort",
                   "систематичес", "мета-анализ", "рандомизирован", "клиническ", "обзор");
    }

    private static bool LooksLikeAcademicPublisherArticleSource(Uri uri, WebSearchDocument document)
    {
        return ((uri.Host.Contains("link.springer.com", StringComparison.OrdinalIgnoreCase) &&
                 uri.AbsolutePath.Contains("/article/", StringComparison.OrdinalIgnoreCase)) ||
                (uri.Host.Contains("springermedicine.com", StringComparison.OrdinalIgnoreCase) &&
                 uri.AbsolutePath.Contains("/a-", StringComparison.OrdinalIgnoreCase)) ||
                (uri.Host.Contains("sciencedirect.com", StringComparison.OrdinalIgnoreCase) &&
                 uri.AbsolutePath.Contains("/science/article/", StringComparison.OrdinalIgnoreCase))) &&
               ContainsAny(
                   $"{document.Title} {document.Snippet}",
                   "systematic review", "meta-analysis", "review", "randomized", "trial", "recovery", "exercise performance",
                   "систематичес", "мета-анализ", "обзор", "рандомизирован", "восстановлен");
    }

    private static bool LooksLikeLifestyleHealthMediaSource(Uri uri, WebSearchDocument document)
    {
        return uri.Host.Contains("doctor.rambler.ru", StringComparison.OrdinalIgnoreCase) ||
               uri.Host.Contains("fitstars.ru", StringComparison.OrdinalIgnoreCase) ||
               uri.Host.Contains("fitness-pro.ru", StringComparison.OrdinalIgnoreCase) ||
               uri.Host.Contains("physio-pedia.com", StringComparison.OrdinalIgnoreCase) ||
               uri.Host.Contains("glamour.ru", StringComparison.OrdinalIgnoreCase) ||
               uri.Host.Contains("mygenetics.ru", StringComparison.OrdinalIgnoreCase) ||
               uri.Host.Contains("vplaboutlet.by", StringComparison.OrdinalIgnoreCase) ||
               uri.Host.Contains("medaboutme.ru", StringComparison.OrdinalIgnoreCase) ||
               uri.Host.Contains("woman.ru", StringComparison.OrdinalIgnoreCase) ||
               uri.AbsolutePath.Contains("/blog/", StringComparison.OrdinalIgnoreCase) ||
               ContainsAny(
                   $"{document.Title} {document.Snippet}",
                   "lifestyle", "wellness", "healthy life", "fitness", "бестселлер", "здоровый образ жизни", "фитнес", "healthy life");
    }

    private static bool LooksLikeMedicalOpinionBlogSource(Uri uri, WebSearchDocument document)
    {
        return uri.Host.Contains("jeffreypengmd.com", StringComparison.OrdinalIgnoreCase) ||
               uri.AbsolutePath.Contains("/post/", StringComparison.OrdinalIgnoreCase) ||
               uri.AbsolutePath.Contains("/blog/", StringComparison.OrdinalIgnoreCase) ||
               ContainsAny(
                   $"{uri.Host} {uri.AbsolutePath} {document.Title} {document.Snippet}",
                   "does it actually work", "guide", "benefits", "recovery guide", "my take", "expert blog");
    }

    private static bool LooksLikeSecondaryResearchAggregatorSource(Uri uri, WebSearchDocument document)
    {
        return uri.Host.Contains("researchgate.net", StringComparison.OrdinalIgnoreCase) &&
               ContainsAny(
                   $"{document.Title} {document.Snippet}",
                   "systematic review", "meta-analysis", "review", "randomized", "trial", "recovery", "exercise performance",
                   "систематичес", "мета-анализ", "обзор", "рандомизирован", "восстановлен");
    }

    private static bool LooksLikeCommercialWellnessVendorSource(Uri uri, WebSearchDocument document)
    {
        return uri.Host.Contains("lighttherapyhome.com", StringComparison.OrdinalIgnoreCase) ||
               uri.Host.Contains("reddotled.com", StringComparison.OrdinalIgnoreCase) ||
               uri.Host.Contains("theragunrussia.ru", StringComparison.OrdinalIgnoreCase) ||
               uri.Host.Contains("icetribe.ru", StringComparison.OrdinalIgnoreCase) ||
               ContainsAny(
                   $"{uri.AbsolutePath} {document.Title} {document.Snippet}",
                   "shop", "store", "product", "buy", "led-panel", "panel", "recovery guide", "rapid relief", "купить", "панель", "устройство", "каталог");
    }

    private static bool LooksLikeLowSignalInteractivePage(Uri uri, WebSearchDocument document)
    {
        return uri.AbsolutePath.Contains("/comments", StringComparison.OrdinalIgnoreCase) ||
               uri.AbsolutePath.Contains("/games/", StringComparison.OrdinalIgnoreCase) ||
               uri.AbsolutePath.Contains("/video/", StringComparison.OrdinalIgnoreCase) ||
               uri.AbsolutePath.Contains("/market/", StringComparison.OrdinalIgnoreCase) ||
               uri.AbsolutePath.Contains("/q/question/", StringComparison.OrdinalIgnoreCase) ||
               uri.AbsolutePath.Contains("/dictionary/", StringComparison.OrdinalIgnoreCase) ||
               uri.AbsolutePath.Contains("/forum", StringComparison.OrdinalIgnoreCase) ||
               uri.AbsolutePath.Contains("/community", StringComparison.OrdinalIgnoreCase) ||
               uri.Host.StartsWith("translate.", StringComparison.OrdinalIgnoreCase) ||
               ContainsAny(
                   $"{document.Title} {document.Snippet}",
                   "играть онлайн", "словарь", "перевод", "мем", "челлендж", "discussion", "comments", "forum", "community", "buy now");
    }

    private static bool LooksLikeIndexOrCategoryPage(string path)
    {
        return path.Contains("/tag/", StringComparison.OrdinalIgnoreCase) ||
               path.Contains("/category/", StringComparison.OrdinalIgnoreCase) ||
               path.Contains("/archive/", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith("/search", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCleanQueryString(string query)
    {
        return !query.Contains("utm_", StringComparison.OrdinalIgnoreCase) &&
               !query.Contains("ref=", StringComparison.OrdinalIgnoreCase) &&
               !query.Contains("aff", StringComparison.OrdinalIgnoreCase) &&
               !query.Contains("coupon", StringComparison.OrdinalIgnoreCase);
    }

    private static string RemoveUrls(string query)
    {
        return System.Text.RegularExpressions.Regex.Replace(
            query,
            @"https?://[^\s\)\]\}>]+",
            " ",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    private static int CountApproximateMatches(IReadOnlyCollection<string> queryTokens, IReadOnlyCollection<string> textTokens)
    {
        var matches = 0;
        foreach (var queryToken in queryTokens)
        {
            if (textTokens.Any(textToken => TokensMatch(queryToken, textToken)))
            {
                matches++;
            }
        }

        return matches;
    }

    private static bool TokensMatch(string left, string right)
    {
        if (string.Equals(left, right, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (left.Length >= 5 && right.Length >= 5)
        {
            return left.StartsWith(right, StringComparison.OrdinalIgnoreCase) ||
                   right.StartsWith(left, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static string NormalizeTokenForRanking(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return string.Empty;
        }

        var normalized = Uri.UnescapeDataString(token.Trim().ToLowerInvariant()).Replace('ё', 'е');
        if (normalized.All(static ch => ch is >= 'a' and <= 'z'))
        {
            foreach (var suffix in LatinSuffixes)
            {
                if (normalized.Length > suffix.Length + 2 &&
                    normalized.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    normalized = normalized[..^suffix.Length];
                    break;
                }
            }

            return normalized;
        }

        if (normalized.Any(static ch => ch is >= '\u0400' and <= '\u04FF'))
        {
            foreach (var suffix in CyrillicSuffixes)
            {
                if (normalized.Length > suffix.Length + 2 &&
                    normalized.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    normalized = normalized[..^suffix.Length];
                    break;
                }
            }
        }

        return normalized;
    }

    private static bool LooksLikeEvidenceBoundaryQuery(string text)
    {
        return ContainsAny(
            text,
            "how strong is the evidence", "how convincing is the evidence", "convincing evidence", "strength of evidence",
            "evidence base", "data on benefits", "benefit of", "benefits of", "effectiveness",
            "насколько убедитель", "убедительны данные", "сила доказательств", "доказательн", "данные о польз", "эффективност");
    }

    private static bool LooksLikeRecoveryTherapyEvidenceQuery(string text)
    {
        return ContainsAny(
            text,
            "red light", "red-light", "light therapy", "phototherapy", "photobiomodulation", "recovery after training",
            "exercise recovery", "post-workout", "muscle recovery", "recovery after exercise", "therapy for recovery",
            "красн свет", "красным свет", "светотерап", "фототерап", "фотобиомодуляц", "восстановлен", "после трениров", "терапи");
    }

    private static bool LooksLikeRegulationFreshnessQuery(string text)
    {
        return ContainsAny(
            text,
            "tax", "taxes", "threshold", "thresholds", "deadline", "deadlines", "reporting", "filing", "compliance", "regulation",
            "ai act", "artificial intelligence act", "provider obligations", "implementation guidance",
            "customs", "import", "import restrictions", "vat", "duty", "drone", "easa", "battery", "batteries", "ce marking",
            "visa", "visas", "immigration", "migration", "relocation", "residence permit", "work permit", "blue card", "bluecard", "skilled worker",
            "налог", "налоги", "налогов", "порог", "пороги", "лимит", "лимиты", "срок", "сроки", "отчетност", "отчётност", "регуляц",
            "тамож", "ввоз", "дрон", "батаре", "ндс", "пошлин", "маркировк",
            "виза", "визы", "визовые", "миграц", "релокац", "внж", "вид на жительство", "разрешение на работу", "голубая карта");
    }

    private static bool LooksLikeRetractionStatusQuery(string text)
    {
        return ContainsAny(
            text,
            "retracted", "retraction", "retract", "withdrawn", "withdrawal", "erratum", "correction", "corrected", "expression of concern", "disputed", "contested",
            "отозван", "отозвана", "отозваны", "ретракц", "исправлен", "исправлена", "исправлены", "эррат", "выражение обеспокоенности", "оспорен", "оспорена", "оспорены");
    }

    private static bool LooksLikeTimeBoundYearQuery(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var currentYear = DateTime.UtcNow.Year;
        var pattern = $@"\b(?:{currentYear - 1}|{currentYear}|{currentYear + 1}|{currentYear + 2})\b";
        return Regex.IsMatch(text, pattern, RegexOptions.CultureInvariant);
    }

    private static bool ContainsAny(string text, params string[] markers)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        foreach (var marker in markers)
        {
            if (text.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}

