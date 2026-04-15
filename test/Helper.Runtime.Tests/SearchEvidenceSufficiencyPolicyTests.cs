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

    [Fact]
    public void Evaluate_RequiresThreeSources_ForStrictLiveRegulationComparison()
    {
        var policy = new SearchEvidenceSufficiencyPolicy();
        var request = new WebSearchRequest("Сравни актуальные визовые пути для software engineer, который хочет переехать в Германию в 2026 году.");
        var executedPlans = new[]
        {
            new WebSearchPlan("germany software engineer visa routes eu blue card skilled worker residence permit work permit 2026", 5, 1, "research", "standard", true, "primary"),
            new WebSearchPlan("germany software engineer visa routes eu blue card skilled worker residence permit work permit official make it in germany bamf arbeitsagentur auswaertiges amt deutschland", 5, 1, "research", "verification", true, "official")
        };
        var aggregate = new[]
        {
            new WebSearchDocument("https://make-it-in-germany.com/en/visa-residence/types/eu-blue-card", "EU Blue Card", "Official guidance."),
            new WebSearchDocument("https://bamf.de/EN/Themen/MigrationAufenthalt/Arbeiten/arbeiten-node.html", "Working in Germany", "Official BAMF migration guidance.")
        };

        var decision = policy.Evaluate(request, executedPlans, aggregate);

        Assert.False(decision.IsSufficient);
        Assert.Equal("need_cross_source_comparison", decision.Reason);
    }

    [Fact]
    public void Evaluate_RequiresThreeSources_ForEuDroneCustomsFreshnessPrompt()
    {
        var policy = new SearchEvidenceSufficiencyPolicy();
        var request = new WebSearchRequest("Проверь, актуальны ли import restrictions и customs rules для ввоза дрона в ЕС на сегодня.");
        var executedPlans = new[]
        {
            new WebSearchPlan("eu drone import customs batteries vat ce marking easa official guidance", 5, 1, "research", "standard", true, "primary"),
            new WebSearchPlan("eu drone import customs batteries vat ce marking official guidance easa europa customs travel", 5, 1, "research", "verification", true, "official")
        };
        var aggregate = new[]
        {
            new WebSearchDocument("https://easa.europa.eu/en/the-agency/faqs/drones", "EASA drones FAQ", "Official EASA drone rules and battery guidance."),
            new WebSearchDocument("https://taxation-customs.ec.europa.eu/customs-4/customs-procedures-import-and-export_en", "EU customs procedures", "Official customs procedures for imports into the EU.")
        };

        var decision = policy.Evaluate(request, executedPlans, aggregate);

        Assert.False(decision.IsSufficient);
        Assert.Equal("need_freshness_query", decision.Reason);
    }

    [Fact]
    public void Evaluate_RequiresThreeSources_ForRegulationFreshnessSearchQuery_EvenWithoutExplicitTodaySignal()
    {
        var policy = new SearchEvidenceSufficiencyPolicy();
        var request = new WebSearchRequest("european union drone import customs batteries vat ce marking easa taxation customs official guidance");
        var executedPlans = new[]
        {
            new WebSearchPlan("european union drone import customs batteries vat ce marking easa taxation customs official guidance", 5, 1, "research", "standard", true, "primary"),
            new WebSearchPlan("easa drones uas faq batteries travel ce marking european union official guidance", 5, 1, "research", "verification", true, "official")
        };
        var aggregate = new[]
        {
            new WebSearchDocument("https://easa.europa.eu/en/the-agency/faqs/drones", "EASA drones FAQ", "Official EASA drone rules and battery guidance."),
            new WebSearchDocument("https://europa.eu/youreurope/business/product-requirements/labels-markings/ce-marking/index_en.htm", "Your Europe CE marking", "Official EU CE marking guidance for products.")
        };

        var decision = policy.Evaluate(request, executedPlans, aggregate);

        Assert.False(decision.IsSufficient);
        Assert.Equal("need_freshness_query", decision.Reason);
    }

    [Fact]
    public void Evaluate_TreatsPublisherPolicyIteration_AsStructuredFollowUp_ForArxivPolicyPrompt()
    {
        var policy = new SearchEvidenceSufficiencyPolicy();
        var request = new WebSearchRequest("Сравни arXiv preprints и peer-reviewed journal papers, а затем проверь текущие практики издателей и репозиториев.");
        var executedPlans = new[]
        {
            new WebSearchPlan("arxiv preprint peer review journal publisher repository sherpa romeo open access self-archiving accepted manuscript policy", 5, 1, "research", "standard", true, "primary"),
            new WebSearchPlan("sherpa romeo journal publisher policy open access self-archiving accepted manuscript embargo doaj crossref arxiv repository", 5, 1, "research", "verification", true, "publisher_policy")
        };
        var aggregate = new[]
        {
            new WebSearchDocument("https://v2.sherpa.ac.uk/id/publication/12345", "Sherpa Romeo", "Publisher open access and self-archiving policy."),
            new WebSearchDocument("https://doaj.org/article/abcdef", "DOAJ policy overview", "Open access journal and repository policy context.")
        };

        var decision = policy.Evaluate(request, executedPlans, aggregate);

        Assert.True(decision.IsSufficient);
        Assert.Equal("paper_focus_covered", decision.Reason);
    }
}

