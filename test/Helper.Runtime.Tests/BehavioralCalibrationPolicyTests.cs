using Helper.Api.Conversation;
using Helper.Api.Conversation.Epistemic;
using Helper.Api.Hosting;
using Helper.Runtime.Core;

namespace Helper.Runtime.Tests;

public sealed class BehavioralCalibrationPolicyTests
{
    [Fact]
    public void BuildSnapshot_Flags_HighRisk_Freshness_And_Abstention_For_Unsupported_Factual_Turn()
    {
        var policy = new BehavioralCalibrationPolicy();
        var context = new ChatTurnContext
        {
            TurnId = "epistemic-snapshot-risk",
            Request = new ChatRequestDto("Какие сегодня самые актуальные рекомендации по лечению мигрени?", null, 12, null),
            Conversation = new ConversationState("epistemic-snapshot-risk"),
            History = Array.Empty<ChatMessageDto>(),
            IsFactualPrompt = true,
            GroundingStatus = "unverified",
            CitationCoverage = 0.15,
            VerifiedClaims = 0,
            TotalClaims = 3,
            Confidence = 0.74
        };

        var snapshot = policy.BuildSnapshot(context);

        Assert.True(snapshot.HighRiskDomain);
        Assert.True(snapshot.FreshnessSensitive);
        Assert.True(snapshot.HasWeakEvidence);
        Assert.True(snapshot.AbstentionRecommended);
        Assert.True(snapshot.ConfidenceCeiling <= 0.58);
        Assert.Contains(snapshot.Trace, entry => entry.Contains("abstention_recommended", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildSnapshot_Detects_Contradictions_And_Lowers_Confidence_Ceiling()
    {
        var policy = new BehavioralCalibrationPolicy();
        var context = new ChatTurnContext
        {
            TurnId = "epistemic-snapshot-contradiction",
            Request = new ChatRequestDto("Сравни conflicting tax deadlines", null, 12, null),
            Conversation = new ConversationState("epistemic-snapshot-contradiction"),
            History = Array.Empty<ChatMessageDto>(),
            IsFactualPrompt = true,
            GroundingStatus = "grounded_with_contradictions",
            CitationCoverage = 0.81,
            VerifiedClaims = 2,
            TotalClaims = 3,
            Confidence = 0.78
        };

        var snapshot = policy.BuildSnapshot(context);

        Assert.True(snapshot.HasContradictions);
        Assert.True(snapshot.CalibrationThreshold >= 0.82);
        Assert.True(snapshot.ConfidenceCeiling <= 0.42);
    }
}
