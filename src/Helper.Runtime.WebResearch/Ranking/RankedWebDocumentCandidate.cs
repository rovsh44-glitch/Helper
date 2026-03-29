namespace Helper.Runtime.WebResearch.Ranking;

internal sealed record RankedWebDocumentCandidate(
    WebSearchDocument Document,
    SourceAuthorityAssessment Authority,
    SpamSeoAssessment Spam,
    double FinalScore);

