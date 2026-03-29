namespace Helper.Runtime.Core;

public static class ResearchIntentPolicy
{
    public static readonly string[] StrongResearchLexemes =
    {
        "research",
        "investigate",
        "find sources",
        "gather sources",
        "with sources",
        "with citations",
        "with references",
        "credible sources",
        "source-cited",
        "исследуй",
        "собери источники",
        "приведи ссылки",
        "с цитатами",
        "с источниками"
    };

    public static readonly string[] WeakResearchLexemes =
    {
        "compare",
        "analysis",
        "analyze",
        "study",
        "benchmark",
        "observability",
        "tracing",
        "resiliency",
        "сравни",
        "проанализируй",
        "анализ",
        "обзор",
        "исследование"
    };

    public static readonly string[] CitationLexemes =
    {
        "sources",
        "source",
        "citations",
        "citation",
        "references",
        "reference",
        "links",
        "link",
        "источники",
        "источник",
        "ссылки",
        "ссылка",
        "цитаты",
        "цитата"
    };

    public static readonly string[] GenerateLexemes =
    {
        "generate",
        "create",
        "build",
        "implement",
        "write code",
        "scaffold",
        "сгенерируй",
        "создай",
        "построй",
        "реализуй",
        "напиши код"
    };

    public static readonly string[] AllResearchLexemes =
        StrongResearchLexemes
            .Concat(WeakResearchLexemes)
            .Concat(CitationLexemes)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public static int CountStrongResearchSignals(string? text)
    {
        return CountMatches(text, StrongResearchLexemes);
    }

    public static int CountWeakResearchSignals(string? text)
    {
        return CountMatches(text, WeakResearchLexemes);
    }

    public static int CountCitationSignals(string? text)
    {
        return CountMatches(text, CitationLexemes);
    }

    public static int CountGenerateSignals(string? text)
    {
        return CountMatches(text, GenerateLexemes);
    }

    public static int ComputeResearchScore(string? text)
    {
        return (CountStrongResearchSignals(text) * 3)
               + (CountCitationSignals(text) * 2)
               + CountWeakResearchSignals(text);
    }

    public static int ComputeGenerateScore(string? text)
    {
        return CountGenerateSignals(text) * 2;
    }

    public static bool HasExplicitResearchRequest(string? text)
    {
        return CountStrongResearchSignals(text) > 0 || CountCitationSignals(text) > 0;
    }

    public static bool HasExplicitGenerateRequest(string? text)
    {
        return CountGenerateSignals(text) > 0;
    }

    public static bool ShouldRouteToResearch(string? text, int weakThreshold = 2)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var researchScore = ComputeResearchScore(text);
        var generateScore = ComputeGenerateScore(text);
        if (HasExplicitResearchRequest(text))
        {
            return researchScore >= generateScore;
        }

        return researchScore >= Math.Max(weakThreshold, generateScore + 1);
    }

    private static int CountMatches(string? text, IEnumerable<string> lexemes)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        var value = text.Trim();
        var matches = 0;
        foreach (var lexeme in lexemes)
        {
            if (value.Contains(lexeme, StringComparison.OrdinalIgnoreCase))
            {
                matches++;
            }
        }

        return matches;
    }
}

