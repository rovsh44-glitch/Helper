using Helper.Runtime.Core;

namespace Helper.Api.Conversation;

internal static class ResearchSourceCategoryClassifier
{
    public static string Resolve(ResearchEvidenceItem evidenceItem)
    {
        return Resolve(
            evidenceItem.Title ?? string.Empty,
            evidenceItem.Url ?? string.Empty,
            evidenceItem.Snippet ?? string.Empty,
            evidenceItem.EvidenceKind);
    }

    public static string Resolve(string title, string url, string text, string? evidenceKind = null)
    {
        var corpus = $"{title} {text} {url} {evidenceKind ?? string.Empty}";

        if (ContainsAny(corpus, "release note", "release notes", "releases", "version"))
        {
            return "release_note";
        }

        if (ContainsAny(corpus, "fact sheet", "fact-sheets", "fact-sheet", "faq", "measles", "корь"))
        {
            return url.Contains("/fact-sheets/", StringComparison.OrdinalIgnoreCase) ||
                   url.Contains("/fact-sheet", StringComparison.OrdinalIgnoreCase) ||
                   title.Contains("Корь", StringComparison.OrdinalIgnoreCase)
                ? "fact_sheet"
                : "general_reference";
        }

        if (ContainsAny(corpus, "guideline", "guidelines", "recommendation", "recommendations", "clinical", "клиничес", "рекомендац"))
        {
            return "clinical_guidance";
        }

        if (ContainsAny(corpus, "systematic review", "meta-analysis", "randomized", "trial", "cohort", "pubmed", "pmc", "cochrane"))
        {
            return "primary_research";
        }

        if (ContainsAny(corpus, "/news/", "news.un.org", "/story/", "announced"))
        {
            return "timely_news";
        }

        return "general_reference";
    }

    public static string ResolveFromMatcherText(string matcherText)
    {
        return Resolve(matcherText, matcherText, matcherText, matcherText);
    }

    public static bool SupportsYearConflict(string category)
    {
        return category is "release_note" or "clinical_guidance" or "primary_research";
    }

    public static bool SupportsNumericConflict(string category)
    {
        return category is "release_note" or "timely_news" or "clinical_guidance" or "primary_research";
    }

    public static bool IsEvidenceFamily(string category)
    {
        return category is "clinical_guidance" or "primary_research" or "general_reference";
    }

    private static bool ContainsAny(string text, params string[] values)
    {
        foreach (var value in values)
        {
            if (text.Contains(value, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}

