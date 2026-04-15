using Helper.Runtime.WebResearch.Ranking;

namespace Helper.Runtime.WebResearch;

public sealed class SearchIterationPolicy : ISearchIterationPolicy
{
    public SearchIterationBudget Resolve(WebSearchRequest request)
    {
        var text = (request.Query ?? string.Empty).Trim();
        var tokenCount = CountTokens(text);
        var configuredCap = ReadConfiguredCap();
        var maxIterations = 1;
        var reasons = new List<string>();
        var queryProfile = SourceAuthorityScorer.BuildQueryProfile(text, null);
        var intentProfile = SearchQueryIntentProfileClassifier.Classify(text, queryProfile);
        var requestProfile = ResearchRequestProfileResolver.From(text);

        if (request.Depth >= 2 ||
            intentProfile.FreshnessSensitive ||
            intentProfile.PaperAnalysisLike ||
            intentProfile.BroadPromptLike ||
            intentProfile.AmbiguousPromptLike ||
            tokenCount >= 10 ||
            queryProfile.EvidenceHeavy)
        {
            maxIterations = 2;
            reasons.Add(
                request.Depth >= 2 ? "depth" :
                intentProfile.PaperAnalysisLike ? "paper_analysis" :
                intentProfile.BroadPromptLike ? "broad_prompt" :
                intentProfile.AmbiguousPromptLike ? "ambiguous_prompt" :
                queryProfile.EvidenceHeavy ? "evidence_sensitive" :
                tokenCount >= 10 ? "long_query" :
                "freshness_sensitive");
        }

        if (request.Depth >= 3 ||
            intentProfile.ComparisonSensitive ||
            intentProfile.ContradictionSensitive ||
            requestProfile.StrictLiveEvidenceRequired ||
            LooksLikeEuAiRegulationQuery(text) ||
            LooksLikeDroneImportCustomsQuery(text) ||
            LooksLikeArxivPublisherPolicyQuery(text) ||
            LooksLikeRetractionStatusQuery(text) ||
            queryProfile.RegulationFreshnessHeavy ||
            (intentProfile.PaperAnalysisLike && intentProfile.FreshnessSensitive) ||
            (intentProfile.PaperAnalysisLike && queryProfile.OfficialBias) ||
            (intentProfile.BroadPromptLike && queryProfile.OfficialBias) ||
            queryProfile.MedicalEvidenceHeavy)
        {
            maxIterations = 3;
            reasons.Add(
                request.Depth >= 3 ? "deep_request" :
                intentProfile.ContradictionSensitive ? "contradiction_sensitive" :
                requestProfile.StrictLiveEvidenceRequired ? "strict_live_evidence" :
                LooksLikeEuAiRegulationQuery(text) ? "eu_ai_regulation_multibranch" :
                LooksLikeDroneImportCustomsQuery(text) ? "drone_customs_multibranch" :
                LooksLikeArxivPublisherPolicyQuery(text) ? "publisher_policy_multibranch" :
                LooksLikeRetractionStatusQuery(text) ? "retraction_status_multibranch" :
                queryProfile.RegulationFreshnessHeavy ? "regulation_freshness_multibranch" :
                (intentProfile.PaperAnalysisLike && intentProfile.FreshnessSensitive) ? "paper_freshness_multibranch" :
                (intentProfile.PaperAnalysisLike && queryProfile.OfficialBias) ? "paper_multi_branch" :
                (intentProfile.BroadPromptLike && queryProfile.OfficialBias) ? "broad_multi_branch" :
                queryProfile.MedicalEvidenceHeavy ? "medical_evidence_query" :
                "comparative_prompt");
        }

        maxIterations = Math.Clamp(Math.Min(maxIterations, configuredCap), 1, 3);
        var reason = reasons.Count == 0 ? "single_query_default" : string.Join("+", reasons.Distinct(StringComparer.OrdinalIgnoreCase));
        return new SearchIterationBudget(maxIterations, reason);
    }

    private static int ReadConfiguredCap()
    {
        var raw = Environment.GetEnvironmentVariable("HELPER_WEB_SEARCH_MAX_ITERATIONS");
        return int.TryParse(raw, out var parsed)
            ? Math.Clamp(parsed, 1, 3)
            : 3;
    }

    private static int CountTokens(string text)
    {
        return string.IsNullOrWhiteSpace(text)
            ? 0
            : text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;
    }

    private static bool LooksLikeArxivPublisherPolicyQuery(string text)
    {
        return text.Contains("arxiv", StringComparison.OrdinalIgnoreCase) &&
               (text.Contains("publisher", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("journal", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("repository", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("sherpa", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("romeo", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("open access", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("self-archiving", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("accepted manuscript", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("embargo", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("издател", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("репозитор", StringComparison.OrdinalIgnoreCase));
    }

    private static bool LooksLikeRetractionStatusQuery(string text)
    {
        return text.Contains("retraction", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("retracted", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("withdrawn", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("erratum", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("correction", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("expression of concern", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("crossmark", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("отозван", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("ретракц", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("исправлен", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("оспор", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeEuAiRegulationQuery(string text)
    {
        return (text.Contains("ai act", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("artificial intelligence act", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("регулирование ии", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("регулировании ии", StringComparison.OrdinalIgnoreCase)) &&
               (text.Contains("eu", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("ес", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("евросою", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("европейск", StringComparison.OrdinalIgnoreCase));
    }

    private static bool LooksLikeDroneImportCustomsQuery(string text)
    {
        return (text.Contains("drone", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("дрон", StringComparison.OrdinalIgnoreCase)) &&
               (text.Contains("import", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("customs", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("ввоз", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("тамож", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("ce marking", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("батар", StringComparison.OrdinalIgnoreCase));
    }
}
