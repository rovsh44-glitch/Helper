using System.Text.RegularExpressions;

namespace Helper.Runtime.WebResearch;

public sealed record ResearchRequestProfile(
    bool MentionsUrl,
    bool IsDocumentAnalysis,
    bool WantsOpinion,
    bool LooksLikePaperOrArticle,
    bool LooksLikeDirectDocumentUrl);

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

    public static ResearchRequestProfile From(string? requestText)
    {
        var text = (requestText ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return new ResearchRequestProfile(false, false, false, false, false);
        }

        var lower = text.ToLowerInvariant();
        var mentionsUrl = UrlRegex().IsMatch(text);
        var looksLikeDirectDocumentUrl = DirectDocumentUrlRegex().IsMatch(text);
        var hasAnalysisSignal = ContainsAny(lower, AnalysisSignals);
        var wantsOpinion = ContainsAny(lower, OpinionSignals);
        var looksLikePaperOrArticle = looksLikeDirectDocumentUrl || ContainsAny(lower, DocumentSignals);
        var isDocumentAnalysis = mentionsUrl && (hasAnalysisSignal || wantsOpinion || looksLikePaperOrArticle);

        return new ResearchRequestProfile(
            MentionsUrl: mentionsUrl,
            IsDocumentAnalysis: isDocumentAnalysis,
            WantsOpinion: wantsOpinion || isDocumentAnalysis,
            LooksLikePaperOrArticle: looksLikePaperOrArticle || isDocumentAnalysis,
            LooksLikeDirectDocumentUrl: looksLikeDirectDocumentUrl);
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

    [GeneratedRegex(@"https?://[^\s\)\]\}>]+", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex UrlRegex();

    [GeneratedRegex(@"https?://[^\s\)\]\}>]+\.(pdf|txt|md)([\?#][^\s\)\]\}>]*)?$", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex DirectDocumentUrlRegex();
}

