using Helper.Runtime.WebResearch.Ranking;

namespace Helper.Runtime.WebResearch;

public sealed class SearchEvidenceSufficiencyPolicy : ISearchEvidenceSufficiencyPolicy
{
    public WebEvidenceSufficiencyDecision Evaluate(
        WebSearchRequest request,
        IReadOnlyList<WebSearchPlan> executedPlans,
        IReadOnlyList<WebSearchDocument> aggregateDocuments)
    {
        var liveDocuments = aggregateDocuments.Where(static document => !document.IsFallback).ToArray();
        var liveResultCount = liveDocuments.Length;
        var extractedLiveCount = liveDocuments.Count(static document => document.ExtractedPage is not null);
        var distinctDomainCount = liveDocuments
            .Select(static document => TryGetHost(document.Url))
            .Where(static host => !string.IsNullOrWhiteSpace(host))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        if (liveResultCount == 0)
        {
            return new WebEvidenceSufficiencyDecision(false, "no_live_results", distinctDomainCount, liveResultCount);
        }

        var text = request.Query ?? string.Empty;
        var queryProfile = SourceAuthorityScorer.BuildQueryProfile(text, null);
        var intentProfile = SearchQueryIntentProfileClassifier.Classify(text, queryProfile);
        var paperAnalysisLike = intentProfile.PaperAnalysisLike ||
                                SearchQuerySignalLexicon.ContainsAny(text, SearchQuerySignalLexicon.PaperTokens);
        var evidenceSensitive = queryProfile.EvidenceHeavy || queryProfile.MedicalEvidenceHeavy;
        var hasFreshnessNeed = intentProfile.FreshnessSensitive;
        var hasComparisonNeed = intentProfile.ComparisonSensitive;
        var hasContradictionNeed = intentProfile.ContradictionSensitive;
        var hasFreshnessIteration = executedPlans.Any(static plan => plan.QueryKind.Equals("freshness", StringComparison.OrdinalIgnoreCase));
        var hasNarrowIteration = executedPlans.Any(static plan => plan.QueryKind.Equals("narrow", StringComparison.OrdinalIgnoreCase));
        var hasContradictionIteration = executedPlans.Any(static plan => plan.QueryKind.Equals("contradiction", StringComparison.OrdinalIgnoreCase));
        var hasEvidenceIteration = executedPlans.Any(static plan => plan.QueryKind.Equals("evidence", StringComparison.OrdinalIgnoreCase));
        var hasStepBackIteration = executedPlans.Any(static plan => plan.QueryKind.Equals("step_back", StringComparison.OrdinalIgnoreCase));
        var hasOfficialIteration = executedPlans.Any(static plan => plan.QueryKind.Equals("official", StringComparison.OrdinalIgnoreCase));
        var hasPaperIteration = executedPlans.Any(static plan => plan.QueryKind.Equals("paper_focus", StringComparison.OrdinalIgnoreCase));
        var tokenCount = CountTokens(text);
        var minimumSourceFloor = ResolveMinimumSourceFloor(intentProfile, queryProfile, paperAnalysisLike);
        var meetsSourceFloor = liveResultCount >= minimumSourceFloor;

        if (paperAnalysisLike)
        {
            var enough = liveResultCount >= 2 &&
                         distinctDomainCount >= 1 &&
                         (hasPaperIteration || hasOfficialIteration || extractedLiveCount >= 1);
            return new WebEvidenceSufficiencyDecision(
                enough,
                enough ? "paper_focus_covered" : "need_paper_focus_query",
                distinctDomainCount,
                liveResultCount);
        }

        if (hasComparisonNeed || hasContradictionNeed)
        {
            var requiresMedicalVerification = hasContradictionNeed && queryProfile.MedicalEvidenceHeavy;
            var enough = requiresMedicalVerification
                ? liveResultCount >= 3 &&
                  distinctDomainCount >= 2 &&
                  executedPlans.Count >= 3 &&
                  (hasEvidenceIteration || hasContradictionIteration)
                : liveResultCount >= 2 &&
                  distinctDomainCount >= 2 &&
                  (!hasContradictionNeed || hasContradictionIteration || executedPlans.Count >= 2);
            return new WebEvidenceSufficiencyDecision(
                enough,
                enough
                    ? requiresMedicalVerification ? "comparative_evidence_coverage" : "comparative_coverage"
                    : requiresMedicalVerification ? "need_medical_evidence_reconciliation" : "need_cross_source_comparison",
                distinctDomainCount,
                liveResultCount);
        }

        var genericExpansionPrompt =
            (intentProfile.BroadPromptLike || intentProfile.AmbiguousPromptLike) &&
            !evidenceSensitive &&
            !hasFreshnessNeed &&
            !queryProfile.OfficialBias;

        if (genericExpansionPrompt)
        {
            var enough = liveResultCount >= 2 ||
                         hasStepBackIteration ||
                         hasOfficialIteration ||
                         hasNarrowIteration;
            return new WebEvidenceSufficiencyDecision(
                enough,
                enough ? "query_expansion_covered" : "need_query_expansion",
                distinctDomainCount,
                liveResultCount);
        }

        if (hasFreshnessNeed)
        {
            var requiresStructuredFollowUp = queryProfile.MedicalEvidenceHeavy ||
                                            evidenceSensitive ||
                                            paperAnalysisLike ||
                                            queryProfile.OfficialBias;
            var hasStructuredFollowUp = queryProfile.MedicalEvidenceHeavy || evidenceSensitive
                ? hasEvidenceIteration || hasOfficialIteration
                : paperAnalysisLike
                    ? hasPaperIteration || hasOfficialIteration
                    : hasFreshnessIteration || hasOfficialIteration;
            var enough = requiresStructuredFollowUp
                ? meetsSourceFloor && hasStructuredFollowUp
                : (liveResultCount >= 2 && distinctDomainCount >= 1) ||
                  (liveResultCount >= 1 && (hasFreshnessIteration || hasOfficialIteration));
            return new WebEvidenceSufficiencyDecision(
                enough,
                enough ? "freshness_covered" : "need_freshness_query",
                distinctDomainCount,
                liveResultCount);
        }

        if (evidenceSensitive)
        {
            var enough = liveResultCount >= 2 &&
                         distinctDomainCount >= 2 &&
                         (hasEvidenceIteration || hasOfficialIteration);
            return new WebEvidenceSufficiencyDecision(
                enough,
                enough ? "evidence_coverage" : "need_evidence_query",
                distinctDomainCount,
                liveResultCount);
        }

        if (tokenCount >= 10)
        {
            var enough = liveResultCount >= 2 || hasNarrowIteration;
            return new WebEvidenceSufficiencyDecision(
                enough,
                enough ? "complex_query_covered" : "need_narrow_query",
                distinctDomainCount,
                liveResultCount);
        }

        return new WebEvidenceSufficiencyDecision(
            true,
            "basic_coverage",
            distinctDomainCount,
            liveResultCount);
    }

    private static int CountTokens(string text)
    {
        return string.IsNullOrWhiteSpace(text)
            ? 0
            : text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;
    }

    private static int ResolveMinimumSourceFloor(
        SearchQueryIntentProfile intentProfile,
        SearchRankingQueryProfile queryProfile,
        bool paperAnalysisLike)
    {
        if (queryProfile.MedicalEvidenceHeavy &&
            (intentProfile.ComparisonSensitive || intentProfile.ContradictionSensitive))
        {
            return 3;
        }

        if (paperAnalysisLike ||
            queryProfile.EvidenceHeavy ||
            intentProfile.FreshnessSensitive ||
            queryProfile.OfficialBias ||
            intentProfile.BroadPromptLike ||
            intentProfile.AmbiguousPromptLike)
        {
            return 2;
        }

        return 1;
    }

    private static string? TryGetHost(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : null;
    }
}

