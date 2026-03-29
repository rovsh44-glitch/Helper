namespace Helper.Runtime.WebResearch.Extraction;

internal static class WebChromePatternCatalog
{
    private static readonly string[] Markers =
    {
        "skip to content",
        "sign in",
        "sign up",
        "create account",
        "contact sales",
        "cookie settings",
        "cookie policy",
        "privacy policy",
        "terms of service",
        "all rights reserved",
        "navigation menu",
        "enterprise platform",
        "advanced security",
        "marketplace",
        "saved searches",
        "search code",
        "pull requests",
        "github advanced security",
        "gitlab docs",
        "blob",
        "history",
        "permalink",
        "raw",
        "issues",
        "stars",
        "forks",
        "community",
        "discussion",
        "comments",
        "q&a",
        "otvet.mail.ru",
        "yandex q"
    };

    public static int CountMatches(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        var count = 0;
        foreach (var marker in Markers)
        {
            if (text.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                count++;
            }
        }

        return count;
    }

    public static bool Matches(string? text)
    {
        return CountMatches(text) > 0;
    }
}

