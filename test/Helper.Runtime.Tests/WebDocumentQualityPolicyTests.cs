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
}

