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
    public void Resolve_AllowsThreeIterations_ForRetractionStatusPrompt()
    {
        var policy = new SearchIterationPolicy();

        var budget = policy.Resolve(new WebSearchRequest("Проверь, была ли статья отозвана, исправлена или оспорена по состоянию на сегодня."));

        Assert.Equal(3, budget.MaxIterations);
    }

    [Fact]
    public void BuildPlans_AddsOfficialAndFreshnessBranches_ForRetractionStatusPrompt()
    {
        var planner = new WebQueryPlanner();

        var plans = planner.BuildPlans(
            new WebSearchRequest("Проверь, была ли статья отозвана, исправлена или оспорена по состоянию на сегодня."),
            new SearchIterationBudget(3, "retraction_status_multibranch"));

        Assert.Equal(3, plans.Count);
        Assert.Equal("primary", plans[0].QueryKind);
        Assert.Equal("official", plans[1].QueryKind);
        Assert.Equal("freshness", plans[2].QueryKind);
        Assert.Contains("crossref", plans[1].Query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pubmed", plans[2].Query, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildPlans_PrefersOfficialBranch_ForFreshOfficialVisaComparisonPrompt()
    {
        var planner = new WebQueryPlanner();

        var plans = planner.BuildPlans(
            new WebSearchRequest("Сравни актуальные визовые пути для software engineer, который хочет переехать в Германию в 2026 году.", Depth: 1),
            new SearchIterationBudget(3, "comparative_prompt+freshness_sensitive"));

        Assert.Equal(3, plans.Count);
        Assert.Equal("primary", plans[0].QueryKind);
        Assert.Equal("official", plans[1].QueryKind);
        Assert.Equal("freshness", plans[2].QueryKind);
        Assert.Contains("bamf", plans[1].Query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("make it in germany", plans[1].Query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("opportunity card", plans[1].Query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("auswaertiges amt", plans[1].Query, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(" eu ", $" {plans[1].Query} ", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_AllowsThreeIterations_ForUzbekistanFilingChecklistPrompt()
    {
        var policy = new SearchIterationPolicy();

        var budget = policy.Resolve(new WebSearchRequest("Составь актуальный filing checklist для remote worker, который выставляет инвойсы иностранным клиентам из Узбекистана в 2026 году."));

        Assert.Equal(3, budget.MaxIterations);
        Assert.Contains("strict_live_evidence", budget.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildPlans_RewritesUzbekistanFilingPrompt_ToRegulatoryTopicCore_AndOfficialBranch()
    {
        var planner = new WebQueryPlanner();

        var plans = planner.BuildPlans(
            new WebSearchRequest("Составь актуальный filing checklist для remote worker, который выставляет инвойсы иностранным клиентам из Узбекистана в 2026 году.", Depth: 1),
            new SearchIterationBudget(3, "regulation_freshness_multibranch"));

        Assert.Equal(3, plans.Count);
        Assert.Equal("primary", plans[0].QueryKind);
        Assert.Contains("узбекистан", plans[0].Query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("инвойсы", plans[0].Query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("отчетность", plans[0].Query, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("составь", plans[0].Query, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("official", plans[1].QueryKind);
        Assert.Contains("soliq", plans[1].Query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("lex", plans[1].Query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ey", plans[1].Query, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("site:", plans[1].Query, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("freshness", plans[2].QueryKind);
    }

    [Fact]
    public void BuildPlans_RewritesClimateSensitivityPrompt_ToScientificCore()
    {
        var planner = new WebQueryPlanner();

        var plans = planner.BuildPlans(
            new WebSearchRequest("Разбери, почему источники расходятся по оценкам climate sensitivity и какой вывод при этом остаётся безопасным.", Depth: 1),
            new SearchIterationBudget(3, "comparative_prompt"));

        Assert.Equal("primary", plans[0].QueryKind);
        Assert.Contains("equilibrium climate sensitivity", plans[0].Query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("transient climate response", plans[0].Query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("earth system sensitivity", plans[0].Query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(plans, plan => string.Equals(plan.QueryKind, "evidence", StringComparison.OrdinalIgnoreCase) &&
                                       plan.Query.Contains("aerosol forcing", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(plans, plan => string.Equals(plan.QueryKind, "contradiction", StringComparison.OrdinalIgnoreCase) &&
                                       plan.Query.Contains("ipcc", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildPlans_RewritesEuAiRegulationPrompt_ToOfficialCore()
    {
        var planner = new WebQueryPlanner();

        var plans = planner.BuildPlans(
            new WebSearchRequest("Объясни, что последние изменения в регулировании ИИ в ЕС означают сегодня для маленького software vendor.", Depth: 1),
            new SearchIterationBudget(3, "strict_live_evidence"));

        Assert.Equal("primary", plans[0].QueryKind);
        Assert.Contains("european union", plans[0].Query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("artificial intelligence act", plans[0].Query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ai office", plans[0].Query, StringComparison.OrdinalIgnoreCase);
        Assert.All(plans, plan => Assert.DoesNotContain(" eu ", $" {plan.Query} ", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(plans, plan => string.Equals(plan.QueryKind, "official", StringComparison.OrdinalIgnoreCase) &&
                                       plan.Query.Contains("service desk", StringComparison.OrdinalIgnoreCase) &&
                                       plan.Query.Contains("faq", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(plans, plan => string.Equals(plan.QueryKind, "freshness", StringComparison.OrdinalIgnoreCase) &&
                                       plan.Query.Contains("regulatory framework", StringComparison.OrdinalIgnoreCase) &&
                                       plan.Query.Contains("digital strategy", StringComparison.OrdinalIgnoreCase) &&
                                       plan.Query.Contains("provider obligations", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildPlans_RewritesArxivPublisherPolicyPrompt_ToFocusedPolicyQueries()
    {
        var planner = new WebQueryPlanner();

        var plans = planner.BuildPlans(
            new WebSearchRequest("Сравни arXiv preprints и peer-reviewed journal papers, а затем проверь текущие практики издателей и репозиториев.", Depth: 1),
            new SearchIterationBudget(3, "publisher_policy_multibranch"));

        Assert.Equal(3, plans.Count);
        Assert.Equal("primary", plans[0].QueryKind);
        Assert.Contains("arxiv", plans[0].Query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("sherpa", plans[0].Query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("open access", plans[0].Query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(plans, plan => string.Equals(plan.QueryKind, "publisher_policy", StringComparison.OrdinalIgnoreCase) &&
                                       plan.Query.Contains("doaj", StringComparison.OrdinalIgnoreCase) &&
                                       plan.Query.Contains("embargo", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(plans, plan => string.Equals(plan.QueryKind, "paper_focus", StringComparison.OrdinalIgnoreCase) &&
                                       plan.Query.Contains("accepted manuscript", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Resolve_AllowsThreeIterations_ForEuDroneCustomsPrompt()
    {
        var policy = new SearchIterationPolicy();

        var budget = policy.Resolve(new WebSearchRequest("Проверь, актуальны ли import restrictions и customs rules для ввоза дрона в ЕС на сегодня."));

        Assert.Equal(3, budget.MaxIterations);
        Assert.Contains("strict_live_evidence", budget.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildPlans_RewritesEuDroneCustomsPrompt_ToOfficialFreshnessBranches()
    {
        var planner = new WebQueryPlanner();

        var plans = planner.BuildPlans(
            new WebSearchRequest("Проверь, актуальны ли import restrictions и customs rules для ввоза дрона в ЕС на сегодня.", Depth: 1),
            new SearchIterationBudget(3, "strict_live_evidence"));

        Assert.Equal("primary", plans[0].QueryKind);
        Assert.Contains("drone", plans[0].Query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("customs", plans[0].Query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("easa", plans[0].Query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("european union", plans[0].Query, StringComparison.OrdinalIgnoreCase);
        Assert.All(plans, plan => Assert.DoesNotContain(" eu ", $" {plan.Query} ", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(plans, plan => string.Equals(plan.QueryKind, "official", StringComparison.OrdinalIgnoreCase) &&
                                       plan.Query.Contains("drones uas faq", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(plans, plan => string.Equals(plan.QueryKind, "freshness", StringComparison.OrdinalIgnoreCase) &&
                                       plan.Query.Contains("your europe", StringComparison.OrdinalIgnoreCase) &&
                                       plan.Query.Contains("customs procedures", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Resolve_AllowsThreeIterations_ForEuAiRegulationPrompt()
    {
        var policy = new SearchIterationPolicy();

        var budget = policy.Resolve(new WebSearchRequest("Объясни, что последние изменения в регулировании ИИ в ЕС означают сегодня для маленького software vendor."));

        Assert.Equal(3, budget.MaxIterations);
        Assert.Contains("strict_live_evidence", budget.Reason, StringComparison.OrdinalIgnoreCase);
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
        Assert.Equal("official", plans[1].QueryKind);
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
        Assert.Contains("профилакти", plans[0].Query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("мигрень", plans[0].Query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("клинические", plans[0].Query, StringComparison.OrdinalIgnoreCase);
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
        Assert.Contains("профилакти", plans[0].Query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("мигрень", plans[0].Query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("клинические", plans[0].Query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("рекомендации", plans[0].Query, StringComparison.OrdinalIgnoreCase);
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
    public void BuildPlans_RewritesMixedLanguageTaxThresholdPrompt_ToTaxDeadlineCore()
    {
        var planner = new WebQueryPlanner();

        var plans = planner.BuildPlans(
            new WebSearchRequest("Проверь, актуальны ли налоговые thresholds и reporting deadlines, которыми я пользуюсь сегодня.", Depth: 1),
            new SearchIterationBudget(3, "fresh_prompt"));

        Assert.Equal("primary", plans[0].QueryKind);
        Assert.Contains("налог", plans[0].Query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("пороги", plans[0].Query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("сроки", plans[0].Query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("официальные", plans[0].Query, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("которыми я пользуюсь сегодня", plans[0].Query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(plans, plan => plan.QueryKind == "freshness");
        Assert.Contains(plans, plan => plan.QueryKind == "official");
    }

    [Fact]
    public void BuildPlans_RewritesSparseVectorComparisonPrompt_ToFocusedCore()
    {
        var planner = new WebQueryPlanner();

        var plans = planner.BuildPlans(
            new WebSearchRequest("Сравни vector databases и классический search для маленькой команды, если большая часть benchmark-ов vendor-shaped.", Depth: 1),
            new SearchIterationBudget(3, "comparative_prompt"));

        Assert.Equal("primary", plans[0].QueryKind);
        Assert.Contains("vector database", plans[0].Query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("independent comparison", plans[0].Query, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("vendor-shaped", plans[0].Query, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildPlans_RewritesRegulatedCodingAssistantPrompt_ToFocusedCore()
    {
        var planner = new WebQueryPlanner();

        var plans = planner.BuildPlans(
            new WebSearchRequest("Составь осторожный план внедрения AI coding assistants в регулируемой компании, где claims о продуктивности пока спорные.", Depth: 1),
            new SearchIterationBudget(2, "broad_prompt"));

        Assert.Equal("primary", plans[0].QueryKind);
        Assert.Contains("ai coding assistants", plans[0].Query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("governance", plans[0].Query, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("осторожный", plans[0].Query, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildPlans_UsesFocusedStepBack_ForFourDayWeekReliabilityPrompt()
    {
        var planner = new WebQueryPlanner();

        var plans = planner.BuildPlans(
            new WebSearchRequest("Объясни, насколько надёжны claims о том, что четырёхдневная рабочая неделя улучшает output практически в любой отрасли.", Depth: 1),
            new SearchIterationBudget(2, "broad_prompt"));

        Assert.Equal(2, plans.Count);
        Assert.Equal("step_back", plans[1].QueryKind);
        Assert.Contains("четырехдневная", plans[1].Query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("обзор", plans[1].Query, StringComparison.OrdinalIgnoreCase);
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
    public void Resolve_TreatsSvezhieAsFreshnessSignal_ForRegulatoryPrompt()
    {
        var policy = new SearchIterationPolicy();

        var budget = policy.Resolve(new WebSearchRequest("Проверь свежие требования к отчетности для ИП"));

        Assert.Equal(3, budget.MaxIterations);
        Assert.Contains("strict_live_evidence", budget.Reason, StringComparison.OrdinalIgnoreCase);
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
    public void Resolve_AllowsThreeIterations_ForLiteratureReviewGuidancePrompt()
    {
        var policy = new SearchIterationPolicy();

        var budget = policy.Resolve(new WebSearchRequest("Оцени мой метод literature review и проверь его по актуальным guidance для systematic reviews."));

        Assert.Equal(3, budget.MaxIterations);
        Assert.Contains("paper_freshness_multibranch", budget.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildPlans_AddsFreshnessThenPaperFocus_ForLiteratureReviewGuidancePrompt()
    {
        var planner = new WebQueryPlanner();

        var plans = planner.BuildPlans(
            new WebSearchRequest("Оцени мой метод literature review и проверь его по актуальным guidance для systematic reviews.", Depth: 1),
            new SearchIterationBudget(3, "paper_freshness_multibranch"));

        Assert.Equal(3, plans.Count);
        Assert.Equal("primary", plans[0].QueryKind);
        Assert.Equal("freshness", plans[1].QueryKind);
        Assert.Equal("paper_focus", plans[2].QueryKind);
        Assert.Contains("systematic", plans[2].Query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("prisma", plans[2].Query, StringComparison.OrdinalIgnoreCase);
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
    public void Resolve_AllowsThreeIterations_ForPrediabetesNutritionPrompt()
    {
        var policy = new SearchIterationPolicy();

        var budget = policy.Resolve(new WebSearchRequest("Оцени мой дневной рацион при преддиабете и проверь его по свежим официальным рекомендациям: сладкий йогурт утром, рис на обед, фрукты вечером, сок перед сном."));

        Assert.Equal(3, budget.MaxIterations);
        Assert.Contains("medical_evidence", budget.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildPlans_RewritesPrediabetesDietPrompt_ToMedicalNutritionCore()
    {
        var planner = new WebQueryPlanner();

        var plans = planner.BuildPlans(
            new WebSearchRequest("Оцени мой дневной рацион при преддиабете и проверь его по свежим официальным рекомендациям: сладкий йогурт утром, рис на обед, фрукты вечером, сок перед сном.", Depth: 1),
            new SearchIterationBudget(3, "medical_evidence_query"));

        Assert.Equal("primary", plans[0].QueryKind);
        Assert.DoesNotContain("оцени", plans[0].Query, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("йогурт", plans[0].Query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("преддиабет", plans[0].Query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("питание", plans[0].Query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("рацион", plans[0].Query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("официальные", plans[0].Query, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildPlans_AddsFreshnessAndEvidenceBranches_ForPrediabetesNutritionPrompt()
    {
        var planner = new WebQueryPlanner();

        var plans = planner.BuildPlans(
            new WebSearchRequest("Оцени мой дневной рацион при преддиабете и проверь его по свежим официальным рекомендациям: сладкий йогурт утром, рис на обед, фрукты вечером, сок перед сном.", Depth: 1),
            new SearchIterationBudget(3, "medical_evidence_query"));

        Assert.Equal(3, plans.Count);
        Assert.Equal("freshness", plans[1].QueryKind);
        Assert.Equal("evidence", plans[2].QueryKind);
        Assert.Contains("свежие", plans[1].Query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("преддиабет", plans[2].Query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("питание", plans[2].Query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("systematic review", plans[2].Query, StringComparison.OrdinalIgnoreCase);
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
    public void BuildPlans_TreatsRaznyeVyvody_AsContradictionSignal()
    {
        var planner = new WebQueryPlanner();

        var plans = planner.BuildPlans(
            new WebSearchRequest("Сравни холодные ванны и сауну для восстановления после силовых тренировок, если исследования и популярные обзоры дают разные выводы.", Depth: 1),
            new SearchIterationBudget(3, "contradiction_sensitive"));

        Assert.Equal(3, plans.Count);
        Assert.Equal("evidence", plans[1].QueryKind);
        Assert.Equal("contradiction", plans[2].QueryKind);
        Assert.Contains("meta-analysis", plans[2].Query, StringComparison.OrdinalIgnoreCase);
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

