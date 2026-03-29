namespace Helper.Runtime.WebResearch.Ranking;

internal interface IMedicalEvidenceFloorPolicy
{
    IReadOnlyList<RankedWebDocumentCandidate> Apply(
        string? requestQuery,
        WebSearchPlan plan,
        IReadOnlyList<RankedWebDocumentCandidate> rankedDocuments,
        List<string> trace);
}

internal sealed class MedicalEvidenceFloorPolicy : IMedicalEvidenceFloorPolicy
{
    public IReadOnlyList<RankedWebDocumentCandidate> Apply(
        string? requestQuery,
        WebSearchPlan plan,
        IReadOnlyList<RankedWebDocumentCandidate> rankedDocuments,
        List<string> trace)
    {
        var profile = DomainAuthorityProfileResolver.Resolve(requestQuery, plan);
        if (profile.Name is not ("medical_evidence" or "medical_conflict"))
        {
            return rankedDocuments;
        }

        return DomainAuthoritySelectionSupport.ApplyProfile(
            profile,
            rankedDocuments,
            trace,
            profile.Name == "medical_conflict"
                ? "medical_conflict_authority_floor"
                : "medical_authority_floor");
    }
}

