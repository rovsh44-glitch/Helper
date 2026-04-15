namespace Helper.Api.Conversation.Epistemic;

public sealed class EpistemicAnswerModePolicy : IEpistemicAnswerModePolicy
{
    public EpistemicAnswerMode Resolve(ChatTurnContext context, EpistemicRiskSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(snapshot);

        if (context.RequiresClarification)
        {
            return EpistemicAnswerMode.NeedsVerification;
        }

        if (!context.IsFactualPrompt)
        {
            return context.ForceBestEffort
                ? EpistemicAnswerMode.BestEffortHypothesis
                : EpistemicAnswerMode.Direct;
        }

        if (snapshot.AbstentionRecommended && snapshot.CurrentConfidence < snapshot.CalibrationThreshold)
        {
            return EpistemicAnswerMode.Abstain;
        }

        if (context.ForceBestEffort && !ShouldSuppressBestEffortForMandatoryLiveWeb(context, snapshot))
        {
            return EpistemicAnswerMode.BestEffortHypothesis;
        }

        if (snapshot.HasContradictions)
        {
            return snapshot.HighRiskDomain
                ? EpistemicAnswerMode.Abstain
                : EpistemicAnswerMode.NeedsVerification;
        }

        if (HasStrongGroundedCoverage(context, snapshot))
        {
            return EpistemicAnswerMode.Grounded;
        }

        if (string.Equals(snapshot.GroundingStatus, "grounded", StringComparison.OrdinalIgnoreCase) &&
            snapshot.VerifiedClaimRatio >= 0.70 &&
            !snapshot.HasWeakEvidence &&
            snapshot.CurrentConfidence >= snapshot.CalibrationThreshold)
        {
            return EpistemicAnswerMode.Grounded;
        }

        if (string.Equals(snapshot.GroundingStatus, "grounded_with_limits", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(snapshot.GroundingStatus, "unverified", StringComparison.OrdinalIgnoreCase) ||
            snapshot.HasWeakEvidence ||
            snapshot.CurrentConfidence < snapshot.CalibrationThreshold)
        {
            return snapshot.HighRiskDomain || snapshot.FreshnessSensitive
                ? EpistemicAnswerMode.Abstain
                : EpistemicAnswerMode.NeedsVerification;
        }

        return context.Sources.Count > 0
            ? EpistemicAnswerMode.Grounded
            : EpistemicAnswerMode.Direct;
    }

    private static bool HasStrongGroundedCoverage(ChatTurnContext context, EpistemicRiskSnapshot snapshot)
    {
        if (snapshot.AbstentionRecommended || snapshot.HasContradictions)
        {
            return false;
        }

        var hasSources = context.Sources.Count > 0 || context.ResearchEvidenceItems.Count > 0;
        return hasSources &&
               string.Equals(snapshot.GroundingStatus, "grounded", StringComparison.OrdinalIgnoreCase) &&
               snapshot.CitationCoverage >= 0.70 &&
               snapshot.VerifiedClaimRatio >= 0.85;
    }

    private static bool ShouldSuppressBestEffortForMandatoryLiveWeb(ChatTurnContext context, EpistemicRiskSnapshot snapshot)
    {
        return context.IsFactualPrompt &&
               string.Equals(context.ResolvedLiveWebRequirement, "web_required", StringComparison.OrdinalIgnoreCase) &&
               (snapshot.HighRiskDomain || snapshot.FreshnessSensitive);
    }
}
