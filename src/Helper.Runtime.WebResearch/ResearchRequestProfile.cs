using System.Text.RegularExpressions;
using Helper.Runtime.WebResearch.Ranking;

namespace Helper.Runtime.WebResearch;

public sealed record ResearchRequestProfile(
    bool MentionsUrl,
    bool IsDocumentAnalysis,
    bool WantsOpinion,
    bool LooksLikePaperOrArticle,
    bool LooksLikeDirectDocumentUrl,
    bool StrictLiveEvidenceRequired);

public static partial class ResearchRequestProfileResolver
{
    private static readonly string[] AnalysisSignals =
    {
        "analyze", "analysis", "review", "read and analyze", "summarize and assess", "what do you think",
        "проанализируй", "разбери", "разбор", "проанализировать", "анализ", "оценка", "что думаешь"
    };

    private static readonly string[] OpinionSignals =
    {
        "your opinion", "what do you think", "assessment", "critique", "strengths", "weaknesses",
        "своё мнение", "твое мнение", "ваше мнение", "что думаешь", "оценка", "сильные стороны", "слабые стороны", "ограничения"
    };

    private static readonly string[] DocumentSignals =
    {
        "paper", "article", "pdf", "report", "document", "whitepaper", "manuscript", "study",
        "пейпер", "статья", "pdf", "документ", "отчёт", "отчет", "исследование", "работа", "рукопись"
    };

    private static readonly string[] RetractionSignals =
    {
        "retracted", "retraction", "retract", "withdrawn", "withdrawal", "erratum", "correction", "corrected", "expression of concern", "contested", "disputed",
        "отозван", "отозвана", "отозваны", "ретракц", "исправлен", "исправлена", "исправлены", "эррат", "выражение обеспокоенности", "оспорен", "оспорена", "оспорены"
    };

    private static readonly string[] RegulationSignals =
    {
        "tax", "taxes", "threshold", "thresholds", "deadline", "deadlines", "reporting", "filing", "compliance", "regulation",
        "ai act", "artificial intelligence act", "provider obligations", "implementation guidance",
        "customs", "import", "import restrictions", "vat", "duty", "battery", "batteries", "drone", "easa", "ce marking",
        "visa", "visas", "immigration", "migration", "relocation", "residence permit", "work permit", "blue card", "bluecard", "skilled worker",
        "налог", "налоги", "налогов", "порог", "пороги", "лимит", "лимиты", "срок", "сроки", "отчетност", "отчётност", "регуляц",
        "тамож", "ввоз", "дрон", "батаре", "ндс", "пошлин", "маркировк", "ии акт", "обязательства провайдера",
        "виза", "визы", "визовые", "миграц", "релокац", "внж", "вид на жительство", "разрешение на работу", "голубая карта"
    };

    private static readonly string[] OperationalRegulationSignals =
    {
        "threshold", "thresholds", "deadline", "deadlines", "reporting", "filing", "compliance", "requirement", "requirements",
        "limit", "limits", "rate", "rates", "vat", "duty", "customs", "import restrictions", "visa", "visas", "work permit", "residence permit",
        "порог", "пороги", "лимит", "лимиты", "срок", "сроки", "отчетност", "отчётност", "требован", "ставк", "ндс", "пошлин",
        "тамож", "ввоз", "визовые", "виза", "визы", "внж", "разрешение на работу"
    };

    public static ResearchRequestProfile From(string? requestText)
    {
        var text = (requestText ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return new ResearchRequestProfile(false, false, false, false, false, false);
        }

        var lower = text.ToLowerInvariant();
        var queryProfile = SourceAuthorityScorer.BuildQueryProfile(text, null);
        var mentionsUrl = UrlRegex().IsMatch(text);
        var looksLikeDirectDocumentUrl = DirectDocumentUrlRegex().IsMatch(text);
        var hasAnalysisSignal = ContainsAny(lower, AnalysisSignals);
        var wantsOpinion = ContainsAny(lower, OpinionSignals);
        var looksLikePaperOrArticle = looksLikeDirectDocumentUrl || ContainsAny(lower, DocumentSignals);
        var isDocumentAnalysis = mentionsUrl && (hasAnalysisSignal || wantsOpinion || looksLikePaperOrArticle);
        var freshnessSensitive = queryProfile.CurrentnessHeavy || SearchQuerySignalLexicon.ContainsAny(text, SearchQuerySignalLexicon.CurrentnessTokens);
        var comparisonSensitive = queryProfile.ComparisonHeavy || SearchQuerySignalLexicon.ContainsAny(text, SearchQuerySignalLexicon.ComparisonTokens);
        var looksLikeRetractionStatus = ContainsAny(lower, RetractionSignals);
        var looksLikeRegulationFreshness = ContainsAny(lower, RegulationSignals);
        var looksLikeOperationalRegulation = ContainsAny(lower, OperationalRegulationSignals);
        var looksLikeEuAiRegulation = LooksLikeEuAiRegulationPrompt(lower);
        var strictLiveEvidenceRequired =
            (looksLikeRetractionStatus && (freshnessSensitive || looksLikePaperOrArticle)) ||
            (looksLikeRegulationFreshness && (freshnessSensitive || comparisonSensitive || looksLikeOperationalRegulation)) ||
            looksLikeEuAiRegulation;

        return new ResearchRequestProfile(
            MentionsUrl: mentionsUrl,
            IsDocumentAnalysis: isDocumentAnalysis,
            WantsOpinion: wantsOpinion || isDocumentAnalysis,
            LooksLikePaperOrArticle: looksLikePaperOrArticle || isDocumentAnalysis,
            LooksLikeDirectDocumentUrl: looksLikeDirectDocumentUrl,
            StrictLiveEvidenceRequired: strictLiveEvidenceRequired);
    }

    private static bool ContainsAny(string text, IReadOnlyList<string> signals)
    {
        foreach (var signal in signals)
        {
            if (text.Contains(signal, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool LooksLikeEuAiRegulationPrompt(string text)
    {
        return (text.Contains("ai act", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("artificial intelligence act", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("регулирован", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("регулирование ии", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("регулировании ии", StringComparison.OrdinalIgnoreCase)) &&
               (text.Contains("eu", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("ес", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("евросою", StringComparison.OrdinalIgnoreCase));
    }

    [GeneratedRegex(@"https?://[^\s\)\]\}>]+", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex UrlRegex();

    [GeneratedRegex(@"https?://[^\s\)\]\}>]+\.(pdf|txt|md)([\?#][^\s\)\]\}>]*)?$", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex DirectDocumentUrlRegex();
}

