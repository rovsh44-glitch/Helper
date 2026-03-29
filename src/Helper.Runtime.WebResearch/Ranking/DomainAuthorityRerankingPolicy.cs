namespace Helper.Runtime.WebResearch.Ranking;

internal static class DomainAuthorityRerankingPolicy
{
    public static double ComputeLift(string? profileName, RankedWebDocumentCandidate candidate, string sourceKind)
    {
        var profile = DomainAuthorityProfileResolver.ResolveByName(profileName);
        if (profile.Name == "default")
        {
            return 0d;
        }

        var lift = 0d;
        if (DomainAuthoritySelectionSupport.Matches(candidate.Authority.Label, profile.PreferredLabels))
        {
            lift += 0.08d;
        }

        if (candidate.Authority.IsAuthoritative)
        {
            lift += 0.03d;
        }

        if (candidate.Authority.Reasons.Any(reason => DomainAuthoritySelectionSupport.Matches(reason, profile.StrongReasonMarkers)))
        {
            lift += 0.05d;
        }

        if (DomainAuthoritySelectionSupport.Matches(candidate.Authority.Label, profile.WeakLabels))
        {
            lift -= 0.10d;
        }

        lift += profile.Name switch
        {
            "science_reference" when sourceKind is "academic_paper" or "document_pdf" or "official_document" => 0.05d,
            "law_regulation" when sourceKind is "official_document" or "document_pdf" => 0.04d,
            "finance_market" when candidate.Authority.Label is "major_business_news" or "major_newswire" => 0.04d,
            "current_events" when candidate.Authority.Label is "major_newswire" or "multilateral_news" or "major_news" => 0.05d,
            "medical_evidence" or "medical_conflict" when sourceKind is "clinical_guidance" or "academic_paper" or "document_pdf" => 0.04d,
            _ => 0d
        };

        return lift;
    }
}

