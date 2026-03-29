namespace Helper.Runtime.WebResearch.Ranking;

internal sealed record SpamSeoAssessment(
    double Penalty,
    bool LowTrust,
    IReadOnlyList<string> Reasons);

internal interface ISpamAndSeoDemotionPolicy
{
    SpamSeoAssessment Evaluate(WebSearchPlan plan, WebSearchDocument document);
}

internal sealed class SpamAndSeoDemotionPolicy : ISpamAndSeoDemotionPolicy
{
    public SpamSeoAssessment Evaluate(WebSearchPlan plan, WebSearchDocument document)
    {
        if (!Uri.TryCreate(document.Url, UriKind.Absolute, out var uri))
        {
            return new SpamSeoAssessment(0.25d, LowTrust: true, new[] { "invalid_uri" });
        }

        var queryProfile = SourceAuthorityScorer.BuildQueryProfile(plan.Query, plan.QueryKind);
        var hostProfile = LowTrustDomainRegistry.Resolve(uri.Host);
        var text = $"{document.Title} {document.Snippet}";
        var penalty = 0d;
        var reasons = new List<string>();

        if (hostProfile.IsLowTrust)
        {
            penalty += 0.42d;
            reasons.Add("low_trust_host");
        }

        if (!string.IsNullOrWhiteSpace(uri.Query) &&
            (uri.Query.Contains("utm_", StringComparison.OrdinalIgnoreCase) ||
             uri.Query.Contains("ref=", StringComparison.OrdinalIgnoreCase) ||
             uri.Query.Contains("aff", StringComparison.OrdinalIgnoreCase)))
        {
            penalty += 0.08d;
            reasons.Add("tracking_query_params");
        }

        if (uri.AbsolutePath.Contains("/tag/", StringComparison.OrdinalIgnoreCase) ||
            uri.AbsolutePath.Contains("/category/", StringComparison.OrdinalIgnoreCase) ||
            uri.AbsolutePath.Contains("/page/", StringComparison.OrdinalIgnoreCase) ||
            uri.AbsolutePath.EndsWith("/search", StringComparison.OrdinalIgnoreCase))
        {
            penalty += 0.10d;
            reasons.Add("index_or_category_page");
        }

        if (ContainsAny(
                text,
                "sponsored", "affiliate", "buy now", "promo code", "coupon", "discount", "casino", "bet",
                "спонсор", "affiliate", "купон", "скидк"))
        {
            penalty += 0.30d;
            reasons.Add("commercial_or_affiliate_language");
        }

        if ((queryProfile.DocumentationHeavy || queryProfile.OfficialBias) &&
            ContainsAny(
                text,
                "top 10", "top ten", "best ", "ultimate guide", "review", "ranking", "ranked",
                "топ 10", "лучш", "обзор", "рейтинг"))
        {
            penalty += 0.22d;
            reasons.Add("clickbait_for_reference_query");
        }

        if (queryProfile.CurrentnessHeavy &&
            ContainsAny(
                text,
                "prediction", "predicted", "rumor", "rumour", "leak", "unconfirmed",
                "прогноз", "слух", "утечк", "неподтверж"))
        {
            penalty += 0.18d;
            reasons.Add("speculative_for_currentness_query");
        }

        if (!queryProfile.LocalityHeavy &&
            ContainsAny(text, "best ", "top rated", "must buy", "editor's choice"))
        {
            penalty += 0.08d;
            reasons.Add("listicle_language");
        }

        var evidenceSensitive = queryProfile.EvidenceHeavy || queryProfile.MedicalEvidenceHeavy;

        if (LooksLikeCommentThread(uri, document))
        {
            penalty += evidenceSensitive ? 0.30d : 0.12d;
            reasons.Add("comment_thread");
        }

        if (LooksLikeInteractiveOrEntertainmentPage(uri, document))
        {
            penalty += evidenceSensitive ? 0.45d : 0.18d;
            reasons.Add("interactive_or_entertainment_page");
        }

        if (LooksLikeQuestionAnswerOrMarketplacePage(uri, document))
        {
            penalty += evidenceSensitive ? 0.34d : 0.14d;
            reasons.Add("ugc_or_marketplace_page");
        }

        if (evidenceSensitive && LooksLikeChatOrAppLandingPage(uri, document))
        {
            penalty += 0.36d;
            reasons.Add("chat_or_app_landing_for_evidence_query");
        }

        if (evidenceSensitive && LooksLikeCommercialProductPage(uri, document))
        {
            penalty += 0.26d;
            reasons.Add("commercial_product_page_for_evidence_query");
        }

        if (queryProfile.MedicalEvidenceHeavy && LooksLikeLifestyleHealthMediaSource(uri, document))
        {
            penalty += 0.22d;
            reasons.Add("lifestyle_health_media_for_medical_query");
        }

        if (evidenceSensitive)
        {
            var overlapRatio = SourceAuthorityScorer.ComputeQueryOverlapRatio(plan.Query, text);
            var strongMedicalEvidenceSource = queryProfile.MedicalEvidenceHeavy && LooksLikeStrongMedicalEvidenceSource(uri, text);
            if (overlapRatio <= 0d && !strongMedicalEvidenceSource)
            {
                penalty += 0.32d;
                reasons.Add("zero_query_overlap_for_evidence_query");
            }
            else if (overlapRatio < 0.18d && !strongMedicalEvidenceSource)
            {
                penalty += 0.18d;
                reasons.Add("weak_query_overlap_for_evidence_query");
            }
        }

        return new SpamSeoAssessment(
            Penalty: Math.Clamp(penalty, 0d, 0.95d),
            LowTrust: hostProfile.IsLowTrust ||
                      reasons.Contains("commercial_or_affiliate_language", StringComparer.OrdinalIgnoreCase) ||
                      (evidenceSensitive &&
                       (reasons.Contains("comment_thread", StringComparer.OrdinalIgnoreCase) ||
                        reasons.Contains("ugc_or_marketplace_page", StringComparer.OrdinalIgnoreCase) ||
                        reasons.Contains("interactive_or_entertainment_page", StringComparer.OrdinalIgnoreCase) ||
                        reasons.Contains("lifestyle_health_media_for_medical_query", StringComparer.OrdinalIgnoreCase))),
            Reasons: reasons);
    }

