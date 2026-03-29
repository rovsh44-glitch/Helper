namespace Helper.Api.Conversation;

public sealed class EvidenceGradingService : IEvidenceGradingService
{
    public string Grade(double score, bool hasSource, bool contradictionDetected = false, double confidence = 0)
    {
        if (!hasSource)
        {
            return "none";
        }

        if (contradictionDetected)
        {
            return "contradicted";
        }

        var calibrated = Math.Max(score, confidence);
        return calibrated switch
        {
            >= 0.55 => "strong",
            >= 0.25 => "medium",
            _ => "weak"
        };
    }

    public IReadOnlyList<string> BuildUncertaintyFlags(IReadOnlyList<ClaimGrounding> groundedClaims, int totalFactualClaims, int verifiedClaims)
    {
        var flags = new List<string>();
        if (totalFactualClaims == 0)
        {
            flags.Add("uncertainty.no_factual_claims");
            return flags;
        }

        if (verifiedClaims == 0)
        {
            flags.Add("uncertainty.no_verified_claims");
        }
        else if ((double)verifiedClaims / totalFactualClaims < 0.70)
        {
            flags.Add("uncertainty.low_coverage");
        }

        var weakCount = groundedClaims.Count(x => string.Equals(x.EvidenceGrade, "weak", StringComparison.OrdinalIgnoreCase));
        var noneCount = groundedClaims.Count(x => string.Equals(x.EvidenceGrade, "none", StringComparison.OrdinalIgnoreCase));
        var contradictedCount = groundedClaims.Count(x => x.ContradictionDetected || string.Equals(x.EvidenceGrade, "contradicted", StringComparison.OrdinalIgnoreCase));
        if (noneCount > 0)
        {
            flags.Add("uncertainty.evidence_none");
        }

        if (weakCount > 0)
        {
            flags.Add("uncertainty.evidence_weak");
        }

        if (contradictedCount > 0)
        {
            flags.Add("uncertainty.contradiction_detected");
        }

        if (groundedClaims.Any(x => string.Equals(x.EvidenceGrade, "strong", StringComparison.OrdinalIgnoreCase)) &&
            (weakCount > 0 || noneCount > 0))
        {
            flags.Add("uncertainty.evidence_mixed");
        }

        if (groundedClaims.Count > 0)
        {
            var avgConfidence = groundedClaims.Average(x => x.MatchConfidence);
            if (avgConfidence < 0.45)
            {
                flags.Add("uncertainty.low_match_confidence");
            }
        }

        return flags;
    }
}

