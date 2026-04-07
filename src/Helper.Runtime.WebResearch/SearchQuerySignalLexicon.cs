namespace Helper.Runtime.WebResearch;

internal static class SearchQuerySignalLexicon
{
    internal static readonly string[] CurrentnessTokens =
    {
        "latest", "current", "today", "this week", "this month", "news", "price", "forecast", "update", "updates", "fresh", "freshness",
        "последн", "актуаль", "сегодня", "на этой неделе", "в этом месяце", "новости", "новост", "цена", "прогноз", "обновлен", "свеж"
    };

    internal static readonly string[] ComparisonTokens =
    {
        "compare", "comparison", "vs", "versus", "best", "top",
        "сравни", "сравнение", "лучший", "топ"
    };

    internal static readonly string[] ContradictionTokens =
    {
        "conflict", "conflicting", "contradiction", "disagree", "disagreement", "controversy",
        "different conclusions", "different findings", "different results",
        "противореч", "расхожд", "расход", "конфликт", "спор", "спорн", "разные выводы", "разные результаты"
    };

    internal static readonly string[] BreadthTokens =
    {
        "overview", "landscape", "state of", "what is known", "background", "basics", "overall", "general",
        "обзор", "общ", "что известно", "общая картина", "основы", "в целом"
    };

    internal static readonly string[] AmbiguityTokens =
    {
        "help", "helps", "worth it", "good", "better", "works", "effective", "benefit",
        "помога", "насколько", "лучше", "эффектив", "польз", "стоит ли"
    };

    internal static readonly string[] PaperTokens =
    {
        "paper", "pdf", "article", "preprint", "manuscript", "abstract", "full text",
        "systematic review", "literature review", "narrative review", "meta-analysis", "meta analysis",
        "пейпер", "статья", "pdf", "препринт", "аннотация", "полный текст",
        "систематический обзор", "обзор литературы", "мета-анализ", "метаанализ"
    };

    internal static bool ContainsAny(string text, IEnumerable<string> tokens)
    {
        foreach (var token in tokens)
        {
            if (text.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}

