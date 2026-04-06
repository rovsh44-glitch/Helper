using Helper.Runtime.WebResearch;

namespace Helper.Runtime.Tests;

public sealed class SearchEvidenceSufficiencyPolicyTests
{
    [Fact]
    public void Evaluate_RequiresThirdVerificationPass_ForMedicalConflictPrompt()
    {
        var policy = new SearchEvidenceSufficiencyPolicy();
        var request = new WebSearchRequest("Объясни, помогает ли интервальное голодание снижать вес без потери мышц, если научные и популярные источники расходятся.");
        var executedPlans = new[]
        {
            new WebSearchPlan("интервальное голодание вес мышцы", 5, 1, "research", "standard", true, "primary"),
            new WebSearchPlan("интервальное голодание systematic review meta-analysis randomized trial", 5, 1, "research", "verification", true, "evidence")
        };
        var aggregate = new[]
        {
            new WebSearchDocument("https://pubmed.ncbi.nlm.nih.gov/35565749/", "PubMed", "Systematic review."),
            new WebSearchDocument("https://pmc.ncbi.nlm.nih.gov/articles/PMC9762455/", "PMC", "Review of intermittent fasting."),
            new WebSearchDocument("https://cochrane.org/review", "Cochrane", "Evidence synthesis.")
        };

        var decision = policy.Evaluate(request, executedPlans, aggregate);

        Assert.False(decision.IsSufficient);
        Assert.Equal("need_medical_evidence_reconciliation", decision.Reason);
    }

    [Fact]
    public void Evaluate_RequiresQueryExpansion_ForBroadPrompt_AfterSingleWeakResult()
    {
        var policy = new SearchEvidenceSufficiencyPolicy();
        var request = new WebSearchRequest("Объясни общую картину рисков и ограничений внедрения малых модульных реакторов в городской энергетике.");
        var executedPlans = new[]
        {
            new WebSearchPlan("риски ограничения малых модульных реакторов городской энергетике", 5, 1, "research", "standard", true, "primary")
        };
        var aggregate = new[]
        {
            new WebSearchDocument("https://example.org/article", "One article", "Single weak result.")
        };

        var decision = policy.Evaluate(request, executedPlans, aggregate);

        Assert.False(decision.IsSufficient);
        Assert.Equal("need_query_expansion", decision.Reason);
    }

    [Fact]
    public void Evaluate_RequiresPaperFocusQuery_ForPaperAnalysisPrompt()
    {
        var policy = new SearchEvidenceSufficiencyPolicy();
        var request = new WebSearchRequest("Analyze Attention Residuals paper pdf");
        var executedPlans = new[]
        {
            new WebSearchPlan("Attention Residuals paper pdf", 5, 1, "research", "standard", true, "primary")
        };
        var aggregate = new[]
        {
            new WebSearchDocument("https://example.org/blog", "Blog note", "Short mention of the paper.")
        };

        var decision = policy.Evaluate(request, executedPlans, aggregate);

        Assert.False(decision.IsSufficient);
        Assert.Equal("need_paper_focus_query", decision.Reason);
    }

    [Fact]
    public void Evaluate_RequiresStructuredFollowUp_ForFreshMedicalGuidelinePrompt_AfterPrimaryHitsOnly()
    {
        var policy = new SearchEvidenceSufficiencyPolicy();
        var request = new WebSearchRequest("Объясни, как обычно строят профилактику мигрени, а затем проверь, что изменилось или уточнилось в последних клинических рекомендациях.");
        var executedPlans = new[]
        {
            new WebSearchPlan("профилактику мигрени последние клинических рекомендациях", 5, 1, "research", "standard", true, "primary")
        };
        var aggregate = new[]
        {
            new WebSearchDocument("https://diseases.medelement.com/disease/migraine", "Guideline", "Clinical guidance."),
            new WebSearchDocument("https://legalacts.ru/doc/migraine-guideline", "Official", "Official document.")
        };

        var decision = policy.Evaluate(request, executedPlans, aggregate);

        Assert.False(decision.IsSufficient);
        Assert.Equal("need_freshness_query", decision.Reason);
    }

    [Fact]
    public void Evaluate_RequiresPaperFocusCoverage_ForLiteratureReviewPrompt_AfterFreshnessWithSingleSource()
    {
        var policy = new SearchEvidenceSufficiencyPolicy();
        var request = new WebSearchRequest("Оцени мой метод literature review и проверь его по актуальным guidance для systematic reviews.");
        var executedPlans = new[]
        {
            new WebSearchPlan("literature review guidance systematic reviews", 5, 1, "research", "standard", true, "primary"),
            new WebSearchPlan("literature review guidance systematic reviews latest update", 5, 1, "research", "freshness", true, "freshness")
        };
        var aggregate = new[]
        {
            new WebSearchDocument("https://cyberleninka.ru/article/n/review-guidance", "Review guidance", "Single science reference.")
        };

        var decision = policy.Evaluate(request, executedPlans, aggregate);

        Assert.False(decision.IsSufficient);
        Assert.Equal("need_paper_focus_query", decision.Reason);
    }
}

