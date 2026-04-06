namespace Helper.Api.Conversation.Epistemic;

public sealed class BehavioralCalibrationPolicy : IBehavioralCalibrationPolicy
{
    private static readonly string[] HighRiskTokens =
    {
        "medical", "medicine", "drug", "dose", "treatment", "diagnosis", "health",
        "legal", "law", "contract", "tax", "finance", "financial", "investment",
        "медиц", "лекар", "доз", "лечен", "диагноз", "здоров", "юрид", "договор",
        "налог", "финанс", "инвест"
    };

    private static readonly string[] FreshnessTokens =
    {
        "latest", "current", "today", "recent", "fresh", "freshness", "breaking",
        "последн", "сегодня", "текущ", "свеж", "новейш", "актуальн"
    };

    public EpistemicRiskSnapshot BuildSnapshot(ChatTurnContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var trace = new List<string>();
        var message = context.Request.Message ?? string.Empty;
        var highRiskDomain = ContainsAny(message, HighRiskTokens);
        var freshnessSensitive = ContainsAny(message, FreshnessTokens) ||
                                 string.Equals(context.ResolvedLiveWebRequirement, "web_required", StringComparison.OrdinalIgnoreCase);
        var verifiedClaimRatio = context.TotalClaims > 0
            ? (double)context.VerifiedClaims / context.TotalClaims
            : 0.0;
        var groundingStatus = context.GroundingStatus ?? "unknown";
        var hasContradictions = string.Equals(groundingStatus, "grounded_with_contradictions", StringComparison.OrdinalIgnoreCase) ||
                                context.UncertaintyFlags.Contains("uncertainty.contradiction_detected");
        var hasWeakEvidence = string.Equals(groundingStatus, "grounded_with_limits", StringComparison.OrdinalIgnoreCase) ||
                              string.Equals(groundingStatus, "unverified", StringComparison.OrdinalIgnoreCase) ||
                              context.UncertaintyFlags.Contains("uncertainty.evidence_weak") ||
                              context.UncertaintyFlags.Contains("uncertainty.search_hit_only_evidence") ||
                              context.UncertaintyFlags.Contains("uncertainty.source_url_only_evidence") ||
                              context.UncertaintyFlags.Contains("factual_without_sources") ||
                              context.CitationCoverage < 0.70;

        var calibrationThreshold = ResolveThreshold(context.IsFactualPrompt, highRiskDomain, freshnessSensitive, hasWeakEvidence, hasContradictions);
        var confidenceCeiling = ResolveConfidenceCeiling(context, highRiskDomain, freshnessSensitive, hasWeakEvidence, hasContradictions);
        var abstentionRecommended = context.IsFactualPrompt &&
                                    ((context.VerifiedClaims == 0 && (highRiskDomain || freshnessSensitive)) ||
                                     (hasContradictions && highRiskDomain) ||
                                     (context.VerifiedClaims == 0 && string.Equals(groundingStatus, "unverified", StringComparison.OrdinalIgnoreCase)));

        trace.Add($"epistemic.high_risk={highRiskDomain}");
        trace.Add($"epistemic.freshness_sensitive={freshnessSensitive}");
        trace.Add($"epistemic.grounding_status={groundingStatus}");
        trace.Add($"epistemic.verified_claim_ratio={verifiedClaimRatio:0.00}");
        trace.Add($"epistemic.calibration_threshold={calibrationThreshold:0.00}");
        trace.Add($"epistemic.confidence_ceiling={confidenceCeiling:0.00}");
        if (abstentionRecommended)
        {
            trace.Add("epistemic.abstention_recommended=true");
        }

        return new EpistemicRiskSnapshot(
            groundingStatus,
            context.CitationCoverage,
            verifiedClaimRatio,
            hasContradictions,
            hasWeakEvidence,
            highRiskDomain,
            freshnessSensitive,
            context.Confidence,
            confidenceCeiling,
            calibrationThreshold,
            abstentionRecommended,
            trace);
    }

    private static double ResolveThreshold(
        bool factualPrompt,
        bool highRiskDomain,
        bool freshnessSensitive,
        bool hasWeakEvidence,
        bool hasContradictions)
    {
        if (!factualPrompt)
        {
            return 0.45;
        }

        if (hasContradictions)
        {
            return 0.82;
        }

        if (highRiskDomain || freshnessSensitive)
        {
            return hasWeakEvidence ? 0.78 : 0.72;
        }

        return hasWeakEvidence ? 0.66 : 0.58;
    }

    private static double ResolveConfidenceCeiling(
        ChatTurnContext context,
        bool highRiskDomain,
        bool freshnessSensitive,
        bool hasWeakEvidence,
        bool hasContradictions)
    {
        var ceiling = context.IsFactualPrompt ? 0.82 : 0.92;
        if (highRiskDomain || freshnessSensitive)
        {
            ceiling = Math.Min(ceiling, 0.68);
        }

        if (hasWeakEvidence)
        {
            ceiling = Math.Min(ceiling, 0.58);
        }

        if (hasContradictions)
        {
            ceiling = Math.Min(ceiling, 0.42);
        }

        if (context.BudgetExceeded)
        {
            ceiling = Math.Min(ceiling, 0.52);
        }

        if (context.ForceBestEffort)
        {
            ceiling = Math.Min(ceiling, 0.50);
        }

        return Math.Max(0.25, ceiling);
    }

    private static bool ContainsAny(string text, IReadOnlyList<string> tokens)
    {
        return tokens.Any(token => text.Contains(token, StringComparison.OrdinalIgnoreCase));
    }
}
