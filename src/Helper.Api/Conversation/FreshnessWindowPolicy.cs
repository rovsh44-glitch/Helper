namespace Helper.Api.Conversation;

public enum WebEvidenceFreshnessState
{
    Fresh,
    Recent,
    Aging,
    Stale
}

public sealed record WebEvidenceFreshnessWindow(
    string Category,
    TimeSpan FreshMaxAge,
    TimeSpan RecentMaxAge,
    TimeSpan AgingMaxAge);

public sealed record WebEvidenceFreshnessAssessment(
    string Category,
    TimeSpan Age,
    WebEvidenceFreshnessState State,
    WebEvidenceFreshnessWindow Window);

public interface IFreshnessWindowPolicy
{
    WebEvidenceFreshnessAssessment Assess(string query, DateTimeOffset storedAtUtc, string? categoryHint = null);
}

public sealed class FreshnessWindowPolicy : IFreshnessWindowPolicy
{
    private const string VolatileCategory = "volatile";
    private const string SoftwareCategory = "software";
    private const string GeneralCategory = "general";

    public WebEvidenceFreshnessAssessment Assess(string query, DateTimeOffset storedAtUtc, string? categoryHint = null)
    {
        var category = ResolveCategory(query, categoryHint);
        var window = ResolveWindow(category);
        var age = DateTimeOffset.UtcNow - storedAtUtc;
        var state = age <= window.FreshMaxAge
            ? WebEvidenceFreshnessState.Fresh
            : age <= window.RecentMaxAge
                ? WebEvidenceFreshnessState.Recent
                : age <= window.AgingMaxAge
                    ? WebEvidenceFreshnessState.Aging
                    : WebEvidenceFreshnessState.Stale;

        return new WebEvidenceFreshnessAssessment(category, age, state, window);
    }

    internal string ResolveCategory(string query, string? categoryHint = null)
    {
        if (!string.IsNullOrWhiteSpace(categoryHint))
        {
            var normalizedHint = categoryHint.Trim().ToLowerInvariant();
            if (normalizedHint is "finance" or "news" or "weather" or "sports" or "elections" or "schedule")
            {
                return VolatileCategory;
            }

            if (normalizedHint is "release" or "version" or "software_release" or "software")
            {
                return SoftwareCategory;
            }
        }

        var text = (query ?? string.Empty).Trim();
        if (ContainsAny(text,
            ".net", "sdk", "release", "version", "changelog", "nuget", "api", "framework",
            "версия", "релиз", "sdk", "пакет", "фреймворк"))
        {
            return SoftwareCategory;
        }

        if (ContainsAny(text,
            "price", "stock", "btc", "bitcoin", "eth", "market cap", "earnings",
            "news", "headline", "breaking", "today", "current", "latest", "right now",
            "weather", "forecast", "rain", "temperature",
            "score", "schedule", "standings", "match", "game", "election",
            "цена", "курс", "сегодня", "текущ", "последн", "новост", "погод", "счёт", "матч", "выбор"))
        {
            return VolatileCategory;
        }

        return GeneralCategory;
    }

    private WebEvidenceFreshnessWindow ResolveWindow(string category)
    {
        return category switch
        {
            VolatileCategory => BuildWindow(
                category,
                ReadMinutes("HELPER_WEB_EVIDENCE_VOLATILE_STALE_MINUTES", 30),
                0.10,
                0.35),
            SoftwareCategory => BuildWindow(
                category,
                ReadMinutes("HELPER_WEB_EVIDENCE_SOFTWARE_STALE_MINUTES", 360),
                0.15,
                0.45),
            _ => BuildWindow(
                category,
                ReadGeneralMaxAge(),
                0.20,
                0.50)
        };
    }

    private static WebEvidenceFreshnessWindow BuildWindow(string category, int staleMinutes, double freshRatio, double recentRatio)
    {
        var agingMinutes = Math.Max(1, staleMinutes);
        var recentMinutes = Math.Max(1, (int)Math.Round(agingMinutes * recentRatio));
        var freshMinutes = Math.Max(1, (int)Math.Round(agingMinutes * freshRatio));

        if (recentMinutes < freshMinutes)
        {
            recentMinutes = freshMinutes;
        }

        return new WebEvidenceFreshnessWindow(
            category,
            TimeSpan.FromMinutes(freshMinutes),
            TimeSpan.FromMinutes(recentMinutes),
            TimeSpan.FromMinutes(agingMinutes));
    }

    private static int ReadGeneralMaxAge()
    {
        var fromMinutes = ReadMinutes("HELPER_WEB_EVIDENCE_GENERAL_STALE_MINUTES", 0);
        if (fromMinutes > 0)
        {
            return fromMinutes;
        }

        var legacyMinutes = ReadMinutes("HELPER_RESEARCH_CACHE_TTL_MINUTES", 20);
        if (legacyMinutes > 0)
        {
            return legacyMinutes;
        }

        var legacySeconds = Environment.GetEnvironmentVariable("HELPER_RESEARCH_CACHE_TTL_SECONDS");
        return int.TryParse(legacySeconds, out var seconds) && seconds > 0
            ? Math.Max(1, (int)Math.Ceiling(seconds / 60d))
            : 20;
    }

    private static int ReadMinutes(string envName, int fallback)
    {
        var raw = Environment.GetEnvironmentVariable(envName);
        return int.TryParse(raw, out var minutes) && minutes > 0 ? minutes : fallback;
    }

    private static bool ContainsAny(string text, params string[] tokens)
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

