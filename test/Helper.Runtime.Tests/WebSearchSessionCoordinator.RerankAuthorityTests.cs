using Helper.Runtime.WebResearch;
using Helper.Runtime.WebResearch.Fetching;
using Helper.Runtime.WebResearch.Normalization;
using Helper.Runtime.WebResearch.Quality;
using Helper.Runtime.WebResearch.Ranking;
using Helper.Runtime.WebResearch.Rendering;
using Helper.Runtime.WebResearch.Safety;
using Helper.Testing.WebResearch;

namespace Helper.Runtime.Tests;

[Trait("Lane", "Integration")]
public sealed partial class WebSearchSessionCoordinatorTests
{
    [Fact]
    public async Task ExecuteAsync_AppliesDedicatedReranker_ForPaperAnalysisPrompt()
    {
        var coordinator = WebSearchSessionCoordinatorFactory.Create(new StubProviderClient(_ =>
            new[]
            {
                new WebSearchDocument(
                    "https://example.org/blog/attention-residuals",
                    "Blog note about Attention Residuals",
                    "Short high-level mention of the paper."),
                new WebSearchDocument(
                    "https://arxiv.org/abs/2603.15031",
                    "Attention Residuals",
                    "Abstract and paper metadata for Attention Residuals."),
                new WebSearchDocument(
                    "https://github.com/MoonshotAI/Attention-Residuals/blob/master/Attention_Residuals.pdf",
                    "Attention Residuals PDF",
                    "Direct PDF source for the paper.")
            }));

        var session = await coordinator.ExecuteAsync(
            new WebSearchRequest("Analyze Attention Residuals paper pdf", MaxResults: 3),
            CancellationToken.None);

        Assert.NotEmpty(session.ResultBundle.Documents);
        Assert.NotEqual("https://example.org/blog/attention-residuals", session.ResultBundle.Documents[0].Url);
        Assert.Contains(session.ResultBundle.ProviderTrace!, line => line.Contains("web_search.rerank profile=paper_analysis", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(session.ResultBundle.ProviderTrace!, line => line.Contains("rerank_profile=paper_analysis", StringComparison.OrdinalIgnoreCase));
    }


    [Fact]
    public async Task ExecuteAsync_PrefersAuthoritativeSources_AndDemotesSeoJunk()
    {
        var coordinator = WebSearchSessionCoordinatorFactory.Create(new StubProviderClient(plan =>
        {
            return new[]
            {
                new WebSearchDocument(
                    "https://top10-tools.xyz/best-dotnet-version?utm_source=ads",
                    "Top 10 Best .NET Versions",
                    "Sponsored affiliate ranking."),
                new WebSearchDocument(
                    "https://learn.microsoft.com/dotnet/core/releases-and-support",
                    "Releases and support",
                    "Official .NET release and support documentation."),
                new WebSearchDocument(
                    "https://community.example.org/dotnet-release-overview",
                    "Community release overview",
                    "Community summary of current release notes.")
            };
        }));

        var session = await coordinator.ExecuteAsync(
            new WebSearchRequest("latest .NET SDK release notes", MaxResults: 2),
            CancellationToken.None);

        Assert.Equal(2, session.ResultBundle.Documents.Count);
        Assert.Equal("https://learn.microsoft.com/dotnet/core/releases-and-support", session.ResultBundle.Documents[0].Url);
        Assert.DoesNotContain(session.ResultBundle.Documents, document => document.Url.Contains("top10-tools.xyz", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(session.ResultBundle.ProviderTrace!, line => line.Contains("web_search.ranking[1]", StringComparison.OrdinalIgnoreCase));
    }


    [Fact]
    public async Task ExecuteAsync_DropsInteractiveNoise_ForMedicalEvidenceQuery()
    {
        var coordinator = WebSearchSessionCoordinatorFactory.Create(new StubProviderClient(_ =>
            new[]
            {
                new WebSearchDocument(
                    "https://yandex.ru/games/app/209428",
                    "Объясни слово! - играть онлайн бесплатно на сервисе Яндекс Игры",
                    "Попробуй сказать слово иначе! Это игра для весёлой компании."),
                new WebSearchDocument(
                    "https://www.consultant.ru/document/cons_doc_LAW_489415/",
                    "Клинические рекомендации \"Мигрень\"",
                    "Клинические рекомендации по диагностике и профилактической терапии мигрени."),
                new WebSearchDocument(
                    "https://cyberleninka.ru/article/n/profilakticheskaya-terapiya-migreni-ot-klinicheskih-rekomendatsiy-k-klinicheskoy-praktike",
                    "Профилактическая терапия мигрени: от клинических рекомендаций к клинической практике",
                    "Эффективность профилактической терапии мигрени доказана в клинических испытаниях.")
            }));

        var session = await coordinator.ExecuteAsync(
            new WebSearchRequest(
                "Объясни, как обычно строят профилактику мигрени, а затем проверь, что изменилось или уточнилось в последних клинических рекомендациях.",
                MaxResults: 3),
            CancellationToken.None);

        Assert.DoesNotContain(session.ResultBundle.Documents, document => document.Url.Contains("/games/", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            session.ResultBundle.Documents,
            document => document.Url.Contains("consultant.ru", StringComparison.OrdinalIgnoreCase) ||
                        document.Url.Contains("cyberleninka.ru", StringComparison.OrdinalIgnoreCase));
    }


    [Fact]
    public async Task ExecuteAsync_DropsMailAnswers_ForConflictHealthQuery()
    {
        var coordinator = WebSearchSessionCoordinatorFactory.Create(new StubProviderClient(_ =>
            new[]
            {
                new WebSearchDocument(
                    "https://otvet.mail.ru/question/269218828",
                    "Что вам помогает успокоиться? - Ответы Mail",
                    "Пользовательские ответы и обсуждение."),
                new WebSearchDocument(
                    "https://doctor.rambler.ru/healthylife/55195086-intervalnoe-golodanie-effektivnyy-sposob-snizit-ves",
                    "Интервальное голодание: эффективный способ снизить вес",
                    "Обзор по теме интервального голодания."),
                new WebSearchDocument(
                    "https://medlineplus.gov/ency/patientinstructions/000899.htm",
                    "Weight loss and diet",
                    "Clinical health reference for safe diet and weight loss approaches.")
            }));

        var session = await coordinator.ExecuteAsync(
            new WebSearchRequest(
                "Объясни, помогает ли интервальное голодание снижать вес без потери мышц, если научные и популярные источники расходятся.",
                MaxResults: 3),
            CancellationToken.None);

        Assert.DoesNotContain(session.ResultBundle.Documents, document => document.Url.Contains("otvet.mail.ru", StringComparison.OrdinalIgnoreCase));
        Assert.NotEmpty(session.ResultBundle.Documents);
        Assert.Contains(
            session.ResultBundle.Documents,
            document => document.Url.Contains("medlineplus.gov", StringComparison.OrdinalIgnoreCase) ||
                        document.Url.Contains("rambler.ru", StringComparison.OrdinalIgnoreCase));
    }


    [Fact]
    public async Task ExecuteAsync_DropsSpellingDictionarySite_ForCurrentOutbreakQuery()
    {
        var coordinator = WebSearchSessionCoordinatorFactory.Create(new StubProviderClient(_ =>
            new[]
            {
                new WebSearchDocument(
                    "https://news.un.org/ru/story/2026/02/1467349",
                    "Заболеваемость корью в Европе и Центральной Азии снизилась, но ...",
                    "UN News summary of measles risk and official public-health context."),
                new WebSearchDocument(
                    "https://www.unicef.org/eca/ru/%D0%9F%D1%80%D0%B5%D1%81%D1%81-%D1%80%D0%B5%D0%BB%D0%B8%D0%B7%D1%8B/%D0%B2-%D0%B5%D0%B2%D1%80%D0%BE%D0%BF%D0%B5-%D0%BF%D1%80%D0%BE%D0%B4%D0%BE%D0%BB%D0%B6%D0%B0%D0%B5%D1%82-%D1%80%D0%B0%D1%81%D1%82%D0%B8-%D1%87%D0%B8%D1%81%D0%BB%D0%BE-%D1%81%D0%BB%D1%83%D1%87%D0%B0%D0%B5%D0%B2-%D0%B7%D0%B0%D0%B1%D0%BE%D0%BB%D0%B5%D0%B2%D0%B0%D0%BD%D0%B8%D1%8F-%D0%BA%D0%BE%D1%80%D1%8C%D1%8E",
                    "В Европе продолжает расти число случаев заболевания корью",
                    "UNICEF press release about measles prevention measures."),
                new WebSearchDocument(
                    "https://www.who.int/europe/ru/news/item/11-02-2026-measles-cases-dropped-in-europe-and-central-asia-in-2025-compared-to-the-previous-year--but-the-risk-of-outbreaks-remains---unicef-and-who",
                    "В 2025 г. число случаев заболевания корью в Европе и Центральной Азии ...",
                    "WHO Europe update on measles outbreaks and prevention."),
                new WebSearchDocument(
                    "https://kak-pishetsya.com/%D1%82%D0%B5%D0%BA%D1%83%D1%89%D0%B5%D0%B9",
                    "Текущей как пишется?",
                    "Проверка правописания слова \"текущей\".")
            }));

        var session = await coordinator.ExecuteAsync(
            new WebSearchRequest(
                "Объясни, что сейчас известно о текущей вспышке кори в Европе и какие официальные меры профилактики рекомендуют на сегодня.",
                MaxResults: 4),
            CancellationToken.None);

        Assert.DoesNotContain(session.ResultBundle.Documents, document => document.Url.Contains("kak-pishetsya.com", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(session.ResultBundle.Documents, document => document.Url.Contains("who.int", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(session.ResultBundle.Documents, document => document.Url.Contains("unicef.org", StringComparison.OrdinalIgnoreCase));
    }


    [Fact]
    public async Task ExecuteAsync_PrefersPrimaryMedicalResearch_OverLifestyleHealthMedia()
    {
        var coordinator = WebSearchSessionCoordinatorFactory.Create(new StubProviderClient(_ =>
            new[]
            {
                new WebSearchDocument(
                    "https://doctor.rambler.ru/healthylife/55195086-intervalnoe-golodanie-effektivnyy-sposob-snizit-ves",
                    "Интервальное голодание: эффективный способ снизить вес",
                    "Популярный обзор по теме healthy life."),
                new WebSearchDocument(
                    "https://fitstars.ru/blog/workout/5-zarubezhnyh-bestsellerov-o-tom-kak-trenirovatsya-bez-boli-i-plato",
                    "Выжимка из 5 зарубежных бестселлеров - FitStars",
                    "Фитнес-блог с обзором книг."),
                new WebSearchDocument(
                    "https://pubmed.ncbi.nlm.nih.gov/35565749/",
                    "Intermittent Fasting versus Continuous Calorie Restriction",
                    "Systematic review and meta-analysis of randomized trials."),
                new WebSearchDocument(
                    "https://pmc.ncbi.nlm.nih.gov/articles/PMC9762455/",
                    "Time-restricted eating as a novel strategy for treatment of obesity",
                    "Review of intermittent fasting strategies and body composition outcomes.")
            }));

        var session = await coordinator.ExecuteAsync(
            new WebSearchRequest(
                "Объясни, помогает ли интервальное голодание снижать вес без потери мышц, если научные и популярные источники расходятся.",
                MaxResults: 3),
            CancellationToken.None);

        Assert.Contains(session.ResultBundle.Documents, document => document.Url.Contains("pubmed.ncbi.nlm.nih.gov", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(session.ResultBundle.Documents, document => document.Url.Contains("pmc.ncbi.nlm.nih.gov", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(session.ResultBundle.Documents, document => document.Url.Contains("fitstars.ru", StringComparison.OrdinalIgnoreCase));
    }


    [Fact]
    public async Task ExecuteAsync_ReappliesMedicalConflictAuthorityFloor_OnAggregateDocuments()
    {
        var coordinator = WebSearchSessionCoordinatorFactory.Create(new StubProviderClient(_ =>
            new[]
            {
                new WebSearchDocument(
                    "https://doctor.rambler.ru/healthylife/55195086-intervalnoe-golodanie-effektivnyy-sposob-snizit-ves",
                    "Интервальное голодание: эффективный способ снизить вес",
                    "Популярный обзор по теме healthy life."),
                new WebSearchDocument(
                    "https://fitstars.ru/blog/workout/5-zarubezhnyh-bestsellerov-o-tom-kak-trenirovatsya-bez-boli-i-plato",
                    "Выжимка из 5 зарубежных бестселлеров - FitStars",
                    "Фитнес-блог с обзором книг."),
                new WebSearchDocument(
                    "https://fitness-pro.ru/biblioteka/eksperiment-s-periodicheskim-golodaniem",
                    "Эксперимент с периодическим голоданием | FPA",
                    "Популярный фитнес-разбор."),
                new WebSearchDocument(
                    "https://cyberleninka.ru/article/n/intervalnoe-golodanie-endokrinnye-aspekty",
                    "ИНТЕРВАЛЬНОЕ ГОЛОДАНИЕ: ЭНДОКРИННЫЕ АСПЕКТЫ",
                    "Научный обзор по эндокринным аспектам интервального голодания."),
                new WebSearchDocument(
                    "https://emcmos.ru/upload/pdf/clinical-obesity-review.pdf",
                    "Формула здоровья",
                    "Клинический обзор по ожирению и снижению веса." )
            }));

        var session = await coordinator.ExecuteAsync(
            new WebSearchRequest(
                "Объясни, помогает ли интервальное голодание снижать вес без потери мышц, если научные и популярные источники расходятся.",
                MaxResults: 5),
            CancellationToken.None);

        Assert.Contains(session.ResultBundle.Documents, document => document.Url.Contains("cyberleninka.ru", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(session.ResultBundle.Documents, document => document.Url.Contains("emcmos.ru", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(session.ResultBundle.Documents, document => document.Url.Contains("doctor.rambler.ru", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(session.ResultBundle.Documents, document => document.Url.Contains("fitstars.ru", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(session.ResultBundle.Documents, document => document.Url.Contains("fitness-pro.ru", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(session.ResultBundle.ProviderTrace!, line => line.Contains("web_search.final_selection stage=aggregate", StringComparison.OrdinalIgnoreCase));
    }


    [Fact]
    public async Task ExecuteAsync_DropsChatAndAppLandingPages_ForRedLightRecoveryEvidenceQuery()
    {
        var coordinator = WebSearchSessionCoordinatorFactory.Create(new StubProviderClient(_ =>
            new[]
            {
                new WebSearchDocument(
                    "https://chatgpt.com/",
                    "ChatGPT",
                    "ChatGPT helps with writing, brainstorming and more."),
                new WebSearchDocument(
                    "https://openai.com/index/chatgpt",
                    "Introducing ChatGPT - OpenAI",
                    "Learn about ChatGPT and its capabilities."),
                new WebSearchDocument(
                    "https://play.google.com/store/apps/details?hl=en-GB&id=com.openai.chatgpt",
                    "ChatGPT on Google Play",
                    "Download ChatGPT for Android."),
                new WebSearchDocument(
                    "https://pubmed.ncbi.nlm.nih.gov/37512345/",
                    "Photobiomodulation therapy and exercise recovery",
                    "Systematic review of muscle recovery and performance outcomes."),
                new WebSearchDocument(
                    "https://pmc.ncbi.nlm.nih.gov/articles/PMC1234567/",
                    "Red light therapy for muscle recovery",
                    "Review of randomized trials and recovery markers.")
            }));

        var session = await coordinator.ExecuteAsync(
            new WebSearchRequest(
                "Объясни, насколько убедительны данные о пользе терапии красным светом для восстановления после тренировок.",
                MaxResults: 5),
            CancellationToken.None);

        Assert.DoesNotContain(session.ResultBundle.Documents, document => document.Url.Contains("chatgpt.com", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(session.ResultBundle.Documents, document => document.Url.Contains("openai.com/index/chatgpt", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(session.ResultBundle.Documents, document => document.Url.Contains("play.google.com", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            session.ResultBundle.Documents,
            document => document.Url.Contains("pubmed.ncbi.nlm.nih.gov", StringComparison.OrdinalIgnoreCase) ||
                        document.Url.Contains("pmc.ncbi.nlm.nih.gov", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(session.ResultBundle.ProviderTrace!, line => line.Contains("rerank_profile=medical_evidence", StringComparison.OrdinalIgnoreCase));
    }


    [Fact]
    public async Task ExecuteAsync_DropsLifestyleMagazine_ForMedicalConflictQuery()
    {
        var coordinator = WebSearchSessionCoordinatorFactory.Create(new StubProviderClient(_ =>
            new[]
            {
                new WebSearchDocument(
                    "https://glamour.ru/health/intermittent-fasting-guide",
                    "Интервальное голодание: гид от Glamour",
                    "Lifestyle-материал о пользе и удобстве интервального голодания."),
                new WebSearchDocument(
                    "https://pubmed.ncbi.nlm.nih.gov/35565749/",
                    "Intermittent Fasting versus Continuous Calorie Restriction",
                    "Systematic review and meta-analysis of randomized trials."),
                new WebSearchDocument(
                    "https://pmc.ncbi.nlm.nih.gov/articles/PMC9762455/",
                    "Time-restricted eating as a novel strategy for treatment of obesity",
                    "Review of intermittent fasting strategies and body composition outcomes.")
            }));

        var session = await coordinator.ExecuteAsync(
            new WebSearchRequest(
                "Объясни, помогает ли интервальное голодание снижать вес без потери мышц, если научные и популярные источники расходятся.",
                MaxResults: 3),
            CancellationToken.None);

        Assert.DoesNotContain(session.ResultBundle.Documents, document => document.Url.Contains("glamour.ru", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(session.ResultBundle.ProviderTrace!, line => line.Contains("medical_conflict_authority_floor", StringComparison.OrdinalIgnoreCase));
    }


    [Fact]
    public async Task ExecuteAsync_PrefersAcademicPublisherReview_OverResearchGate_ForRecoveryEvidenceQuery()
    {
        var coordinator = WebSearchSessionCoordinatorFactory.Create(new StubProviderClient(_ =>
            new[]
            {
                new WebSearchDocument(
                    "https://researchgate.net/publication/388513692_A_systematic_review_on_whole-body_photobiomodulation_for_exercise_performance_and_recovery",
                    "A systematic review on whole-body photobiomodulation for exercise performance and recovery",
                    "Secondary aggregator mirror of the same review."),
                new WebSearchDocument(
                    "https://link.springer.com/article/10.1007/s10103-025-04318-w",
                    "A systematic review on whole-body photobiomodulation for exercise performance and recovery",
                    "Systematic review on exercise performance and recovery outcomes."),
                new WebSearchDocument(
                    "https://pmc.ncbi.nlm.nih.gov/articles/PMC12463863/",
                    "The Effect of Photobiomodulation Therapy on Muscle Performance in Volleyball and Football Players",
                    "Meta-analysis of randomized controlled trials on exercise performance and recovery.")
            }));

        var session = await coordinator.ExecuteAsync(
            new WebSearchRequest(
                "Объясни, насколько убедительны данные о пользе терапии красным светом для восстановления после тренировок.",
                MaxResults: 3),
            CancellationToken.None);

        Assert.Contains(session.ResultBundle.Documents, document => document.Url.Contains("link.springer.com", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(session.ResultBundle.Documents, document => document.Url.Contains("pmc.ncbi.nlm.nih.gov", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(session.ResultBundle.Documents, document => document.Url.Contains("researchgate.net", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(session.ResultBundle.ProviderTrace!, line => line.Contains("medical_authority_floor", StringComparison.OrdinalIgnoreCase));
    }


    [Fact]
    public async Task ExecuteAsync_DropsWeakBlog_ForLawRegulationFreshnessQuery()
    {
        var coordinator = WebSearchSessionCoordinatorFactory.Create(new StubProviderClient(_ =>
            new[]
            {
                new WebSearchDocument(
                    "https://random-blog.example.com/gdpr-guide",
                    "Simple GDPR guide",
                    "General blog summary of GDPR."),
                new WebSearchDocument(
                    "https://gdpr-info.eu/",
                    "GDPR text",
                    "Consolidated legal text and articles."),
                new WebSearchDocument(
                    "https://ec.europa.eu/commission/presscorner/gdpr-update",
                    "Commission update",
                    "Official regulatory update.")
            }));

        var session = await coordinator.ExecuteAsync(
            new WebSearchRequest("latest GDPR compliance enforcement guidance", MaxResults: 3),
            CancellationToken.None);

        Assert.DoesNotContain(session.ResultBundle.Documents, document => document.Url.Contains("random-blog.example.com", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(session.ResultBundle.ProviderTrace!, line => line.Contains("law_regulation_authority_floor", StringComparison.OrdinalIgnoreCase));
    }


    [Fact]
    public async Task ExecuteAsync_DropsWeakBlog_ForFinanceFreshnessQuery()
    {
        var coordinator = WebSearchSessionCoordinatorFactory.Create(new StubProviderClient(_ =>
            new[]
            {
                new WebSearchDocument(
                    "https://my-crypto-blog.example.com/etf-soon",
                    "ETF soon?",
                    "Personal opinion blog."),
                new WebSearchDocument(
                    "https://sec.gov/news/statement/spot-bitcoin-etf",
                    "SEC statement",
                    "Official statement on ETF approval path."),
                new WebSearchDocument(
                    "https://bloomberg.com/crypto/spot-bitcoin-etf",
                    "Bloomberg market note",
                    "Market context for ETF approval.")
            }));

        var session = await coordinator.ExecuteAsync(
            new WebSearchRequest("latest SEC guidance for spot bitcoin ETF approval", MaxResults: 3),
            CancellationToken.None);

        Assert.DoesNotContain(session.ResultBundle.Documents, document => document.Url.Contains("my-crypto-blog.example.com", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(session.ResultBundle.ProviderTrace!, line => line.Contains("finance_market_authority_floor", StringComparison.OrdinalIgnoreCase));
    }


}
