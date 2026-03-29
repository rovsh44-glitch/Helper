using Helper.Runtime.WebResearch.Extraction;

namespace Helper.Runtime.WebResearch.Ranking;

internal sealed record WebRerankedDocumentCandidate(
    RankedWebDocumentCandidate Candidate,
    string SourceKind,
    double SemanticFit,
    double AuthorityLift,
    double DomainAuthorityLift,
    double SourceTypeLift,
    double FreshnessLift,
    double EvidenceLift,
    double Penalty,
    double Score,
    int OriginalOrder);

internal static class WebDocumentRerankingCandidateScorer
{
    public static WebRerankedDocumentCandidate Score(
        WebDocumentRerankerProfile profile,
        string? requestQuery,
        RankedWebDocumentCandidate candidate,
        int originalOrder)
    {
        var semanticFit = ComputeSemanticFit(requestQuery, candidate.Document);
        var sourceKind = ResolveSourceKind(candidate.Document);
        var authorityLift = candidate.Authority.Score * 0.12d;
        var domainAuthorityLift = DomainAuthorityRerankingPolicy.ComputeLift(profile.DomainAuthorityProfileName, candidate, sourceKind);
        var sourceTypeLift = ComputeSourceTypeLift(profile, candidate, sourceKind);
        var freshnessLift = ComputeFreshnessLift(profile, candidate);
        var evidenceLift = ComputeEvidenceLift(profile, candidate, sourceKind);
        var penalty = ComputePenalty(profile, candidate, sourceKind, semanticFit);
        var score = Math.Clamp(
            (candidate.FinalScore * 0.46d) +
            (semanticFit * 0.30d) +
            authorityLift +
            domainAuthorityLift +
            sourceTypeLift +
            freshnessLift +
            evidenceLift -
            penalty,
            0d,
            1.5d);

        return new WebRerankedDocumentCandidate(
            candidate,
            sourceKind,
            semanticFit,
            authorityLift,
            domainAuthorityLift,
            sourceTypeLift,
            freshnessLift,
            evidenceLift,
            penalty,
            score,
            originalOrder);
    }

    private static double ComputeSemanticFit(string? requestQuery, WebSearchDocument document)
    {
        var titleFit = SourceAuthorityScorer.ComputeQueryOverlapRatio(requestQuery, document.Title);
        var snippetFit = SourceAuthorityScorer.ComputeQueryOverlapRatio(requestQuery, document.Snippet);
        var urlCorpusFit = SourceAuthorityScorer.ComputeQueryOverlapRatio(requestQuery, BuildUrlCorpus(document.Url));
        return Math.Clamp((titleFit * 0.46d) + (snippetFit * 0.38d) + (urlCorpusFit * 0.16d), 0d, 1d);
    }

    private static string ResolveSourceKind(WebSearchDocument document)
    {
        if (!Uri.TryCreate(document.Url, UriKind.Absolute, out var uri))
        {
            return "unknown";
        }

        var profile = WebSourceTypeClassifier.Classify(uri, uri, document.ExtractedPage?.ContentType);
        return profile.Kind;
    }

    private static double ComputeSourceTypeLift(
        WebDocumentRerankerProfile profile,
        RankedWebDocumentCandidate candidate,
        string sourceKind)
    {
        var lift = profile.Name switch
        {
            "paper_analysis" => sourceKind switch
            {
                "academic_paper" => 0.18d,
                "document_pdf" => 0.16d,
                "official_document" => 0.06d,
                "interactive_shell" => -0.20d,
                _ => 0d
            },
            "medical_evidence" => sourceKind switch
            {
                "clinical_guidance" => 0.16d,
                "academic_paper" => 0.14d,
                "document_pdf" => 0.10d,
                "official_document" => 0.12d,
                "interactive_shell" => -0.20d,
                _ => 0d
            },
            "contrastive_review" => sourceKind switch
            {
                "clinical_guidance" => 0.10d,
                "academic_paper" => 0.08d,
                "document_pdf" => 0.08d,
                "official_document" => 0.12d,
                "interactive_shell" => -0.16d,
                _ => 0d
            },
            "factual_freshness" => sourceKind switch
            {
                "official_document" => 0.10d,
                "interactive_shell" => -0.12d,
                _ => 0d
            },
            "reference_lookup" => sourceKind switch
            {
                "official_document" => 0.14d,
                "document_pdf" => 0.08d,
                "clinical_guidance" => 0.06d,
                "academic_paper" => 0.05d,
                "general_article" => -0.04d,
                "interactive_shell" => -0.16d,
                _ => 0d
            },
            _ => sourceKind switch
            {
                "official_document" => 0.04d,
                "clinical_guidance" => 0.04d,
                "academic_paper" => 0.04d,
                "interactive_shell" => -0.12d,
                _ => 0d
            }
        };

        if (profile.Name == "paper_analysis" &&
            candidate.Authority.Label.Equals("source_repository", StringComparison.OrdinalIgnoreCase) &&
            ContainsAny($"{candidate.Document.Title} {candidate.Document.Snippet}", "pdf", "paper", "abstract", "arxiv"))
        {
            lift += 0.04d;
        }

        if (profile.Name == "medical_evidence" &&
            candidate.Authority.Reasons.Contains("primary_medical_research_match", StringComparer.OrdinalIgnoreCase))
        {
            lift += 0.10d;
        }

        return lift;
    }

