namespace Helper.Runtime.WebResearch.Ranking;

internal interface IDomainAuthorityFloorPolicy
{
    IReadOnlyList<RankedWebDocumentCandidate> Apply(
        string? requestQuery,
        WebSearchPlan plan,
        IReadOnlyList<RankedWebDocumentCandidate> rankedDocuments,
        List<string> trace);
}

internal sealed class DomainAuthorityFloorPolicy : IDomainAuthorityFloorPolicy
{
    public IReadOnlyList<RankedWebDocumentCandidate> Apply(
        string? requestQuery,
        WebSearchPlan plan,
        IReadOnlyList<RankedWebDocumentCandidate> rankedDocuments,
        List<string> trace)
    {
        var profile = DomainAuthorityProfileResolver.Resolve(requestQuery, plan);
        if (profile.Name is "default" or "medical_evidence" or "medical_conflict")
        {
            return rankedDocuments;
        }

        return DomainAuthoritySelectionSupport.ApplyProfile(
            profile,
            rankedDocuments,
            trace,
            $"{profile.Name}_authority_floor");
    }
}

