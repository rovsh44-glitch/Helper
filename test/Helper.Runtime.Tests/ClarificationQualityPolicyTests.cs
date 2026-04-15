using Helper.Api.Conversation;
using Helper.Runtime.Core;

namespace Helper.Runtime.Tests;

public sealed class ClarificationQualityPolicyTests
{
    [Fact]
    public void HybridAmbiguityDetector_DoesNotTreat_ResearchPromptDomainLanguage_AsConstraintGap()
    {
        var detector = new HybridAmbiguityDetector();

        var decision = detector.Analyze("Объясни, помогает ли интервальное голодание снижать вес без потери мышц, если научные и популярные источники расходятся.");

        Assert.NotEqual(AmbiguityType.Constraints, decision.Type);
    }

    [Fact]
    public void ClarificationQualityPolicy_ReturnsBoundarySpecificQuestion()
    {
        var policy = new ClarificationQualityPolicy();

        var decision = policy.BuildDecision(
            new AmbiguityDecision(true, AmbiguityType.Format, 0.8, "format unclear"),
            CollaborationIntentAnalysis.None,
            new IntentClassification(new IntentAnalysis(IntentType.Unknown, "test"), 0.5, "test", Array.Empty<string>()),
            1,
            "en");

        Assert.True(decision.ShouldBlockForClarification);
        Assert.Equal(ClarificationBoundary.Format, decision.Boundary);
        Assert.Contains("format", decision.Question, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ClarificationQualityPolicy_UsesSoftRefinement_ForGuidanceSeekingPrompt()
    {
        var policy = new ClarificationQualityPolicy();
        var collaboration = new CollaborationIntentAnalysis(
            IsGuidanceSeeking: true,
            TrustsBestJudgment: true,
            SeeksDelegatedExecution: false,
            PrefersAnswerOverClarification: true,
            HasHardConstraintLanguage: false,
            PrimaryMode: "guidance",
            Signals: new[] { "guidance_seeking" });

        var decision = policy.BuildDecision(
            new AmbiguityDecision(true, AmbiguityType.Goal, 0.8, "goal unclear"),
            collaboration,
            new IntentClassification(new IntentAnalysis(IntentType.Unknown, "test"), 0.5, "test", Array.Empty<string>()),
            1,
            "ru");

        Assert.False(decision.ShouldBlockForClarification);
        Assert.Equal(ClarificationBoundary.Goal, decision.Boundary);
        Assert.Contains("приоритет", decision.Question, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ClarificationQualityPolicy_UsesSoftRefinement_ForResearchConstraintAmbiguity()
    {
        var policy = new ClarificationQualityPolicy();

        var decision = policy.BuildDecision(
            new AmbiguityDecision(true, AmbiguityType.Constraints, 0.68, "constraints unclear"),
            CollaborationIntentAnalysis.None,
            new IntentClassification(new IntentAnalysis(IntentType.Research, "test"), 0.82, "test", Array.Empty<string>()),
            1,
            "ru");

        Assert.False(decision.ShouldBlockForClarification);
        Assert.Equal(ClarificationBoundary.Constraints, decision.Boundary);
        Assert.Contains("огранич", decision.Question, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HybridAmbiguityDetector_DoesNotTreat_ProductivityOutputClaim_AsFormatAmbiguity()
    {
        var detector = new HybridAmbiguityDetector();

        var decision = detector.Analyze("Объясни, насколько надёжны claims о том, что четырёхдневная рабочая неделя улучшает output практически в любой отрасли.");

        Assert.NotEqual(AmbiguityType.Format, decision.Type);
    }
}
