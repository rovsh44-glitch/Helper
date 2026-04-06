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
            (intentProfile.PaperAnalysisLike && intentProfile.FreshnessSensitive) ||
            (intentProfile.PaperAnalysisLike && queryProfile.OfficialBias) ||
            (intentProfile.BroadPromptLike && queryProfile.OfficialBias) ||
            queryProfile.MedicalEvidenceHeavy)
        {
            maxIterations = 3;
            reasons.Add(
                request.Depth >= 3 ? "deep_request" :
                intentProfile.ContradictionSensitive ? "contradiction_sensitive" :
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
}

