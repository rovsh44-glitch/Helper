using System.Text.RegularExpressions;

namespace Helper.Api.Conversation;

public enum AmbiguityType
{
    None,
    Goal,
    Format,
    Constraints,
    Data,
    Scope,
    SafetyConfirmation
}

public sealed record AmbiguityDecision(
    bool IsAmbiguous,
    AmbiguityType Type,
    double Confidence,
    string Reason);

public interface IAmbiguityDetector
{
    AmbiguityDecision Analyze(string message);
}

public sealed class HybridAmbiguityDetector : IAmbiguityDetector
{
    private static readonly string[] GoalAmbiguityTokens =
    {
        "help", "something", "anything", "what should i do", "what should i do next",
        "помоги", "что делать", "что делать дальше", "сделай что-нибудь"
    };

    private static readonly string[] MissingContextTokens =
    {
        // EN underspecification / directional prompts
        "context is missing", "limited context", "requirements are unclear",
        "not sure where to start", "where to start", "next step", "first action",
        "quick advice", "direction for this task", "do the right thing",
        "please clarify what to do first", "guide me",

        // RU underspecification / directional prompts
        "контекст неясен", "контекст не ясен", "данных мало",
        "входные данные неполные", "входные данные не полные",
        "не понимаю с чего начать", "с чего начать", "следующий шаг",
        "первый шаг", "первым шагом", "направление по задаче",
        "подскажи следующий шаг", "сделай как лучше"
    };

    private static readonly string[] FormatTokens =
    {
        "format", "формат", "выведи", "json", "table", "таблица", "bullet", "список"
    };

    private static readonly string[] ConstraintTokens =
    {
        "constraint", "огранич", "deadline", "срок", "budget", "бюджет", "без ", "must", "нельзя"
    };

    private static readonly string[] SafetyTokens =
    {
        "delete", "drop", "remove", "format disk", "rm ", "taskkill", "shutdown",
        "удали", "сотри", "форматни", "очисти", "сломай"
    };

    private static readonly string[] ConfirmationTokens =
    {
        "confirm", "confirmed", "подтверждаю", "подтвердить"
    };

    public AmbiguityDecision Analyze(string message)
    {
        var text = (message ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return new AmbiguityDecision(true, AmbiguityType.Goal, 1.0, "Empty request.");
        }

        if (ContainsAny(text, SafetyTokens) && !ContainsAny(text, ConfirmationTokens))
        {
            return new AmbiguityDecision(
                true,
                AmbiguityType.SafetyConfirmation,
                0.96,
                "Potentially destructive intent without explicit confirmation.");
        }

        if (ContainsAny(text, MissingContextTokens))
        {
            return new AmbiguityDecision(
                true,
                AmbiguityType.Data,
                0.88,
                "Prompt explicitly indicates missing context or asks for directional guidance.");
        }

        if (text.Length < 10 || ContainsAny(text, GoalAmbiguityTokens))
        {
            return new AmbiguityDecision(
                true,
                AmbiguityType.Goal,
                0.86,
                "Goal is too broad or underspecified.");
        }

        if (ContainsAny(text, FormatTokens) && !HasConcreteFormat(text))
        {
            return new AmbiguityDecision(
                true,
                AmbiguityType.Format,
                0.72,
                "Output format requested but not concretely specified.");
        }

        if (ContainsAny(text, ConstraintTokens) && !HasConcreteConstraints(text) && !LooksLikeResearchEvidencePrompt(text))
        {
            return new AmbiguityDecision(
                true,
                AmbiguityType.Constraints,
                0.68,
                "Constraints are referenced but not explicitly provided.");
        }

        if (LooksLikeMissingData(text))
        {
            return new AmbiguityDecision(
                true,
                AmbiguityType.Data,
                0.7,
                "Request references action without required input data.");
        }

        if (LooksLikeScopeConflict(text))
        {
            return new AmbiguityDecision(
                true,
                AmbiguityType.Scope,
                0.64,
                "Scope appears broad and may require narrowing.");
        }

        return new AmbiguityDecision(false, AmbiguityType.None, 0.0, string.Empty);
    }

