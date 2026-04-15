using Helper.Runtime.WebResearch;
using Helper.Runtime.WebResearch.Quality;

namespace Helper.Runtime.Tests;

public sealed class WebDocumentQualityPolicyTests
{
    [Fact]
    public void Evaluate_RejectsMachineDiagnosticSnippet()
    {
        var policy = new WebDocumentQualityPolicy();

        var decision = policy.Evaluate(
            new WebSearchDocument(
                "https://www.kaggle.com/competitions/kaggle-measuring-agi",
                "Kaggle competition",
                "Unexpected token , \"!doctype \"... is not valid JSON. SyntaxError: Unexpected token , \"!doctype \"... is not valid JSON."),
            "provider");

        Assert.False(decision.Allowed);
        Assert.Equal("diagnostic_or_shell_content", decision.Reason);
        Assert.Contains(decision.Signals, signal => signal.Contains("machine_error", StringComparison.Ordinal));
    }

    [Fact]
    public void Evaluate_AllowsNormalResearchSnippet()
    {
        var policy = new WebDocumentQualityPolicy();

        var decision = policy.Evaluate(
            new WebSearchDocument(
                "https://example.org/article",
                "Measuring progress toward AGI",
                "The article argues that benchmark design should focus on transferable cognitive abilities and on explicit evaluation boundaries."),
            "provider");

        Assert.True(decision.Allowed);
    }

