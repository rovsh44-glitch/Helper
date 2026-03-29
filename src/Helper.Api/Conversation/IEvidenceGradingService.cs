namespace Helper.Api.Conversation;

public interface IEvidenceGradingService
{
    string Grade(double score, bool hasSource, bool contradictionDetected = false, double confidence = 0);
    IReadOnlyList<string> BuildUncertaintyFlags(IReadOnlyList<ClaimGrounding> groundedClaims, int totalFactualClaims, int verifiedClaims);
}

