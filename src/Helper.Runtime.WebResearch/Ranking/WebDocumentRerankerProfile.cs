using Helper.Runtime.WebResearch.Extraction;

namespace Helper.Runtime.WebResearch.Ranking;

internal sealed record WebDocumentRerankerProfile(
    string Name,
    string DomainAuthorityProfileName,
    bool PreferFreshnessSignals,
    bool PreferEvidenceSources,
    bool PreferPaperSources,
    bool PreferOfficialSources,
    bool PreferSourceDiversity,
    double MinimumSemanticFit,
    int TargetDistinctHosts,
    int MaxPerHost);

internal static class WebDocumentRerankerProfileResolver
{
    public static WebDocumentRerankerProfile Resolve(string? requestQuery, WebSearchPlan plan)
    {
        var query = string.IsNullOrWhiteSpace(requestQuery) ? plan.Query : requestQuery!;
        var queryProfile = SourceAuthorityScorer.BuildQueryProfile(query, plan.QueryKind);
        var intentProfile = SearchQueryIntentProfileClassifier.Classify(query, queryProfile);
        var authorityProfile = DomainAuthorityProfileResolver.Resolve(query, plan);

        if (intentProfile.PaperAnalysisLike)
        {
            return new WebDocumentRerankerProfile(
                Name: "paper_analysis",
                DomainAuthorityProfileName: authorityProfile.Name == "default" ? "science_reference" : authorityProfile.Name,
                PreferFreshnessSignals: false,
                PreferEvidenceSources: true,
                PreferPaperSources: true,
                PreferOfficialSources: false,
                PreferSourceDiversity: true,
                MinimumSemanticFit: 0.10d,
                TargetDistinctHosts: 3,
                MaxPerHost: 1);
        }

        if (queryProfile.MedicalEvidenceHeavy)
        {
            return new WebDocumentRerankerProfile(
                Name: "medical_evidence",
                DomainAuthorityProfileName: authorityProfile.Name,
                PreferFreshnessSignals: intentProfile.FreshnessSensitive,
                PreferEvidenceSources: true,
                PreferPaperSources: false,
                PreferOfficialSources: true,
                PreferSourceDiversity: true,
                MinimumSemanticFit: 0.12d,
                TargetDistinctHosts: 3,
                MaxPerHost: 1);
        }

        if (intentProfile.ComparisonSensitive || intentProfile.ContradictionSensitive)
        {
            return new WebDocumentRerankerProfile(
                Name: "contrastive_review",
                DomainAuthorityProfileName: authorityProfile.Name,
                PreferFreshnessSignals: intentProfile.FreshnessSensitive,
                PreferEvidenceSources: true,
                PreferPaperSources: false,
                PreferOfficialSources: true,
                PreferSourceDiversity: true,
                MinimumSemanticFit: 0.10d,
                TargetDistinctHosts: 3,
                MaxPerHost: 1);
        }

        if (intentProfile.FreshnessSensitive)
        {
            return new WebDocumentRerankerProfile(
                Name: "factual_freshness",
                DomainAuthorityProfileName: authorityProfile.Name,
                PreferFreshnessSignals: true,
                PreferEvidenceSources: false,
                PreferPaperSources: false,
                PreferOfficialSources: true,
                PreferSourceDiversity: true,
                MinimumSemanticFit: 0.08d,
                TargetDistinctHosts: 3,
                MaxPerHost: 2);
        }

        if (queryProfile.DocumentationHeavy || queryProfile.OfficialBias)
        {
            return new WebDocumentRerankerProfile(
                Name: "reference_lookup",
                DomainAuthorityProfileName: authorityProfile.Name,
                PreferFreshnessSignals: false,
                PreferEvidenceSources: false,
                PreferPaperSources: false,
                PreferOfficialSources: true,
                PreferSourceDiversity: false,
                MinimumSemanticFit: 0.08d,
                TargetDistinctHosts: 2,
                MaxPerHost: 2);
        }

        return new WebDocumentRerankerProfile(
            Name: "default_research",
            DomainAuthorityProfileName: authorityProfile.Name,
            PreferFreshnessSignals: false,
            PreferEvidenceSources: false,
            PreferPaperSources: false,
            PreferOfficialSources: false,
            PreferSourceDiversity: false,
            MinimumSemanticFit: 0.08d,
            TargetDistinctHosts: 2,
            MaxPerHost: 2);
    }
}

