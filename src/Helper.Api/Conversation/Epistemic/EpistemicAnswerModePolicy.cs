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

        if (context.ForceBestEffort)
        {
            return EpistemicAnswerMode.BestEffortHypothesis;
        }

        if (snapshot.HasContradictions)
        {
            return snapshot.HighRiskDomain
                ? EpistemicAnswerMode.Abstain
                : EpistemicAnswerMode.NeedsVerification;
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
}
