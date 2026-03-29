using Helper.Runtime.WebResearch;

namespace Helper.Runtime.Tests;

public sealed class WebQueryPlannerTests
{
    [Fact]
    public void Resolve_AllowsThreeIterations_ForComparativePrompt()
    {
        var policy = new SearchIterationPolicy();

        var budget = policy.Resolve(new WebSearchRequest("compare latest nvidia and amd datacenter gpu roadmap", Depth: 1));

        Assert.Equal(3, budget.MaxIterations);
        Assert.Contains("comparative", budget.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildPlans_AddsFreshnessAndContradictionQueries_ForComparativeCurrentPrompt()
    {
        var planner = new WebQueryPlanner();

        var plans = planner.BuildPlans(
            new WebSearchRequest("compare latest nvidia and amd datacenter gpu roadmap", Depth: 1),
            new SearchIterationBudget(3, "comparative_prompt"));

        Assert.Equal(3, plans.Count);
        Assert.Equal("primary", plans[0].QueryKind);
        Assert.Equal("freshness", plans[1].QueryKind);
        Assert.Equal("contradiction", plans[2].QueryKind);
    }

    [Fact]
    public void BuildPlans_AddsNarrowQuery_ForLongPromptWithoutFreshnessSignals()
    {
        var planner = new WebQueryPlanner();

        var plans = planner.BuildPlans(
            new WebSearchRequest("please find enterprise dotnet observability migration guidance with production tracing patterns and rollout constraints", Depth: 1),
            new SearchIterationBudget(2, "long_query"));

        Assert.Equal(2, plans.Count);
        Assert.Equal("primary", plans[0].QueryKind);
        Assert.Equal("narrow", plans[1].QueryKind);
        Assert.NotEqual(plans[0].Query, plans[1].Query);
    }

    [Fact]
    public void BuildPlans_DoesNotFanoutSimpleSearchLikeQuery_EvenWhenBudgetAllowsThreePasses()
    {
        var planner = new WebQueryPlanner();

        var plans = planner.BuildPlans(
            new WebSearchRequest("grpc retry policy official docs", Depth: 1),
            new SearchIterationBudget(3, "manual_budget"));

        Assert.Single(plans);
        Assert.Equal("primary", plans[0].QueryKind);
    }

    [Fact]
    public void BuildPlans_CleansInstructionNoise_FromPrimaryQuery()
    {
        var planner = new WebQueryPlanner();

        var plans = planner.BuildPlans(
            new WebSearchRequest("Объясни, как обычно строят профилактику мигрени, а затем проверь, что изменилось или уточнилось в последних клинических рекомендациях.", Depth: 1),
            new SearchIterationBudget(2, "fresh_prompt"));

        Assert.Equal("primary", plans[0].QueryKind);
        Assert.DoesNotContain("объясни", plans[0].Query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("профилактику", plans[0].Query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("мигрени", plans[0].Query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("клинических", plans[0].Query, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildPlans_RewritesGuidelinePrompt_ToTopicCore_InsteadOfVerbNoise()
    {
        var planner = new WebQueryPlanner();

        var plans = planner.BuildPlans(
            new WebSearchRequest("Объясни, как обычно строят профилактику мигрени, а затем проверь, что изменилось или уточнилось в последних клинических рекомендациях.", Depth: 1),
            new SearchIterationBudget(2, "fresh_prompt"));

        Assert.DoesNotContain("строят", plans[0].Query, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("изменилось", plans[0].Query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("профилактику", plans[0].Query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("мигрени", plans[0].Query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("клинических", plans[0].Query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("рекомендациях", plans[0].Query, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildPlans_AttachesRewriteTrace_ToPrimaryPlan()
    {
        var planner = new WebQueryPlanner();

        var plans = planner.BuildPlans(
            new WebSearchRequest("Объясни, что сейчас известно о текущей вспышке кори в Европе и какие официальные меры профилактики рекомендуют на сегодня.", Depth: 1),
            new SearchIterationBudget(2, "fresh_prompt"));

        Assert.NotNull(plans[0].RewriteTrace);
        Assert.Contains(plans[0].RewriteTrace!, line => line.Contains("stage=topic_core", StringComparison.Ordinal));
        Assert.Contains(plans[0].RewriteTrace!, line => line.Contains("stage=lexical_cleanup", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildPlans_RewritesCurrentOutbreakPrompt_ToTopicalCore_WithoutLosingOfficialMeasures()
    {
        var planner = new WebQueryPlanner();

        var plans = planner.BuildPlans(
            new WebSearchRequest("Объясни, что сейчас известно о текущей вспышке кори в Европе и какие официальные меры профилактики рекомендуют на сегодня.", Depth: 1),
            new SearchIterationBudget(3, "fresh_prompt"));

        Assert.DoesNotContain("объясни", plans[0].Query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("вспышке", plans[0].Query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("кори", plans[0].Query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Европе", plans[0].Query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("официальные", plans[0].Query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("профилактики", plans[0].Query, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("freshness", plans[1].QueryKind);
    }

    [Fact]
    public void BuildPlans_AddsStepBackBranch_ForBroadHumanPrompt()
    {
        var planner = new WebQueryPlanner();

        var plans = planner.BuildPlans(
            new WebSearchRequest("Объясни общую картину рисков и ограничений внедрения малых модульных реакторов в городской энергетике.", Depth: 1),
            new SearchIterationBudget(2, "broad_prompt"));

        Assert.Equal(2, plans.Count);
        Assert.Equal("step_back", plans[1].QueryKind);
        Assert.Contains("обзор", plans[1].Query, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildPlans_AddsPaperFocusBranch_ForPaperAnalysisPrompt()
    {
        var planner = new WebQueryPlanner();

        var plans = planner.BuildPlans(
            new WebSearchRequest("Analyze Attention Residuals paper pdf", Depth: 1),
            new SearchIterationBudget(2, "paper_analysis"));

        Assert.Equal(2, plans.Count);
        Assert.Equal("paper_focus", plans[1].QueryKind);
        Assert.Contains("abstract", plans[1].Query, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_AllowsThreeIterations_ForMedicalEvidencePrompt()
    {
        var policy = new SearchIterationPolicy();

        var budget = policy.Resolve(new WebSearchRequest("профилактика мигрени клинические рекомендации для взрослых"));

        Assert.Equal(3, budget.MaxIterations);
        Assert.Contains("medical_evidence", budget.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildPlans_AddsFreshnessThenEvidenceQueries_ForMedicalGuidelinePrompt()
    {
        var planner = new WebQueryPlanner();

        var plans = planner.BuildPlans(
            new WebSearchRequest("Объясни, как обычно строят профилактику мигрени, а затем проверь, что изменилось или уточнилось в последних клинических рекомендациях.", Depth: 1),
            new SearchIterationBudget(3, "medical_evidence_query"));

        Assert.Equal(3, plans.Count);
        Assert.Equal("freshness", plans[1].QueryKind);
        Assert.Equal("evidence", plans[2].QueryKind);
        Assert.Contains("systematic review", plans[2].Query, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildPlans_TreatsRaskhodyatsya_AsContradictionSignal()
    {
        var planner = new WebQueryPlanner();

        var plans = planner.BuildPlans(
            new WebSearchRequest("Объясни, помогает ли интервальное голодание снижать вес без потери мышц, если научные и популярные источники расходятся.", Depth: 1),
            new SearchIterationBudget(3, "contradiction_sensitive"));

        Assert.Equal(3, plans.Count);
        Assert.Equal("contradiction", plans[2].QueryKind);
        Assert.Contains("meta-analysis", plans[2].Query, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildPlans_PrefersEvidenceSecondPass_ForMedicalConflictPrompt()
    {
        var planner = new WebQueryPlanner();

        var plans = planner.BuildPlans(
            new WebSearchRequest("Объясни, помогает ли интервальное голодание снижать вес без потери мышц, если научные и популярные источники расходятся.", Depth: 1),
            new SearchIterationBudget(3, "medical_conflict"));

        Assert.Equal(3, plans.Count);
        Assert.Equal("evidence", plans[1].QueryKind);
        Assert.Contains("systematic review", plans[1].Query, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("contradiction", plans[2].QueryKind);
    }

    [Fact]
    public void Resolve_TreatsRedLightRecoveryEvidencePrompt_AsMedicalEvidence()
    {
        var policy = new SearchIterationPolicy();

        var budget = policy.Resolve(new WebSearchRequest("Объясни, насколько убедительны данные о пользе терапии красным светом для восстановления после тренировок."));

        Assert.Equal(3, budget.MaxIterations);
        Assert.Contains("multi", budget.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildPlans_AddsScientificEvidenceBranch_ForRedLightRecoveryPrompt()
    {
        var planner = new WebQueryPlanner();

        var plans = planner.BuildPlans(
            new WebSearchRequest("Объясни, насколько убедительны данные о пользе терапии красным светом для восстановления после тренировок.", Depth: 1),
            new SearchIterationBudget(3, "medical_evidence_query"));

        Assert.Equal(3, plans.Count);
        Assert.Equal("evidence", plans[1].QueryKind);
        Assert.Contains("photobiomodulation", plans[1].Query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pubmed", plans[1].Query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("systematic review", plans[1].Query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("whole-body", plans[1].Query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("springer", plans[1].Query, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildPlans_RewritesConflictPrompt_ToMedicalTopicCore_WithoutLosingBodyCompositionFocus()
    {
        var planner = new WebQueryPlanner();

        var plans = planner.BuildPlans(
            new WebSearchRequest("Объясни, помогает ли интервальное голодание снижать вес без потери мышц, если научные и популярные источники расходятся.", Depth: 1),
            new SearchIterationBudget(3, "medical_conflict"));

        Assert.DoesNotContain("объясни", plans[0].Query, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("расходятся", plans[0].Query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("intermittent", plans[0].Query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("fasting", plans[0].Query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("time-restricted", plans[0].Query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("lean", plans[0].Query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("body", plans[0].Query, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("интервальное", plans[0].Query, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildPlans_AddsLeanMassEvidenceBranch_ForIntermittentFastingConflictPrompt()
    {
        var planner = new WebQueryPlanner();

        var plans = planner.BuildPlans(
            new WebSearchRequest("Объясни, помогает ли интервальное голодание снижать вес без потери мышц, если научные и популярные источники расходятся.", Depth: 1),
            new SearchIterationBudget(3, "medical_conflict"));

        Assert.Equal("evidence", plans[1].QueryKind);
        Assert.Contains("lean", plans[1].Query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("body", plans[1].Query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pubmed", plans[1].Query, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Evaluate_RequiresCrossSourceCoverage_ForComparativePrompt()
    {
        var policy = new SearchEvidenceSufficiencyPolicy();
        var request = new WebSearchRequest("compare aws and azure retry guidance");
        var executedPlans = new[]
        {
            new WebSearchPlan("compare aws and azure retry guidance", 5, 1, "research", "standard", true, "primary")
        };
        var aggregate = new[]
        {
            new WebSearchDocument("https://aws.amazon.com/retries", "AWS", "Retry guidance.")
        };

        var decision = policy.Evaluate(request, executedPlans, aggregate);

        Assert.False(decision.IsSufficient);
        Assert.Equal("need_cross_source_comparison", decision.Reason);
    }
}

