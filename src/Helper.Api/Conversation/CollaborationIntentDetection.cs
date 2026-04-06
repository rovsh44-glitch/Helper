namespace Helper.Api.Conversation;

public sealed class CollaborationIntentDetector : ICollaborationIntentDetector
{
    private static readonly string[] GuidanceTokens =
    {
        "guide me",
        "what should i do next",
        "what should i do",
        "where should i start",
        "best next step",
        "do the right thing",
        "use your judgment",
        "what would you do",
        "help",
        "подскажи следующий шаг",
        "что делать дальше",
        "что делать",
        "помоги",
        "с чего начать",
        "сделай как лучше",
        "используй лучшее решение",
        "как бы ты сделал"
    };

    private static readonly string[] DelegationTokens =
    {
        "do it",
        "go ahead",
        "implement it",
        "just start",
        "handle it",
        "сделай это",
        "запускай",
        "начинай",
        "реализуй",
        "возьми на себя"
    };

    private static readonly string[] BestJudgmentTokens =
    {
        "best judgment",
        "best option",
        "reasonable assumptions",
        "smallest useful scope",
        "на свое усмотрение",
        "разумные допущения",
        "как лучше",
        "выбери лучший вариант"
    };

    private static readonly string[] HardConstraintTokens =
    {
        "must",
        "do not",
        "don't",
        "narrowly",
        "exactly",
        "strict",
        "нельзя",
        "строго",
        "обязательно",
        "точно"
    };

    public CollaborationIntentAnalysis Analyze(string? message, string? resolvedLanguage)
    {
        var text = message?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            return CollaborationIntentAnalysis.None;
        }

        var signals = new List<string>();
        var isGuidanceSeeking = ContainsAny(text, GuidanceTokens);
        if (isGuidanceSeeking)
        {
            signals.Add("guidance_seeking");
        }

        var seeksDelegatedExecution = ContainsAny(text, DelegationTokens);
        if (seeksDelegatedExecution)
        {
            signals.Add("delegated_execution");
        }

        var trustsBestJudgment = ContainsAny(text, BestJudgmentTokens) || text.Contains("как лучше", StringComparison.OrdinalIgnoreCase);
        if (trustsBestJudgment)
        {
            signals.Add("best_judgment");
        }

        var hasHardConstraintLanguage = ContainsAny(text, HardConstraintTokens);
        if (hasHardConstraintLanguage)
        {
            signals.Add("hard_constraints");
        }

        var prefersAnswerOverClarification = (isGuidanceSeeking || seeksDelegatedExecution || trustsBestJudgment) && !hasHardConstraintLanguage;
        if (prefersAnswerOverClarification)
        {
            signals.Add("prefer_answer_over_clarification");
        }

        var primaryMode = seeksDelegatedExecution
            ? "delegation"
            : isGuidanceSeeking
                ? "guidance"
                : trustsBestJudgment
                    ? "best_judgment"
                    : "none";

        return new CollaborationIntentAnalysis(
            IsGuidanceSeeking: isGuidanceSeeking,
            TrustsBestJudgment: trustsBestJudgment,
            SeeksDelegatedExecution: seeksDelegatedExecution,
            PrefersAnswerOverClarification: prefersAnswerOverClarification,
            HasHardConstraintLanguage: hasHardConstraintLanguage,
            PrimaryMode: primaryMode,
            Signals: signals);
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
}