    private static bool ContainsAny(string text, IEnumerable<string> tokens)
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

    private static bool HasConcreteFormat(string text)
    {
        return text.Contains("json", StringComparison.OrdinalIgnoreCase)
            || text.Contains("markdown", StringComparison.OrdinalIgnoreCase)
            || text.Contains("table", StringComparison.OrdinalIgnoreCase)
            || text.Contains("таблица", StringComparison.OrdinalIgnoreCase)
            || text.Contains("checklist", StringComparison.OrdinalIgnoreCase)
            || Regex.IsMatch(text, @"\b(bullets?|список)\b", RegexOptions.IgnoreCase);
    }

    private static bool HasConcreteConstraints(string text)
    {
        return Regex.IsMatch(text, @"\b(\d+)\s*(min|mins|minutes|час|часа|часов|дн|day|days|%|gb|mb|usd|\$)\b", RegexOptions.IgnoreCase)
            || text.Contains("c#", StringComparison.OrdinalIgnoreCase)
            || text.Contains(".net", StringComparison.OrdinalIgnoreCase)
            || text.Contains("react", StringComparison.OrdinalIgnoreCase)
            || text.Contains("typescript", StringComparison.OrdinalIgnoreCase)
            || text.Contains("python", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeMissingData(string text)
    {
        var actionOnly = Regex.IsMatch(text, @"\b(сделай|do|build|generate|создай|реализуй)\b", RegexOptions.IgnoreCase);
        var hasObject = Regex.IsMatch(text, @"\b(api|service|module|скрипт|план|отчет|report|endpoint)\b", RegexOptions.IgnoreCase);
        return actionOnly && !hasObject && text.Length < 28;
    }

    private static bool LooksLikeScopeConflict(string text)
    {
        var broadTokens = new[] { "всё", "all", "entire", "полностью", "completely", "full rewrite" };
        var immediateTokens = new[] { "срочно", "now", "asap", "быстро", "quickly" };
        return ContainsAny(text, broadTokens) && ContainsAny(text, immediateTokens);
    }

    private static bool LooksLikeResearchEvidencePrompt(string text)
    {
        var researchTokens = new[]
        {
            "source", "sources", "research", "evidence", "study", "studies",
            "источник", "источники", "научн", "исследован", "доказатель", "популярн"
        };
        return ContainsAny(text, researchTokens);
    }
}

public interface IClarificationPolicy
{
    int MaxClarificationTurns { get; }
    bool ShouldForceBestEffort(AmbiguityDecision decision, int priorClarificationTurns);
    string BuildQuestion(AmbiguityDecision decision, IntentClassification intent, int attemptNumber, string? resolvedLanguage);
    string BuildLowConfidenceQuestion(IntentClassification intent, int attemptNumber, string? resolvedLanguage);
}

public sealed class ClarificationPolicy : IClarificationPolicy
{
    public int MaxClarificationTurns { get; }

    public ClarificationPolicy(int? maxClarificationTurns = null)
    {
        MaxClarificationTurns = Math.Clamp(
            maxClarificationTurns ?? ReadMaxClarificationTurns(),
            1,
            6);
    }

    public bool ShouldForceBestEffort(AmbiguityDecision decision, int priorClarificationTurns)
    {
        if (decision.Type == AmbiguityType.SafetyConfirmation)
        {
            return false;
        }

        return priorClarificationTurns >= MaxClarificationTurns;
    }

    public string BuildQuestion(AmbiguityDecision decision, IntentClassification intent, int attemptNumber, string? resolvedLanguage)
    {
        var depth = ResolveDepth(attemptNumber);
        var isRussian = string.Equals(resolvedLanguage, "ru", StringComparison.OrdinalIgnoreCase);
        var question = decision.Type == AmbiguityType.SafetyConfirmation
            ? BuildSafetyQuestion(depth, isRussian)
            : BuildSoftGuidanceQuestion(decision.Type, intent, depth, isRussian);

        return decision.Type == AmbiguityType.SafetyConfirmation
            ? question
            : EnsureClarificationSignal(question, resolvedLanguage);
    }

    public string BuildLowConfidenceQuestion(IntentClassification intent, int attemptNumber, string? resolvedLanguage)
    {
        var depth = ResolveDepth(attemptNumber);
        var isRussian = string.Equals(resolvedLanguage, "ru", StringComparison.OrdinalIgnoreCase);
        if (depth == ClarificationDepth.Short)
        {
            return isRussian
                ? $"Чтобы не промахнуться с режимом, подскажите, что сейчас полезнее: {BuildModeOptions(isRussian)}? {BuildBestEffortOffer(isRussian)}"
                : $"So I do not pick the wrong mode, tell me what would help most right now: {BuildModeOptions(isRussian)}? {BuildBestEffortOffer(isRussian)}";
        }

        if (depth == ClarificationDepth.Medium)
        {
            return isRussian
                ? $"Сейчас вижу несколько правдоподобных трактовок. Выберите направление: {BuildModeOptions(isRussian, numbered: true)}. {BuildBestEffortOffer(isRussian)}"
                : $"I still see a few plausible interpretations. Pick a direction: {BuildModeOptions(isRussian, numbered: true)}. {BuildBestEffortOffer(isRussian)}";
        }

        return isRussian
            ? "Чтобы не тратить ещё один ход на развилку, напишите одним сообщением желаемый режим и результат. Либо я продолжу с разумными допущениями и явно их отмечу."
            : "To avoid spending another turn on interpretation, reply with the desired mode and deliverable in one message. Otherwise I will continue with best-effort assumptions and label them clearly.";
    }

    private static string BuildSafetyQuestion(ClarificationDepth depth, bool isRussian)
    {
        if (depth == ClarificationDepth.Short)
        {
            return isRussian
                ? "Похоже, здесь возможны разрушительные изменения. Если хотите продолжать, подтвердите цель и точную разрешённую область: что можно трогать, а что нельзя?"
                : "This could cause destructive changes. If you want me to continue, confirm the target and exact allowed scope: what can I touch and what is off-limits?";
        }

        return isRussian
            ? "Прежде чем выполнять потенциально разрушительное действие, мне нужно явное подтверждение. Напишите цель, разрешённые пути или объекты и ожидаемый результат."
            : "Before I execute a potentially destructive action, I need explicit confirmation. Reply with the target, allowed paths or objects, and the expected outcome.";
    }

    private static string BuildSoftGuidanceQuestion(AmbiguityType type, IntentClassification intent, ClarificationDepth depth, bool isRussian)
    {
        return (type, depth, isRussian) switch
        {
            (AmbiguityType.Goal, ClarificationDepth.Short, true) => $"Чтобы попасть точнее, подскажите направление: {BuildModeOptions(isRussian)}? {BuildBestEffortOffer(isRussian)}",
            (AmbiguityType.Goal, ClarificationDepth.Medium, true) => $"Могу пойти тремя путями: {BuildModeOptions(isRussian, numbered: true)}. Что выбираем? {BuildBestEffortOffer(isRussian)}",
            (AmbiguityType.Goal, ClarificationDepth.Deep, true) => "Чтобы не тратить ещё один ход на догадки, скажите одним сообщением: что делаем, в каком формате отвечать и можно ли идти с разумными допущениями, если деталей всё ещё не хватает.",
            (AmbiguityType.Goal, ClarificationDepth.Short, false) => $"To aim more precisely, tell me the direction: {BuildModeOptions(isRussian)}? {BuildBestEffortOffer(isRussian)}",
            (AmbiguityType.Goal, ClarificationDepth.Medium, false) => $"I can take this three ways: {BuildModeOptions(isRussian, numbered: true)}. Which one do you want? {BuildBestEffortOffer(isRussian)}",
            (AmbiguityType.Goal, ClarificationDepth.Deep, false) => "To avoid spending another turn on guesswork, reply in one message with the goal, preferred output format, and whether I may continue with best-effort assumptions if details stay incomplete.",

            (AmbiguityType.Format, ClarificationDepth.Short, true) => $"Как вам удобнее получить ответ: {BuildFormatOptions(isRussian)}? {BuildBestEffortOffer(isRussian)}",
            (AmbiguityType.Format, ClarificationDepth.Medium, true) => $"Могу оформить это {BuildFormatOptions(isRussian, extended: true)}. Что будет полезнее? {BuildBestEffortOffer(isRussian)}",
            (AmbiguityType.Format, ClarificationDepth.Deep, true) => "Последнее уточнение по форме ответа: какой вариант будет полезнее, или разрешаете мне выбрать формат самому и явно отметить предположения?",
            (AmbiguityType.Format, ClarificationDepth.Short, false) => $"How would you like the answer: {BuildFormatOptions(isRussian)}? {BuildBestEffortOffer(isRussian)}",
            (AmbiguityType.Format, ClarificationDepth.Medium, false) => $"I can format this as {BuildFormatOptions(isRussian, extended: true)}. Which would be most useful? {BuildBestEffortOffer(isRussian)}",
            (AmbiguityType.Format, ClarificationDepth.Deep, false) => "Final format check: which output shape would help most, or should I pick a best-effort format and label assumptions explicitly?",

            (AmbiguityType.Constraints, ClarificationDepth.Short, true) => $"Есть ли жёсткие рамки по стеку, срокам, бюджету или запретам? Если нет, {BuildBestEffortOffer(isRussian, lowerCase: true)}",
            (AmbiguityType.Constraints, ClarificationDepth.Medium, true) => $"Чтобы не промахнуться, назовите жёсткие ограничения: стек, срок, бюджет или запретные варианты. Иначе {BuildBestEffortOffer(isRussian, lowerCase: true)}",
            (AmbiguityType.Constraints, ClarificationDepth.Deep, true) => "Последняя проверка ограничений: перечислите жёсткие рамки одним сообщением, либо я продолжу с разумными допущениями и отдельно их отмечу.",
            (AmbiguityType.Constraints, ClarificationDepth.Short, false) => $"Do you have hard limits on stack, timeline, budget, or forbidden options? If not, {BuildBestEffortOffer(isRussian, lowerCase: true)}",
            (AmbiguityType.Constraints, ClarificationDepth.Medium, false) => $"To avoid missing the mark, list the hard constraints: stack, deadline, budget, or forbidden options. Otherwise {BuildBestEffortOffer(isRussian, lowerCase: true)}",
            (AmbiguityType.Constraints, ClarificationDepth.Deep, false) => "Last constraints check: list the hard limits in one message, or I will proceed with best-effort assumptions and label them separately.",

            (AmbiguityType.Data, ClarificationDepth.Short, true) => $"Мне не хватает входных данных. Можете прислать файл, пример или ссылку? Если удобнее, {BuildBestEffortOffer(isRussian, lowerCase: true)}",
            (AmbiguityType.Data, ClarificationDepth.Medium, true) => $"Сейчас не хватает артефактов, без них легко ошибиться. Пришлите файл, ссылку или пример, либо {BuildBestEffortOffer(isRussian, lowerCase: true)}",
            (AmbiguityType.Data, ClarificationDepth.Deep, true) => "Последний запрос данных: если входных артефактов пока нет, я могу продолжить с разумными допущениями и явно их подписать.",
            (AmbiguityType.Data, ClarificationDepth.Short, false) => $"I am missing input data. Can you send a file, example, or link? If you prefer, {BuildBestEffortOffer(isRussian, lowerCase: true)}",
            (AmbiguityType.Data, ClarificationDepth.Medium, false) => $"I am still missing the artifacts needed to be precise. Send a file, link, or example, or {BuildBestEffortOffer(isRussian, lowerCase: true)}",
            (AmbiguityType.Data, ClarificationDepth.Deep, false) => "Final data check: if the input artifacts are still unavailable, I can continue with best-effort assumptions and label them explicitly.",

            (AmbiguityType.Scope, ClarificationDepth.Short, true) => $"Сейчас задача выглядит широкой. Хотите, я начну {BuildModeOptions(isRussian)}? Если нужно идти сразу, возьму минимальный рабочий scope и помечу предположения.",
            (AmbiguityType.Scope, ClarificationDepth.Medium, true) => $"Давайте сузим первую фазу: выбрать один модуль, один результат или один блок работ? Если удобнее не останавливаться, я возьму минимальный полезный scope и явно отмечу предположения.",
            (AmbiguityType.Scope, ClarificationDepth.Deep, true) => "Последнее уточнение по области: назовите приоритет для первой фазы, либо я сам возьму минимальный полезный scope и отмечу допущения.",
            (AmbiguityType.Scope, ClarificationDepth.Short, false) => $"The scope still looks broad. Should I start {BuildModeOptions(isRussian)}? If you want me to move now, I will take the smallest useful scope and label assumptions.",
            (AmbiguityType.Scope, ClarificationDepth.Medium, false) => "Let us narrow phase one: one module, one deliverable, or one block of work. If you would rather keep moving, I can choose the smallest useful scope and label assumptions.",
            (AmbiguityType.Scope, ClarificationDepth.Deep, false) => "Final scope check: name the priority for phase one, or I will choose the smallest useful scope and label the assumptions I make.",

            (_, _, true) => intent.Analysis.Intent == Helper.Runtime.Core.IntentType.Research
                ? $"Могу сделать краткий обзор, подробный разбор с источниками или план дальнейших шагов. Что полезнее? {BuildBestEffortOffer(isRussian)}"
                : $"Чтобы не промахнуться, подскажите, что полезнее: {BuildModeOptions(isRussian)}? {BuildBestEffortOffer(isRussian)}",
            _ => intent.Analysis.Intent == Helper.Runtime.Core.IntentType.Research
                ? $"I can do a brief overview, a source-backed deep dive, or a plan for next steps. What would help most? {BuildBestEffortOffer(isRussian)}"
                : $"To avoid guessing, tell me what would be most useful: {BuildModeOptions(isRussian)}? {BuildBestEffortOffer(isRussian)}"
        };
    }

    private static string BuildModeOptions(bool isRussian, bool numbered = false)
    {
        if (isRussian)
        {
            return numbered
                ? "1) кратко, 2) подробно, 3) в виде плана"
                : "кратко, подробно или в виде плана";
        }

        return numbered
            ? "1) briefly, 2) in depth, 3) as a plan"
            : "briefly, in depth, or as a plan";
    }

    private static string BuildFormatOptions(bool isRussian, bool extended = false)
    {
        if (isRussian)
        {
            return extended
                ? "кратко, подробно, таблицей, чеклистом или патчем"
                : "кратко, подробно или сразу в виде плана/чеклиста";
        }

        return extended
            ? "briefly, in depth, as a table, checklist, or patch"
            : "briefly, in depth, or directly as a plan/checklist";
    }

    private static string BuildBestEffortOffer(bool isRussian, bool lowerCase = false)
    {
        if (isRussian)
        {
            return lowerCase
                ? "могу начать с разумных допущений и явно пометить предположения."
                : "Если хотите, могу начать с разумных допущений и явно пометить предположения.";
        }

        return lowerCase
            ? "I can start with best-effort assumptions and label them clearly."
            : "If you prefer, I can start with best-effort assumptions and label them clearly.";
    }

    private static ClarificationDepth ResolveDepth(int attemptNumber)
    {
        if (attemptNumber <= 1) return ClarificationDepth.Short;
        if (attemptNumber == 2) return ClarificationDepth.Medium;
        return ClarificationDepth.Deep;
    }

    private static int ReadMaxClarificationTurns()
    {
        var raw = Environment.GetEnvironmentVariable("HELPER_MAX_CLARIFICATION_TURNS");
        return int.TryParse(raw, out var parsed) ? parsed : 2;
    }

    private static string EnsureClarificationSignal(string question, string? resolvedLanguage)
    {
        if (question.Contains("?", StringComparison.Ordinal))
        {
            return question;
        }

        if (string.Equals(resolvedLanguage, "ru", StringComparison.OrdinalIgnoreCase))
        {
            return $"Коротко уточню: {question}";
        }

        return $"Quick check: {question}";
    }

    private enum ClarificationDepth
    {
        Short,
        Medium,
        Deep
    }
}

