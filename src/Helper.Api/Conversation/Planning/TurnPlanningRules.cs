using Helper.Runtime.Core;

namespace Helper.Api.Conversation;

internal static class TurnPlanningRules
{
    public static string NormalizeLiveWebMode(string? mode)
    {
        if (string.Equals(mode, "force_search", StringComparison.OrdinalIgnoreCase))
        {
            return "force_search";
        }

        if (string.Equals(mode, "no_web", StringComparison.OrdinalIgnoreCase))
        {
            return "no_web";
        }

        return "auto";
    }

    public static int GetPriorClarificationTurns(ConversationState state)
    {
        lock (state.SyncRoot)
        {
            return Math.Max(0, state.ConsecutiveClarificationTurns);
        }
    }

    public static bool IsLikelyFactualPrompt(string prompt)
    {
        string[] factualTokens = ["what", "when", "where", "who", "когда", "где", "кто", "факт", "источник", "сколько"];
        return factualTokens.Any(token => prompt.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsExplicitResearchPrompt(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return false;
        }

        return ResearchIntentPolicy.HasExplicitResearchRequest(prompt) ||
               ResearchIntentPolicy.CountWeakResearchSignals(prompt) >= 2;
    }

    public static bool ShouldPromoteToResearch(IntentType currentIntent, string prompt, LiveWebRequirementDecision decision)
    {
        if (decision.Requirement == LiveWebRequirementLevel.NoWebNeeded)
        {
            return false;
        }

        if (currentIntent == IntentType.Research || currentIntent == IntentType.Unknown)
        {
            return true;
        }

        if (currentIntent != IntentType.Generate)
        {
            return false;
        }

        return !IsProtectedGeneratePrompt(prompt);
    }

    public static void PromoteBenchmarkResearch(ChatTurnContext turn, string sourceSuffix, double minConfidence, string signal)
    {
        if (turn.Intent.Intent != IntentType.Research)
        {
            turn.Intent = turn.Intent with { Intent = IntentType.Research };
        }

        turn.IntentConfidence = Math.Max(turn.IntentConfidence, minConfidence);
        turn.IntentSource = string.IsNullOrWhiteSpace(turn.IntentSource)
            ? sourceSuffix
            : $"{turn.IntentSource}+{sourceSuffix}";
        turn.IntentSignals.Add(signal);
    }

    public static bool IsProtectedGeneratePrompt(string prompt)
    {
        if (GoldenTemplateIntentPolicy.HasExplicitGoldenTemplateRequest(prompt))
        {
            return true;
        }

        if (!ResearchIntentPolicy.HasExplicitGenerateRequest(prompt))
        {
            return false;
        }

        return ContainsAny(
            prompt,
            "code", "app", "project", "template", "service", "api", "endpoint", "library", "class", "function",
            "код", "приложение", "проект", "шаблон", "сервис", "эндпоинт", "библиотека", "класс", "функция");
    }

    public static IntentClassification BuildLegacyIntent(string message)
    {
        var text = (message ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return new IntentClassification(new IntentAnalysis(IntentType.Unknown, string.Empty), 0.0, "legacy", Array.Empty<string>());
        }

        var isResearch = text.Contains("research", StringComparison.OrdinalIgnoreCase) ||
                         text.Contains("исслед", StringComparison.OrdinalIgnoreCase) ||
                         text.Contains("source", StringComparison.OrdinalIgnoreCase) ||
                         text.Contains("источник", StringComparison.OrdinalIgnoreCase) ||
                         text.Contains("compare", StringComparison.OrdinalIgnoreCase) ||
                         text.Contains("сравни", StringComparison.OrdinalIgnoreCase);

        var intent = isResearch ? IntentType.Research : IntentType.Generate;
        return new IntentClassification(
            new IntentAnalysis(intent, string.Empty),
            0.55,
            "legacy",
            ["legacy:intent_v1"]);
    }

    public static string BuildSoftBestEffortReason(AmbiguityType type, string? resolvedLanguage)
    {
        var isRussian = string.Equals(resolvedLanguage, "ru", StringComparison.OrdinalIgnoreCase);
        return (type, isRussian) switch
        {
            (AmbiguityType.Goal, true) => "Запрос пока слишком общий, поэтому беру самый полезный практический старт.",
            (AmbiguityType.Format, true) => "Точный формат не задан, поэтому начну с краткой практической структуры.",
            (AmbiguityType.Constraints, true) => "Жёсткие ограничения не заданы, поэтому двигаюсь от безопасных стандартных допущений.",
            (AmbiguityType.Data, true) => "Не хватает входных данных, поэтому начну с безопасного общего варианта и отмечу допущения.",
            (AmbiguityType.Scope, true) => "Область пока широкая, поэтому начну с минимального полезного объёма.",
            (_, true) => "Деталей пока недостаточно, поэтому начинаю с наиболее вероятной полезной трактовки.",
            (AmbiguityType.Goal, false) => "The request is still broad, so I am starting with the most useful practical interpretation.",
            (AmbiguityType.Format, false) => "The exact format is unspecified, so I will begin with a concise practical structure.",
            (AmbiguityType.Constraints, false) => "Hard constraints are missing, so I am proceeding from safe default assumptions.",
            (AmbiguityType.Data, false) => "Key input data is missing, so I will start with a safe general version and label assumptions.",
            (AmbiguityType.Scope, false) => "The scope is still broad, so I will start with the smallest useful slice.",
            _ => "Details are still incomplete, so I am starting from the most likely useful interpretation."
        };
    }

    private static bool ContainsAny(string prompt, params string[] tokens)
    {
        foreach (var token in tokens)
        {
            if (prompt.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