    private static double ComputeFreshnessLift(WebDocumentRerankerProfile profile, RankedWebDocumentCandidate candidate)
    {
        if (!profile.PreferFreshnessSignals)
        {
            return 0d;
        }

        var corpus = $"{candidate.Document.Title} {candidate.Document.Snippet} {candidate.Document.Url}";
        if (!LooksFresh(corpus))
        {
            return 0d;
        }

        return candidate.Authority.IsAuthoritative ||
               candidate.Authority.Label is "major_newswire" or "major_news" or "major_business_news" or "multilateral_news"
            ? 0.10d
            : 0.04d;
    }

    private static double ComputeEvidenceLift(
        WebDocumentRerankerProfile profile,
        RankedWebDocumentCandidate candidate,
        string sourceKind)
    {
        if (!profile.PreferEvidenceSources)
        {
            return 0d;
        }

        var corpus = $"{candidate.Document.Title} {candidate.Document.Snippet} {candidate.Document.Url}";
        var lift = 0d;

        if (candidate.Authority.IsAuthoritative)
        {
            lift += 0.04d;
        }

        if (ContainsAny(
                corpus,
                "systematic review", "meta-analysis", "guideline", "guidelines", "recommendation", "fact sheet",
                "клиничес", "рекомендац", "мета-анализ", "систематичес", "официальные меры", "официальные рекомендации"))
        {
            lift += 0.08d;
        }

        if (profile.Name == "contrastive_review" &&
            sourceKind is "clinical_guidance" or "official_document" or "academic_paper" or "document_pdf")
        {
            lift += 0.04d;
        }

        return lift;
    }

    private static double ComputePenalty(
        WebDocumentRerankerProfile profile,
        RankedWebDocumentCandidate candidate,
        string sourceKind,
        double semanticFit)
    {
        var penalty = 0d;
        var domainProfile = DomainAuthorityProfileResolver.ResolveByName(profile.DomainAuthorityProfileName);

        if (semanticFit < profile.MinimumSemanticFit &&
            !candidate.Authority.IsAuthoritative &&
            candidate.Authority.Score < 0.74d)
        {
            penalty += 0.18d;
        }

        if (profile.Name is "medical_evidence" or "contrastive_review" &&
            candidate.Authority.Label is "health_media_portal" or "fitness_blog" or "commercial_health_vendor" or "standard")
        {
            penalty += candidate.Authority.Label.Equals("commercial_health_vendor", StringComparison.OrdinalIgnoreCase)
                ? 0.22d
                : 0.14d;
        }

        if (profile.DomainAuthorityProfileName is "medical_evidence" or "medical_conflict")
        {
            var hasMedicalAuthoritySignal =
                DomainAuthoritySelectionSupport.Matches(candidate.Authority.Label, domainProfile.PreferredLabels) ||
                candidate.Authority.Reasons.Any(reason => DomainAuthoritySelectionSupport.Matches(reason, domainProfile.StrongReasonMarkers));
            if (!hasMedicalAuthoritySignal && semanticFit < 0.26d)
            {
                penalty += 0.20d;
            }

            if (!hasMedicalAuthoritySignal &&
                ContainsAny(
                    $"{candidate.Document.Url} {candidate.Document.Title} {candidate.Document.Snippet}",
                    "chatgpt", "google play", "app store", "/store/apps/details", "/index/chatgpt", "/shop", "/store", "/product", "buy", "купить", "цена"))
            {
                penalty += 0.24d;
            }

            if (candidate.Authority.Label.Equals("commercial_health_vendor", StringComparison.OrdinalIgnoreCase))
            {
                penalty += 0.22d;
            }
        }

        if (profile.Name == "paper_analysis" && sourceKind == "general_article")
        {
            penalty += 0.08d;
        }

        if (sourceKind == "interactive_shell")
        {
            penalty += 0.06d;
        }

        return penalty;
    }

    private static bool LooksFresh(string corpus)
    {
        var currentYear = DateTime.UtcNow.Year.ToString();
        var previousYear = (DateTime.UtcNow.Year - 1).ToString();
        return ContainsAny(
            corpus,
            "latest", "current", "today", "update", "updated", "announced", "release", "news",
            "послед", "текущ", "сегодня", "обновл", "релиз", currentYear, previousYear);
    }

    private static string BuildUrlCorpus(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return string.Empty;
        }

        return $"{Uri.UnescapeDataString(uri.Host)} {Uri.UnescapeDataString(uri.AbsolutePath)}";
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
}

