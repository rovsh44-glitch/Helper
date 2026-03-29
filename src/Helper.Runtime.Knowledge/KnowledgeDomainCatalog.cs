using System.Text.RegularExpressions;

namespace Helper.Runtime.Knowledge;

public static class KnowledgeDomainCatalog
{
    private static readonly HashSet<string> CanonicalDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        "analysis_strategy",
        "anatomy",
        "art_culture",
        "biology",
        "chemistry",
        "computer_science",
        "economics",
        "encyclopedias",
        "english_lang_lit",
        "entomology",
        "generic",
        "geology",
        "historical_encyclopedias",
        "history",
        "linguistics",
        "math",
        "medicine",
        "mythology_religion",
        "neuro",
        "philosophy",
        "physics",
        "psychology",
        "robotics",
        "russian_lang_lit",
        "sci_fi_concepts",
        "social_sciences",
        "virology"
    };

    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["computerscience"] = "computer_science",
        ["programming"] = "computer_science",
        ["software_engineering"] = "computer_science",
        ["coding"] = "computer_science",
        ["historical_encyclopedia"] = "historical_encyclopedias",
        ["neuroscience"] = "neuro",
        ["mathematics"] = "math",
        ["encyclopedia"] = "encyclopedias",
        ["history_fix"] = "history",
        ["social_science"] = "social_sciences",
        ["socialscience"] = "social_sciences",
        ["socialsciences"] = "social_sciences",
        ["sociology"] = "social_sciences",
        ["religion"] = "mythology_religion",
        ["mythology"] = "mythology_religion"
    };

    public static IReadOnlyCollection<string> AllowedDomains => CanonicalDomains;

    public static bool IsCanonical(string? domain)
        => !string.IsNullOrWhiteSpace(domain) && CanonicalDomains.Contains(domain);

    public static string Normalize(string? domain)
    {
        if (string.IsNullOrWhiteSpace(domain))
        {
            return "generic";
        }

        var normalized = domain.Trim().ToLowerInvariant();
        normalized = Regex.Replace(normalized, "[^a-z0-9_\\-\\s]", string.Empty);
        normalized = normalized.Replace(" ", "_", StringComparison.Ordinal)
            .Replace("-", "_", StringComparison.Ordinal);
        normalized = Regex.Replace(normalized, "_{2,}", "_").Trim('_');

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "generic";
        }

        if (Aliases.TryGetValue(normalized, out var mapped))
        {
            normalized = mapped;
        }

        return CanonicalDomains.Contains(normalized) ? normalized : "generic";
    }
}