    private static bool LooksLikeCommentThread(Uri uri, WebSearchDocument document)
    {
        return uri.AbsolutePath.Contains("/comments", StringComparison.OrdinalIgnoreCase) ||
               ContainsAny($"{document.Title} {document.Snippet}", "comments", "комментар", "discussion");
    }

    private static bool LooksLikeInteractiveOrEntertainmentPage(Uri uri, WebSearchDocument document)
    {
        return uri.AbsolutePath.Contains("/games/", StringComparison.OrdinalIgnoreCase) ||
               uri.AbsolutePath.Contains("/video/", StringComparison.OrdinalIgnoreCase) ||
               uri.AbsolutePath.Contains("/movies/", StringComparison.OrdinalIgnoreCase) ||
               uri.Host.StartsWith("translate.", StringComparison.OrdinalIgnoreCase) ||
               ContainsAny(
                   $"{document.Title} {document.Snippet}",
                   "играть онлайн", "video", "видео", "челлендж", "перевод", "словарь");
    }

    private static bool LooksLikeQuestionAnswerOrMarketplacePage(Uri uri, WebSearchDocument document)
    {
        return uri.Host.Contains("otvet.mail.ru", StringComparison.OrdinalIgnoreCase) ||
               uri.Host.Contains("reddit.com", StringComparison.OrdinalIgnoreCase) ||
               uri.Host.Contains("kak-pishetsya.com", StringComparison.OrdinalIgnoreCase) ||
               uri.AbsolutePath.Contains("/q/question/", StringComparison.OrdinalIgnoreCase) ||
               uri.AbsolutePath.Contains("/question/", StringComparison.OrdinalIgnoreCase) ||
               uri.AbsolutePath.Contains("/market", StringComparison.OrdinalIgnoreCase) ||
               uri.AbsolutePath.Contains("/dictionary/", StringComparison.OrdinalIgnoreCase) ||
               uri.AbsolutePath.Contains("/user-query/", StringComparison.OrdinalIgnoreCase) ||
               ContainsAny(
                   $"{document.Title} {document.Snippet}",
                   "question", "q&a", "ответы mail", "ответы на вопросы", "яндекс маркет", "marketplace", "словарь", "reddit", "как пишется", "spelling");
    }