    [Fact]
    public void Evaluate_RejectsGitHubChrome_ForDocumentAnalysisPrompt()
    {
        var policy = new WebDocumentQualityPolicy();

        var decision = policy.Evaluate(
            new WebSearchDocument(
                "https://github.com/MoonshotAI/Attention-Residuals/blob/master/Attention_Residuals.pdf",
                "Attention_Residuals.pdf at master",
                "GitHub Advanced Security Enterprise platform AI-powered developer platform Marketplace Saved searches Search code, repositories, users, issues, pull requests..."),
            "page",
            "Проанализируй и предоставь своё мнение: https://github.com/MoonshotAI/Attention-Residuals/blob/master/Attention_Residuals.pdf");

        Assert.False(decision.Allowed);
        Assert.Contains(decision.Signals, signal => signal.Contains("site_chrome_markers", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(decision.Signals, signal => signal.Contains("document_analysis_without_document_content", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Evaluate_AllowsSubstantivePdfDocument_ForDocumentAnalysisPrompt()
    {
        var policy = new WebDocumentQualityPolicy();

        var decision = policy.Evaluate(
            new WebSearchDocument(
                "https://example.org/paper.pdf",
                "Attention Residuals",
                "Attention residuals replace fixed residual accumulation in transformers.",
                ExtractedPage: new ExtractedWebPage(
                    "https://example.org/paper.pdf",
                    "https://example.org/paper.pdf",
                    "https://example.org/paper.pdf",
                    "Attention Residuals",
                    "2026",
                    "Attention residuals replace fixed residual accumulation and report scaling improvements over baseline residual connections.",
                    new[]
                    {
                        new ExtractedWebPassage(1, "Attention residuals replace fixed residual accumulation and report scaling improvements over baseline residual connections.")
                    },
                    "application/pdf")),
            "page",
            "Analyze this paper and give your opinion: https://example.org/paper.pdf");

        Assert.True(decision.Allowed);
    }

    [Fact]
    public void Evaluate_RejectsInteractiveNoise_ForEvidenceHeavyMedicalQuery()
    {
        var policy = new WebDocumentQualityPolicy();

        var decision = policy.Evaluate(
            new WebSearchDocument(
                "https://yandex.ru/games/app/209428",
                "Объясни слово! - играть онлайн бесплатно на сервисе Яндекс Игры",
                "Попробуй сказать слово иначе! Это игра для весёлой компании."),
            "provider",
            "Объясни, как обычно строят профилактику мигрени, а затем проверь, что изменилось или уточнилось в последних клинических рекомендациях.");

        Assert.False(decision.Allowed);
        Assert.Contains(decision.Signals, signal => signal.Contains("interactive_or_ugc_for_evidence_query", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Evaluate_RejectsMailAnswers_ForEvidenceHeavyMedicalQuery()
    {
        var policy = new WebDocumentQualityPolicy();

        var decision = policy.Evaluate(
            new WebSearchDocument(
                "https://otvet.mail.ru/question/269218828",
                "Что вам помогает успокоиться? - Ответы Mail",
                "Пользовательские ответы и обсуждение."),
            "provider",
            "Объясни, помогает ли интервальное голодание снижать вес без потери мышц, если научные и популярные источники расходятся.");

        Assert.False(decision.Allowed);
        Assert.Contains(decision.Signals, signal => signal.Contains("interactive_or_ugc_for_evidence_query", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Evaluate_RejectsSpellingDictionarySite_ForCurrentMedicalEvidenceQuery()
    {
        var policy = new WebDocumentQualityPolicy();

        var decision = policy.Evaluate(
            new WebSearchDocument(
                "https://kak-pishetsya.com/%D1%82%D0%B5%D0%BA%D1%83%D1%89%D0%B5%D0%B9",
                "Текущей как пишется?",
                "Проверка орфографии и правописания слова \"текущей\"."),
            "provider",
            "Объясни, что сейчас известно о текущей вспышке кори в Европе и какие официальные меры профилактики рекомендуют на сегодня.");

        Assert.False(decision.Allowed);
        Assert.Contains(decision.Signals, signal => signal.Contains("interactive_or_ugc_for_evidence_query", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Evaluate_AllowsClinicalResult_WithMorphologyMismatch_WhenUrlAndTitleAreRelevant()
    {
        var policy = new WebDocumentQualityPolicy();

        var decision = policy.Evaluate(
            new WebSearchDocument(
                "https://diseases.medelement.com/disease/%D0%BC%D0%B8%D0%B3%D1%80%D0%B5%D0%BD%D1%8C-%D0%BA%D1%80-%D1%80%D1%84-2024/18269",
                "Мигрень у взрослых > Клинические рекомендации РФ 2024",
                "Мигрень - первичная форма головной боли. В документе описаны подходы к профилактической терапии."),
            "provider",
            "Объясни, как обычно строят профилактику мигрени, а затем проверь, что изменилось или уточнилось в последних клинических рекомендациях.");

        Assert.True(decision.Allowed);
    }

    [Fact]
    public void Evaluate_RejectsAntiBotInterstitialPage_EvenWhenUrlLooksRelevant()
    {
        var policy = new WebDocumentQualityPolicy();

        var decision = policy.Evaluate(
            new WebSearchDocument(
                "https://researchgate.net/publication/396037552_THE_EFFECT_OF_RED_LIGHT_THERAPY_PHOTOBIOMODULATION_ON_MUSCLE_RECOVERY_AND_PHYSICAL_PERFORMANCE_IN_ATHLETES",
                "Just a moment...",
                "We've detected unusual activity from your network. To continue, complete the security check below.",
                ExtractedPage: new ExtractedWebPage(
                    "https://researchgate.net/publication/396037552_THE_EFFECT_OF_RED_LIGHT_THERAPY_PHOTOBIOMODULATION_ON_MUSCLE_RECOVERY_AND_PHYSICAL_PERFORMANCE_IN_ATHLETES",
                    "https://researchgate.net/publication/396037552_THE_EFFECT_OF_RED_LIGHT_THERAPY_PHOTOBIOMODULATION_ON_MUSCLE_RECOVERY_AND_PHYSICAL_PERFORMANCE_IN_ATHLETES",
                    "https://researchgate.net/publication/396037552_THE_EFFECT_OF_RED_LIGHT_THERAPY_PHOTOBIOMODULATION_ON_MUSCLE_RECOVERY_AND_PHYSICAL_PERFORMANCE_IN_ATHLETES",
                    "Just a moment...",
                    null,
                    "We've detected unusual activity from your network. To continue, complete the security check below. Verification successful. Waiting for www.researchgate.net to respond.",
                    new[]
                    {
                        new ExtractedWebPassage(1, "We've detected unusual activity from your network. To continue, complete the security check below.")
                    },
                    "text/html")),
            "page",
            "Объясни, насколько убедительны данные о пользе терапии красным светом для восстановления после тренировок.");

        Assert.False(decision.Allowed);
        Assert.Contains(decision.Signals, signal => signal.Contains("anti_bot_interstitial", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Evaluate_RejectsLowOverlapSource_ForOfficialFreshnessQuery()
    {
        var policy = new WebDocumentQualityPolicy();

        var decision = policy.Evaluate(
            new WebSearchDocument(
                "https://wikileaks.org/ciav7p1",
                "Vault 7: CIA Hacking Tools Revealed - WikiLeaks",
                "Press Release Today, Tuesday 7 March 2017, WikiLeaks begins its new series of leaks on the U.S. Central Intelligence Agency."),
            "provider",
            "Проверь, актуальны ли налоговые thresholds и reporting deadlines, которыми я пользуюсь сегодня.");

        Assert.False(decision.Allowed);
        Assert.Contains(decision.Signals, signal => signal.Contains("low_query_overlap", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Evaluate_RejectsLowTrustAggregator_ForOfficialFreshnessQuery_EvenWithTopicOverlap()
    {
        var policy = new WebDocumentQualityPolicy();

        var decision = policy.Evaluate(
            new WebSearchDocument(
                "https://cont.ws/@oolegov2025/3088229",
                "Налоги для трейдера в 2026: ужесточение правил и новые риски",
                "В 2026 году российская налоговая система получает масштабное обновление, меняются лимиты и сроки отчетности."),
            "provider",
            "Проверь, актуальны ли налоговые thresholds и reporting deadlines, которыми я пользуюсь сегодня.");

        Assert.False(decision.Allowed);
        Assert.Contains(decision.Signals, signal => signal.Contains("low_trust_for_freshness_or_official_query", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Evaluate_AllowsTrustedLegalReference_ForOfficialFreshnessQuery()
    {
        var policy = new WebDocumentQualityPolicy();

        var decision = policy.Evaluate(
            new WebSearchDocument(
                "https://www.consultant.ru/document/cons_doc_LAW_28165/",
                "Сроки сдачи отчетности и налоговые лимиты",
                "Официальные сроки отчетности, лимиты и налоговые требования на текущий период."),
            "provider",
            "Проверь, актуальны ли налоговые thresholds и reporting deadlines, которыми я пользуюсь сегодня.");

        Assert.True(decision.Allowed);
    }

    [Fact]
    public void Evaluate_RejectsGoogleMapsSupport_ForVisaRegulationQuery()
    {
        var policy = new WebDocumentQualityPolicy();

        var decision = policy.Evaluate(
            new WebSearchDocument(
                "https://support.google.com/maps/answer/144339?co=GENIE.Platform%3DDesktop&hl=en",
                "Use navigation in Google Maps",
                "Get directions and use Google Maps navigation on desktop and Android."),
            "provider",
            "Сравни актуальные визовые пути для software engineer, который хочет переехать в Германию в 2026 году.");

        Assert.False(decision.Allowed);
        Assert.Contains(decision.Signals, signal => signal.Contains("low_query_overlap", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Evaluate_RejectsJingyanNoise_ForSparseTechnicalComparisonQuery()
    {
        var policy = new WebDocumentQualityPolicy();

        var decision = policy.Evaluate(
            new WebSearchDocument(
                "https://jingyan.baidu.com/article/2a13832857c2cc464a134f9f.html",
                "Vector Magic怎么用，图片转CAD图操作教程-百度经验",
                "Step by step how-to article on converting images into CAD diagrams."),
            "provider",
            "Сравни vector databases и классический search для маленькой команды, если большая часть benchmark-ов vendor-shaped.");

        Assert.False(decision.Allowed);
        Assert.Contains(decision.Signals, signal => signal.Contains("interactive_or_ugc_for_evidence_query", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Evaluate_AllowsUzbekistanOfficialTaxSource_ForFilingChecklistQuery()
    {
        var policy = new WebDocumentQualityPolicy();

        var decision = policy.Evaluate(
            new WebSearchDocument(
                "https://soliq.uz/page/foreign-income-reporting",
                "Отчетность и налоговые требования для физлиц и ИП",
                "Налоговая отчетность, сроки подачи, требования к инвойсам и правила работы с иностранными клиентами."),
            "provider",
            "Составь актуальный filing checklist для remote worker, который выставляет инвойсы иностранным клиентам из Узбекистана в 2026 году.");

        Assert.True(decision.Allowed);
    }

    [Fact]
    public void Evaluate_AllowsTrustedUzbekistanTaxReference_ForFilingChecklistQuery()
    {
        var policy = new WebDocumentQualityPolicy();

        var decision = policy.Evaluate(
            new WebSearchDocument(
                "https://buxgalter.uz/publish/doc/text211019_remote-worker-invoices-foreign-clients",
                "Налогообложение инвойсов иностранным клиентам в Узбекистане",
                "Checklist по отчетности, инвойсам, срокам подачи и требованиям для работы с иностранными клиентами."),
            "provider",
            "Составь актуальный filing checklist для remote worker, который выставляет инвойсы иностранным клиентам из Узбекистана в 2026 году.");

        Assert.True(decision.Allowed);
    }

    [Fact]
    public void Evaluate_AllowsTrustedTaxAlertReference_ForUzbekistanFilingChecklistQuery()
    {
        var policy = new WebDocumentQualityPolicy();

        var decision = policy.Evaluate(
            new WebSearchDocument(
                "https://www.ey.com/en_uz/technical/tax-alerts/2026/01/uzbekistan-tax-updates-2026",
                "Uzbekistan tax updates 2026",
                "Technical tax alert covering filing, reporting deadlines, invoicing obligations and foreign-client tax treatment in Uzbekistan."),
            "provider",
            "Составь актуальный filing checklist для remote worker, который выставляет инвойсы иностранным клиентам из Узбекистана в 2026 году.");

        Assert.True(decision.Allowed);
    }

    [Fact]
    public void Evaluate_AllowsGovUzAdviceDocument_ForUzbekistanFilingChecklistQuery()
    {
        var policy = new WebDocumentQualityPolicy();

        var decision = policy.Evaluate(
            new WebSearchDocument(
                "https://gov.uz/ru/advice/75/document/1812",
                "Разъяснение по документу",
                "Официальный advisory-документ для иностранных доходов, сервисов и административных требований."),
            "provider",
            "Составь актуальный filing checklist для remote worker, который выставляет инвойсы иностранным клиентам из Узбекистана в 2026 году.");

        Assert.True(decision.Allowed);
        Assert.DoesNotContain(decision.Signals, signal => signal.Contains("regulatory_source_mismatch", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Evaluate_AllowsOfficialGermanyImmigrationPortal_ForVisaComparisonQuery()
    {
        var policy = new WebDocumentQualityPolicy();

        var decision = policy.Evaluate(
            new WebSearchDocument(
                "https://deutschland.de/en/topic/work/germany-blue-card-skilled-workers",
                "Working in Germany: EU Blue Card and skilled workers",
                "Official Germany portal overview of Blue Card, skilled worker visa routes, residence permits and work authorization."),
            "provider",
            "Сравни актуальные визовые пути для software engineer, который хочет переехать в Германию в 2026 году.");

        Assert.True(decision.Allowed);
    }

    [Fact]
    public void Evaluate_AllowsOfficialEuAiActSource_ForFreshRegulationQuery()
    {
        var policy = new WebDocumentQualityPolicy();

        var decision = policy.Evaluate(
            new WebSearchDocument(
                "https://digital-strategy.ec.europa.eu/en/policies/regulatory-framework-ai",
                "EU AI Act: regulatory framework and provider obligations",
                "European Commission guidance on the AI Act, provider obligations, implementation guidance and AI Office updates."),
            "provider",
            "Объясни, что последние изменения в регулировании ИИ в ЕС означают сегодня для маленького software vendor.");

        Assert.True(decision.Allowed);
        Assert.DoesNotContain(decision.Signals, signal => signal.Contains("regulatory_source_mismatch", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Evaluate_AllowsOfficialEuCustomsDroneSource_ForFreshRegulationQuery()
    {
        var policy = new WebDocumentQualityPolicy();

        var decision = policy.Evaluate(
            new WebSearchDocument(
                "https://easa.europa.eu/en/the-agency/faqs/drones",
                "Drones | Frequently Asked Questions",
                "Official EASA guidance on drone import, customs, batteries, VAT, CE marking and travel within the EU."),
            "provider",
            "Проверь, актуальны ли import restrictions и customs rules для ввоза дрона в ЕС на сегодня.");

        Assert.True(decision.Allowed);
        Assert.DoesNotContain(decision.Signals, signal => signal.Contains("regulatory_source_mismatch", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Evaluate_RejectsCommentCaMarcheNoise_ForDroneCustomsQuery()
    {
        var policy = new WebDocumentQualityPolicy();

        var decision = policy.Evaluate(
            new WebSearchDocument(
                "https://forums.commentcamarche.net/forum/affich-35995097-desabonnement-wetransfer",
                "Désabonnement wetransfer - Consommation & Internet",
                "Forum thread unrelated to customs rules, imports, drones or EU guidance."),
            "provider",
            "Проверь, актуальны ли import restrictions и customs rules для ввоза дрона в ЕС на сегодня.");

        Assert.False(decision.Allowed);
        Assert.Contains(decision.Signals, signal => signal.Contains("low_trust", StringComparison.OrdinalIgnoreCase) ||
                                                   signal.Contains("interactive_or_ugc", StringComparison.OrdinalIgnoreCase));
    }
}

