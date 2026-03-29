using Helper.Runtime.WebResearch;
using Helper.Runtime.WebResearch.Ranking;

namespace Helper.Runtime.Tests;

public sealed class SourceAuthorityScorerTests
{
    [Fact]
    public void Evaluate_PrefersOfficialDocumentation_ForReferenceQuery()
    {
        var scorer = new SourceAuthorityScorer();
        var plan = new WebSearchPlan(
            Query: "latest .NET SDK release notes",
            MaxResults: 5,
            Depth: 1,
            Purpose: "research",
            SearchMode: "standard",
            AllowDeterministicFallback: false);

        var official = scorer.Evaluate(
            plan,
            new WebSearchDocument(
                "https://learn.microsoft.com/dotnet/core/releases-and-support",
                "Releases and support",
                "Official .NET release notes and support policy."));
        var generic = scorer.Evaluate(
            plan,
            new WebSearchDocument(
                "https://random-blog.example.com/best-dotnet-version",
                "Best .NET version in 2026",
                "A blog review of the best .NET version."));

        Assert.True(official.Score > generic.Score);
        Assert.True(official.IsAuthoritative);
        Assert.Equal("official_vendor_docs", official.Label);
    }

    [Fact]
    public void Evaluate_DemotesLowTrustSeoPages_ForReferenceQuery()
    {
        var policy = new SpamAndSeoDemotionPolicy();
        var plan = new WebSearchPlan(
            Query: "official .NET SDK documentation",
            MaxResults: 5,
            Depth: 1,
            Purpose: "research",
            SearchMode: "standard",
            AllowDeterministicFallback: false);

        var assessment = policy.Evaluate(
            plan,
            new WebSearchDocument(
                "https://top10-tools.xyz/best-dotnet-version?utm_source=ads",
                "Top 10 Best .NET Versions - Sponsored",
                "Sponsored ranking and affiliate review."));

        Assert.True(assessment.LowTrust);
        Assert.True(assessment.Penalty >= 0.50d);
        Assert.Contains(assessment.Reasons, reason => reason.Contains("low_trust_host", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Evaluate_PrefersClinicalEvidenceSource_OverInteractiveNoise_ForMedicalGuidelineQuery()
    {
        var scorer = new SourceAuthorityScorer();
        var policy = new SpamAndSeoDemotionPolicy();
        var plan = new WebSearchPlan(
            Query: "профилактика мигрени клинические рекомендации",
            MaxResults: 5,
            Depth: 1,
            Purpose: "research",
            SearchMode: "standard",
            AllowDeterministicFallback: false);

        var guideline = new WebSearchDocument(
            "https://www.consultant.ru/document/cons_doc_LAW_489415/",
            "Клинические рекомендации \"Мигрень\"",
            "Клинические рекомендации по диагностике и профилактической терапии мигрени.");
        var junk = new WebSearchDocument(
            "https://yandex.ru/games/app/209428",
            "Объясни слово! - играть онлайн бесплатно на сервисе Яндекс Игры",
            "Попробуй сказать слово иначе! Это игра для весёлой компании.");

        var guidelineScore = scorer.Evaluate(plan, guideline).Score - policy.Evaluate(plan, guideline).Penalty;
        var junkScore = scorer.Evaluate(plan, junk).Score - policy.Evaluate(plan, junk).Penalty;

        Assert.True(guidelineScore > junkScore);
    }

    [Fact]
    public void Evaluate_DemotesMailAnswers_ForEvidenceHeavyMedicalQuery()
    {
        var scorer = new SourceAuthorityScorer();
        var policy = new SpamAndSeoDemotionPolicy();
        var plan = new WebSearchPlan(
            Query: "интервальное голодание снижение веса без потери мышц клинические источники",
            MaxResults: 5,
            Depth: 1,
            Purpose: "research",
            SearchMode: "standard",
            AllowDeterministicFallback: false);

        var evidence = new WebSearchDocument(
            "https://medlineplus.gov/ency/patientinstructions/000899.htm",
            "Weight loss and diet",
            "Clinical health reference for safe diet and weight loss approaches.");
        var ugc = new WebSearchDocument(
            "https://otvet.mail.ru/question/269218828",
            "Что вам помогает успокоиться? - Ответы Mail",
            "Пользовательские ответы и обсуждение.");

        var evidenceScore = scorer.Evaluate(plan, evidence).Score - policy.Evaluate(plan, evidence).Penalty;
        var ugcAssessment = policy.Evaluate(plan, ugc);
        var ugcScore = scorer.Evaluate(plan, ugc).Score - ugcAssessment.Penalty;

        Assert.True(ugcAssessment.LowTrust);
        Assert.True(evidenceScore > ugcScore);
    }

    [Fact]
    public void ComputeQueryOverlapRatio_RecognizesRussianMorphology_ForClinicalSources()
    {
        var ratio = SourceAuthorityScorer.ComputeQueryOverlapRatio(
            "профилактика мигрени клинические рекомендации",
            "Мигрень у взрослых. Клинические рекомендации РФ 2024 по профилактической терапии мигрени.");

        Assert.True(ratio >= 0.50d);
    }

    [Fact]
    public void Evaluate_DemotesLifestyleHealthMedia_ForMedicalEvidenceQuery()
    {
        var scorer = new SourceAuthorityScorer();
        var policy = new SpamAndSeoDemotionPolicy();
        var plan = new WebSearchPlan(
            Query: "интервальное голодание снижение веса без потери мышц systematic review",
            MaxResults: 5,
            Depth: 1,
            Purpose: "research",
            SearchMode: "standard",
            AllowDeterministicFallback: false);

        var research = new WebSearchDocument(
            "https://pubmed.ncbi.nlm.nih.gov/35565749/",
            "Intermittent Fasting versus Continuous Calorie Restriction",
            "Systematic review and meta-analysis of randomized trials.");
        var lifestyle = new WebSearchDocument(
            "https://doctor.rambler.ru/healthylife/55195086-intervalnoe-golodanie-effektivnyy-sposob-snizit-ves",
            "Интервальное голодание: эффективный способ снизить вес",
            "Популярный обзор по теме healthy life.");

        var researchScore = scorer.Evaluate(plan, research).Score - policy.Evaluate(plan, research).Penalty;
        var lifestyleScore = scorer.Evaluate(plan, lifestyle).Score - policy.Evaluate(plan, lifestyle).Penalty;

        Assert.True(researchScore > lifestyleScore);
    }

    [Fact]
    public void Evaluate_DemotesCommercialLightTherapyVendor_ForEvidenceBoundaryQuery()
    {
        var scorer = new SourceAuthorityScorer();
        var policy = new SpamAndSeoDemotionPolicy();
        var plan = new WebSearchPlan(
            Query: "photobiomodulation muscle recovery pubmed pmc systematic review",
            MaxResults: 5,
            Depth: 1,
            Purpose: "research",
            SearchMode: "standard",
            AllowDeterministicFallback: false);

        var research = new WebSearchDocument(
            "https://pubmed.ncbi.nlm.nih.gov/37512345/",
            "Photobiomodulation therapy and exercise recovery",
            "Systematic review of muscle recovery and performance outcomes.");
        var commercial = new WebSearchDocument(
            "https://lighttherapyhome.com/ru/red-light-therapy-workout-boost-recovery",
            "Терапия красным светом после тренировки",
            "Guide to product-supported recovery and LED benefits.");

        var researchScore = scorer.Evaluate(plan, research).Score - policy.Evaluate(plan, research).Penalty;
        var commercialScore = scorer.Evaluate(plan, commercial).Score - policy.Evaluate(plan, commercial).Penalty;

        Assert.True(researchScore > commercialScore);
    }

    [Fact]
    public void Evaluate_DemotesTelegramPost_ForRecoveryEvidenceQuery()
    {
        var scorer = new SourceAuthorityScorer();
        var policy = new SpamAndSeoDemotionPolicy();
        var plan = new WebSearchPlan(
            Query: "photobiomodulation red light therapy muscle recovery after training",
            MaxResults: 5,
            Depth: 1,
            Purpose: "research",
            SearchMode: "standard",
            AllowDeterministicFallback: false);

        var research = new WebSearchDocument(
            "https://pmc.ncbi.nlm.nih.gov/articles/PMC4340643/",
            "Photobiomodulation Therapy in Skeletal Muscle",
            "Systematic review coverage of exercise recovery evidence.");
        var telegram = new WebSearchDocument(
            "https://t.me/s/examplechannel?before=1234",
            "Telegram post about red light therapy",
            "Social post claiming faster recovery after workouts.");

        var researchScore = scorer.Evaluate(plan, research).Score - policy.Evaluate(plan, research).Penalty;
        var telegramScore = scorer.Evaluate(plan, telegram).Score - policy.Evaluate(plan, telegram).Penalty;

        Assert.True(researchScore > telegramScore);
    }

    [Fact]
    public void Evaluate_DemotesMedicalOpinionBlog_ForRecoveryEvidenceQuery()
    {
        var scorer = new SourceAuthorityScorer();
        var policy = new SpamAndSeoDemotionPolicy();
        var plan = new WebSearchPlan(
            Query: "photobiomodulation red light therapy muscle recovery after training",
            MaxResults: 5,
            Depth: 1,
            Purpose: "research",
            SearchMode: "standard",
            AllowDeterministicFallback: false);

        var research = new WebSearchDocument(
            "https://pmc.ncbi.nlm.nih.gov/articles/PMC12463863/",
            "The Effect of Photobiomodulation Therapy on Muscle Performance",
            "Meta-analysis of randomized controlled trials on exercise performance and recovery.");
        var blog = new WebSearchDocument(
            "https://jeffreypengmd.com/post/red-light-therapy-muscle-recovery",
            "Red Light Therapy for Muscle Recovery: Does It Actually Work?",
            "Sports medicine perspective on red light therapy recovery claims.");

        var researchScore = scorer.Evaluate(plan, research).Score - policy.Evaluate(plan, research).Penalty;
        var blogScore = scorer.Evaluate(plan, blog).Score - policy.Evaluate(plan, blog).Penalty;

        Assert.True(researchScore > blogScore);
    }

    [Fact]
    public void Evaluate_DemotesPhysioWiki_ForRecoveryEvidenceQuery()
    {
        var scorer = new SourceAuthorityScorer();
        var policy = new SpamAndSeoDemotionPolicy();
        var plan = new WebSearchPlan(
            Query: "photobiomodulation red light therapy muscle recovery after training",
            MaxResults: 5,
            Depth: 1,
            Purpose: "research",
            SearchMode: "standard",
            AllowDeterministicFallback: false);

        var research = new WebSearchDocument(
            "https://link.springer.com/article/10.1007/s10103-025-04318-w",
            "A systematic review on whole-body photobiomodulation for exercise performance and recovery",
            "Systematic review on exercise performance and recovery outcomes.");
        var wiki = new WebSearchDocument(
            "https://physio-pedia.com/Red_Light_Therapy_and_Muscle_Recovery",
            "Red Light Therapy and Muscle Recovery - Physiopedia",
            "Educational overview of red light therapy and recovery.");

        var researchScore = scorer.Evaluate(plan, research).Score - policy.Evaluate(plan, research).Penalty;
        var wikiScore = scorer.Evaluate(plan, wiki).Score - policy.Evaluate(plan, wiki).Penalty;

        Assert.True(researchScore > wikiScore);
    }

    [Fact]
    public void Evaluate_PrefersAcademicPublisherReview_OverResearchGate_ForRecoveryEvidenceQuery()
    {
        var scorer = new SourceAuthorityScorer();
        var policy = new SpamAndSeoDemotionPolicy();
        var plan = new WebSearchPlan(
            Query: "photobiomodulation red light therapy muscle recovery after training",
            MaxResults: 5,
            Depth: 1,
            Purpose: "research",
            SearchMode: "standard",
            AllowDeterministicFallback: false);

        var publisherReview = new WebSearchDocument(
            "https://link.springer.com/article/10.1007/s10103-025-04318-w",
            "A systematic review on whole-body photobiomodulation for exercise performance and recovery",
            "Systematic review on exercise performance and recovery outcomes.");
        var aggregator = new WebSearchDocument(
            "https://researchgate.net/publication/388513692_A_systematic_review_on_whole-body_photobiomodulation_for_exercise_performance_and_recovery",
            "A systematic review on whole-body photobiomodulation for exercise performance and recovery",
            "Secondary aggregator page mirroring the same review.");

        var publisherScore = scorer.Evaluate(plan, publisherReview).Score - policy.Evaluate(plan, publisherReview).Penalty;
        var aggregatorScore = scorer.Evaluate(plan, aggregator).Score - policy.Evaluate(plan, aggregator).Penalty;

        Assert.True(publisherScore > aggregatorScore);
    }
}