    private static bool LooksLikeChatOrAppLandingPage(Uri uri, WebSearchDocument document)
    {
        return uri.Host.Contains("chatgpt.com", StringComparison.OrdinalIgnoreCase) ||
               (uri.Host.Contains("openai.com", StringComparison.OrdinalIgnoreCase) &&
                uri.AbsolutePath.Contains("/chatgpt", StringComparison.OrdinalIgnoreCase)) ||
               (uri.Host.Contains("play.google.com", StringComparison.OrdinalIgnoreCase) &&
                uri.AbsolutePath.Contains("/store/apps/details", StringComparison.OrdinalIgnoreCase)) ||
               ContainsAny(
                   $"{document.Title} {document.Snippet}",
                   "chatgpt", "google play", "app store", "download the app");
    }

    private static bool LooksLikeCommercialProductPage(Uri uri, WebSearchDocument document)
    {
        return uri.AbsolutePath.Contains("/product", StringComparison.OrdinalIgnoreCase) ||
               uri.AbsolutePath.Contains("/shop", StringComparison.OrdinalIgnoreCase) ||
               uri.AbsolutePath.Contains("/store", StringComparison.OrdinalIgnoreCase) ||
               uri.Host.Contains("theragun", StringComparison.OrdinalIgnoreCase) ||
               uri.Host.Contains("icetribe", StringComparison.OrdinalIgnoreCase) ||
               ContainsAny(
                   $"{document.Title} {document.Snippet}",
                   "купить", "цена", "заказать", "shop", "store", "led-панель", "led panel", "панель красного света");
    }

    private static bool LooksLikeLifestyleHealthMediaSource(Uri uri, WebSearchDocument document)
    {
        return uri.Host.Contains("doctor.rambler.ru", StringComparison.OrdinalIgnoreCase) ||
               uri.Host.Contains("fitstars.ru", StringComparison.OrdinalIgnoreCase) ||
               uri.AbsolutePath.Contains("/blog/", StringComparison.OrdinalIgnoreCase) ||
               ContainsAny(
                   $"{document.Title} {document.Snippet}",
                   "healthy life", "wellness", "fitness", "бестселлер", "здоровый образ жизни", "фитнес");
    }

    private static bool LooksLikeStrongMedicalEvidenceSource(Uri uri, string corpus)
    {
        return uri.Host.Contains("pubmed.ncbi.nlm.nih.gov", StringComparison.OrdinalIgnoreCase) ||
               uri.Host.Contains("pmc.ncbi.nlm.nih.gov", StringComparison.OrdinalIgnoreCase) ||
               uri.Host.Contains("cochrane.org", StringComparison.OrdinalIgnoreCase) ||
               ((uri.Host.Contains("link.springer.com", StringComparison.OrdinalIgnoreCase) &&
                 uri.AbsolutePath.Contains("/article/", StringComparison.OrdinalIgnoreCase)) ||
                (uri.Host.Contains("springermedicine.com", StringComparison.OrdinalIgnoreCase)) ||
                (uri.Host.Contains("sciencedirect.com", StringComparison.OrdinalIgnoreCase) &&
                 uri.AbsolutePath.Contains("/science/article/", StringComparison.OrdinalIgnoreCase))) ||
               uri.Host.Contains("who.int", StringComparison.OrdinalIgnoreCase) ||
               corpus.Contains("systematic review", StringComparison.OrdinalIgnoreCase) ||
               corpus.Contains("meta-analysis", StringComparison.OrdinalIgnoreCase) ||
               corpus.Contains("guideline", StringComparison.OrdinalIgnoreCase) ||
               corpus.Contains("клиничес", StringComparison.OrdinalIgnoreCase) ||
               corpus.Contains("рекомендац", StringComparison.OrdinalIgnoreCase);
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

