using Helper.Runtime.Core;

namespace Helper.Api.Conversation;

public enum LiveWebRequirementLevel
{
    NoWebNeeded,
    WebHelpful,
    WebRequired
}

public sealed record LiveWebRequirementDecision(
    LiveWebRequirementLevel Requirement,
    string ReasonCode,
    IReadOnlyList<string> Signals);

public interface ILiveWebRequirementPolicy
{
    LiveWebRequirementDecision Evaluate(string message, IntentAnalysis intent);
}

public sealed class LiveWebRequirementPolicy : ILiveWebRequirementPolicy
{
    private static readonly string[] CurrentnessTokens =
    {
        "latest", "most recent", "current", "currently", "today", "tonight", "this week", "this month",
        "последн", "актуаль", "сейчас", "сегодня", "на этой неделе", "в этом месяце"
    };

    private static readonly string[] NewsTokens =
    {
        "news", "headline", "breaking", "update", "updates", "новости", "новость", "сводка", "обновление"
    };

    private static readonly string[] FinanceTokens =
    {
        "price", "prices", "stock", "stocks", "quote", "market cap", "btc", "bitcoin", "ethereum", "crypto",
        "цена", "цены", "акции", "котировка", "курс", "крипт"
    };

    private static readonly string[] SportsTokens =
    {
        "score", "scores", "standings", "fixtures", "schedule", "match", "game", "games", "season", "playoffs",
        "счёт", "результат", "турнирная таблица", "расписание", "матч", "игра", "сезон", "плей-офф"
    };

    private static readonly string[] WeatherTokens =
    {
        "weather", "forecast", "temperature", "rain", "snow", "погода", "прогноз", "температура", "дождь", "снег"
    };

    private static readonly string[] RegulationTokens =
    {
        "law", "laws", "regulation", "regulations", "rule", "rules", "policy", "tax", "compliance", "gdpr",
        "закон", "законы", "регуляц", "правило", "правила", "налог", "комплаенс", "политика"
    };

    private static readonly string[] LocalTokens =
    {
        "near me", "nearby", "around me", "open now", "best in", "restaurant", "restaurants", "hotel", "hotels",
        "рядом", "поблизости", "недалеко", "открыто сейчас", "ресторан", "рестораны", "отель", "отели"
    };

    private static readonly string[] LeadershipTokens =
    {
        "ceo", "president", "prime minister", "mayor", "governor", "head coach", "owner", "minister", "chairman",
        "гендир", "директор", "президент", "премьер", "мэр", "губернатор", "тренер", "министр"
    };

    private static readonly string[] ComparisonTokens =
    {
        "compare", "comparison", "vs", "versus", "best", "top", "сравни", "сравнение", "лучший", "топ"
    };

    private static readonly string[] ProductTokens =
    {
        "phone", "laptop", "gpu", "hosting", "camera", "tool", "tools", "service", "services", "library", "libraries",
        "телефон", "ноутбук", "видеокарта", "хостинг", "камера", "инструмент", "сервис", "библиотека"
    };

    public LiveWebRequirementDecision Evaluate(string message, IntentAnalysis intent)
    {
        var text = (message ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return new LiveWebRequirementDecision(LiveWebRequirementLevel.NoWebNeeded, "empty", Array.Empty<string>());
        }

        var signals = new List<string>();
        var hasCurrentness = AppendIfMatched(text, CurrentnessTokens, "currentness", signals);
        var hasNews = AppendIfMatched(text, NewsTokens, "news", signals);
        var hasFinance = AppendIfMatched(text, FinanceTokens, "finance", signals);
        var hasSports = AppendIfMatched(text, SportsTokens, "sports", signals);
        var hasWeather = AppendIfMatched(text, WeatherTokens, "weather", signals);
        var hasRegulation = AppendIfMatched(text, RegulationTokens, "regulation", signals);
        var hasLocal = AppendIfMatched(text, LocalTokens, "local", signals);
        var hasLeadership = AppendIfMatched(text, LeadershipTokens, "leadership", signals);
        var hasComparison = AppendIfMatched(text, ComparisonTokens, "comparison", signals);
        var hasProduct = AppendIfMatched(text, ProductTokens, "product", signals);

        if (hasNews || hasFinance || hasSports || hasWeather || hasRegulation || hasLocal)
        {
            return Required(ResolvePrimaryReason(signals), signals);
        }

        if (hasLeadership && (hasCurrentness || LooksLikeFactQuestion(text)))
        {
            signals.Add("mutable_entity_fact");
            return Required("mutable_entity_fact", signals);
        }

        if (hasCurrentness)
        {
            return Required("currentness", signals);
        }

        if (hasComparison && hasProduct)
        {
            return Helpful("product_comparison", signals);
        }

        if (intent.Intent == IntentType.Research && hasComparison)
        {
            return Helpful("comparative_research", signals);
        }

        return new LiveWebRequirementDecision(LiveWebRequirementLevel.NoWebNeeded, "none", Array.Empty<string>());
    }

    private static LiveWebRequirementDecision Required(string reasonCode, IReadOnlyList<string> signals)
    {
        return new LiveWebRequirementDecision(LiveWebRequirementLevel.WebRequired, reasonCode, Deduplicate(signals));
    }

    private static LiveWebRequirementDecision Helpful(string reasonCode, IReadOnlyList<string> signals)
    {
        return new LiveWebRequirementDecision(LiveWebRequirementLevel.WebHelpful, reasonCode, Deduplicate(signals));
    }

    private static IReadOnlyList<string> Deduplicate(IReadOnlyList<string> signals)
    {
        return signals
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string ResolvePrimaryReason(IReadOnlyList<string> signals)
    {
        if (signals.Count == 0)
        {
            return "currentness";
        }

        string[] priority =
        {
            "news",
            "finance",
            "sports",
            "weather",
            "regulation",
            "local",
            "mutable_entity_fact",
            "leadership",
            "currentness"
        };

        foreach (var candidate in priority)
        {
            if (signals.Contains(candidate, StringComparer.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return signals[0];
    }

    private static bool AppendIfMatched(string text, IEnumerable<string> lexemes, string signal, ICollection<string> signals)
    {
        foreach (var lexeme in lexemes)
        {
            if (text.Contains(lexeme, StringComparison.OrdinalIgnoreCase))
            {
                signals.Add(signal);
                return true;
            }
        }

        return false;
    }

    private static bool LooksLikeFactQuestion(string text)
    {
        return text.Contains("who is", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("кто", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("what is", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("кто сейчас", StringComparison.OrdinalIgnoreCase);
    }
}

