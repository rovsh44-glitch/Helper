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

    [Fact]
    public void BuildSnapshot_Treats_LocalOnly_FreshClaims_As_Unsupported_For_WebRequiredTurns()
    {
        var policy = new BehavioralCalibrationPolicy();
        var context = new ChatTurnContext
        {
            TurnId = "epistemic-local-only-fresh",
            Request = new ChatRequestDto("Какие сегодня актуальные налоговые пороги?", null, 12, null),
            Conversation = new ConversationState("epistemic-local-only-fresh"),
            History = Array.Empty<ChatMessageDto>(),
            IsFactualPrompt = true,
            ResolvedLiveWebRequirement = "web_required",
            GroundingStatus = "grounded",
            CitationCoverage = 1.0,
            VerifiedClaims = 1,
            TotalClaims = 1,
            Confidence = 0.74
        };
        context.ResearchEvidenceItems.Add(new ResearchEvidenceItem(
            1,
            "Tax Handbook.pdf (pdf) | page:12 | id=tax-handbook",
            "Tax Handbook.pdf",
            "Historical tax threshold background.",
            TrustLevel: "local_library",
            EvidenceKind: "local_library_chunk",
            SourceLayer: "local_library",
            SourceFormat: "pdf",
            SourceId: "tax-handbook",
            DisplayTitle: "Tax Handbook.pdf",
            Locator: "page:12"));
        context.ClaimGroundings.Add(new ClaimGrounding(
            "Сегодня действует актуальный налоговый порог.",
            ClaimSentenceType.Fact,
            SourceIndex: 1,
            EvidenceGrade: "strong",
            MatchConfidence: 0.9));

        var snapshot = policy.BuildSnapshot(context);

        Assert.True(snapshot.FreshnessSensitive);
        Assert.True(snapshot.HasWeakEvidence);
        Assert.True(snapshot.AbstentionRecommended);
        Assert.NotNull(context.EvidenceFusionSnapshot);
        Assert.Equal(0, context.EvidenceFusionSnapshot!.WebSourceCount);
        Assert.Equal(1, context.EvidenceFusionSnapshot.LocalSourceCount);
        Assert.Equal(1, context.EvidenceFusionSnapshot.LocalOnlyFreshClaimCount);
    }
}
