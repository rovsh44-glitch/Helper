using Helper.Api.Conversation;
using Helper.Api.Conversation.Epistemic;
using Helper.Api.Hosting;
using Helper.Runtime.Core;

namespace Helper.Runtime.Tests;

public sealed class EpistemicAnswerModePolicyTests
{
    [Fact]
    public void Resolve_Returns_Grounded_For_Well_Supported_Factual_Turn()
    {
        var policy = new EpistemicAnswerModePolicy();
        var context = CreateContext(isFactualPrompt: true);
        var snapshot = new EpistemicRiskSnapshot(
            GroundingStatus: "grounded",
            CitationCoverage: 0.92,
            VerifiedClaimRatio: 0.85,
            HasContradictions: false,
            HasWeakEvidence: false,
            HighRiskDomain: false,
            FreshnessSensitive: false,
            CurrentConfidence: 0.76,
            ConfidenceCeiling: 0.88,
            CalibrationThreshold: 0.58,
            AbstentionRecommended: false,
            Trace: Array.Empty<string>());

        var mode = policy.Resolve(context, snapshot);

        Assert.Equal(EpistemicAnswerMode.Grounded, mode);
    }

    [Fact]
    public void Resolve_Returns_Abstain_For_HighRisk_Unsupported_Factual_Turn()
    {
        var policy = new EpistemicAnswerModePolicy();
        var context = CreateContext(isFactualPrompt: true);
        var snapshot = new EpistemicRiskSnapshot(
            GroundingStatus: "unverified",
            CitationCoverage: 0.0,
            VerifiedClaimRatio: 0.0,
            HasContradictions: false,
            HasWeakEvidence: true,
            HighRiskDomain: true,
            FreshnessSensitive: true,
            CurrentConfidence: 0.34,
            ConfidenceCeiling: 0.42,
            CalibrationThreshold: 0.78,
            AbstentionRecommended: true,
            Trace: Array.Empty<string>());

        var mode = policy.Resolve(context, snapshot);

        Assert.Equal(EpistemicAnswerMode.Abstain, mode);
    }

    [Fact]
    public void Resolve_Returns_BestEffortHypothesis_When_Forced_BestEffort()
    {
        var policy = new EpistemicAnswerModePolicy();
        var context = CreateContext(isFactualPrompt: false);
        context.ForceBestEffort = true;
        var snapshot = new EpistemicRiskSnapshot(
            GroundingStatus: "unknown",
            CitationCoverage: 0.0,
            VerifiedClaimRatio: 0.0,
            HasContradictions: false,
            HasWeakEvidence: false,
            HighRiskDomain: false,
            FreshnessSensitive: false,
            CurrentConfidence: 0.51,
            ConfidenceCeiling: 0.50,
            CalibrationThreshold: 0.45,
            AbstentionRecommended: false,
            Trace: Array.Empty<string>());

        var mode = policy.Resolve(context, snapshot);

        Assert.Equal(EpistemicAnswerMode.BestEffortHypothesis, mode);
    }

    [Fact]
    public void Resolve_Returns_NeedsVerification_For_Clarification_Or_Weak_Evidence()
    {
        var policy = new EpistemicAnswerModePolicy();
        var context = CreateContext(isFactualPrompt: true);
        context.RequiresClarification = true;
        var snapshot = new EpistemicRiskSnapshot(
            GroundingStatus: "grounded_with_limits",
            CitationCoverage: 0.46,
            VerifiedClaimRatio: 0.40,
            HasContradictions: false,
            HasWeakEvidence: true,
            HighRiskDomain: false,
            FreshnessSensitive: false,
            CurrentConfidence: 0.49,
            ConfidenceCeiling: 0.58,
            CalibrationThreshold: 0.66,
            AbstentionRecommended: false,
            Trace: Array.Empty<string>());

        var mode = policy.Resolve(context, snapshot);

        Assert.Equal(EpistemicAnswerMode.NeedsVerification, mode);
    }

    private static ChatTurnContext CreateContext(bool isFactualPrompt)
    {
        return new ChatTurnContext
        {
            TurnId = Guid.NewGuid().ToString("N"),
            Request = new ChatRequestDto("test", null, 12, null),
            Conversation = new ConversationState("epistemic-policy"),
            History = Array.Empty<ChatMessageDto>(),
            IsFactualPrompt = isFactualPrompt
        };
    }
}
