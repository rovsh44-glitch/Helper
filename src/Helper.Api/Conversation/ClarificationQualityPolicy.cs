using Helper.Api.Conversation.InteractionState;

namespace Helper.Api.Conversation;

public sealed class ClarificationQualityPolicy : IClarificationQualityPolicy
{
    public ClarificationQualityDecision BuildDecision(
        AmbiguityDecision ambiguity,
        CollaborationIntentAnalysis collaborationIntent,
        IntentClassification intent,
        int attemptNumber,
        string? resolvedLanguage,
        InteractionPolicyProjection? interactionPolicy = null)
    {
        var isRussian = string.Equals(resolvedLanguage, "ru", StringComparison.OrdinalIgnoreCase);
        var boundary = ResolveBoundary(ambiguity.Type);

        if (ambiguity.Type == AmbiguityType.SafetyConfirmation)
        {
            return new ClarificationQualityDecision(
                ClarificationBoundary.Safety,
                ShouldBlockForClarification: true,
                Question: isRussian
                    ? "Подтвердите точную разрешённую область изменений для потенциально разрушительного действия: что можно трогать, а что нельзя?"
                    : "Confirm the exact allowed change boundary for this potentially destructive action: what can I touch and what must stay untouched?",
                Reason: "safety_boundary_confirmation");
        }

        var shouldBlock = ShouldBlockForClarification(boundary, ambiguity, collaborationIntent, intent, attemptNumber, interactionPolicy);
        if (!shouldBlock)
        {
            var reason = collaborationIntent.PrefersAnswerOverClarification
                ? "soft_refinement_only"
                : intent.Analysis.Intent == Helper.Runtime.Core.IntentType.Research
                    ? "research_best_effort_first"
                    : "best_effort_preferred";

            return new ClarificationQualityDecision(
                boundary,
                ShouldBlockForClarification: false,
                Question: BuildRefinementHook(boundary, isRussian, interactionPolicy),
                Reason: reason);
        }

        return new ClarificationQualityDecision(
            boundary,
            ShouldBlockForClarification: true,
            Question: BuildBoundaryQuestion(boundary, intent, attemptNumber, isRussian, interactionPolicy),
            Reason: "boundary_specific_clarification");
    }

    private static ClarificationBoundary ResolveBoundary(AmbiguityType type)
    {
        return type switch
        {
            AmbiguityType.Goal => ClarificationBoundary.Goal,
            AmbiguityType.Format => ClarificationBoundary.Format,
            AmbiguityType.Constraints => ClarificationBoundary.Constraints,
            AmbiguityType.Data => ClarificationBoundary.Data,
            AmbiguityType.Scope => ClarificationBoundary.Scope,
            AmbiguityType.SafetyConfirmation => ClarificationBoundary.Safety,
            _ => ClarificationBoundary.None
        };
    }

    private static string BuildBoundaryQuestion(ClarificationBoundary boundary, IntentClassification intent, int attemptNumber, bool isRussian, InteractionPolicyProjection? interactionPolicy)
    {
        _ = intent;
        _ = attemptNumber;
        var question = boundary switch
        {
            ClarificationBoundary.Goal => isRussian
                ? "Что сейчас важнее: краткий практический старт, пошаговый план или полный разбор вариантов?"
                : "What matters most right now: a short practical start, a step-by-step plan, or a full option breakdown?",
            ClarificationBoundary.Scope => isRussian
                ? "Какой минимальный полезный объём взять первым: один файл, один модуль или весь поток целиком?"
                : "What is the smallest useful scope to take first: one file, one module, or the whole flow?",
            ClarificationBoundary.Format => isRussian
                ? "Какой формат нужен на выходе: абзацы, список, чеклист или JSON?"
                : "Which output format do you want: paragraphs, bullets, a checklist, or JSON?",
            ClarificationBoundary.Constraints => isRussian
                ? "Какое ограничение здесь главное: срок, стек, риск, запрет на изменения или формат результата?"
                : "Which constraint matters most here: deadline, stack, risk, no-change boundary, or result format?",
            ClarificationBoundary.Data => isRussian
                ? "Какой один недостающий вход нужен первым: целевой объект, пример данных или точный контекст выполнения?"
                : "Which single missing input matters first: the target object, example data, or the exact execution context?",
            _ => isRussian
                ? "Уточните один ключевой параметр, чтобы я не промахнулся с ответом."
                : "Clarify one key parameter so I do not aim the answer incorrectly."
        };

        if (interactionPolicy?.SoftenClarification == true)
        {
            return isRussian
                ? $"Чтобы не тормозить разговор, уточню только один момент: {question.TrimStart()}"
                : $"To keep things moving, I only need one quick clarification: {question.TrimStart()}";
        }

        return question;
    }

    private static string BuildRefinementHook(ClarificationBoundary boundary, bool isRussian, InteractionPolicyProjection? interactionPolicy)
    {
        var question = boundary switch
        {
            ClarificationBoundary.Goal => isRussian
                ? "Если направление нужно сместить, одним сообщением укажите приоритет: кратко, подробно или план; иначе я продолжу с разумными предположениями."
                : "If the direction should shift, reply with the priority in one message: a short start, a deeper breakdown, or a plan; otherwise I will continue with reasonable assumptions.",
            ClarificationBoundary.Scope => isRussian
                ? "Если нужен другой объём, одним сообщением укажите желаемый scope."
                : "If you want a different scope, reply with the desired scope in one message.",
            ClarificationBoundary.Format => isRussian
                ? "Если нужен другой формат, одним сообщением назовите его."
                : "If you want a different format, name it in one message.",
            ClarificationBoundary.Constraints => isRussian
                ? "Если есть жёсткие ограничения, одним сообщением перечислите их."
                : "If there are hard constraints, list them in one message.",
            ClarificationBoundary.Data => isRussian
                ? "Если не хватает конкретного входа, пришлите один недостающий фрагмент."
                : "If a concrete input is missing, send the one missing fragment.",
            _ => isRussian
                ? "Если нужно скорректировать ответ, уточните один ключевой параметр."
                : "If the answer needs steering, clarify one key parameter."
        };

        if (interactionPolicy?.SoftenClarification == true)
        {
            return isRussian
                ? $"{question} Я не буду требовать больше одного уточнения, если этого не понадобится."
                : $"{question} I will keep it to one clarification unless more is truly necessary.";
        }

        return question;
    }

    private static bool ShouldBlockForClarification(
        ClarificationBoundary boundary,
        AmbiguityDecision ambiguity,
        CollaborationIntentAnalysis collaborationIntent,
        IntentClassification intent,
        int attemptNumber,
        InteractionPolicyProjection? interactionPolicy)
    {
        _ = ambiguity;

        if (boundary == ClarificationBoundary.Safety)
        {
            return true;
        }

        if (collaborationIntent.PrefersAnswerOverClarification)
        {
            return false;
        }

        if (interactionPolicy?.PreferAnswerFirst == true && boundary != ClarificationBoundary.Data)
        {
            return false;
        }

        if (intent.Analysis.Intent == Helper.Runtime.Core.IntentType.Research)
        {
            return boundary is ClarificationBoundary.Scope && collaborationIntent.HasHardConstraintLanguage;
        }

        if (boundary is ClarificationBoundary.Constraints or ClarificationBoundary.Scope)
        {
            return collaborationIntent.HasHardConstraintLanguage;
        }

        if (boundary == ClarificationBoundary.Data)
        {
            return intent.Analysis.Intent == Helper.Runtime.Core.IntentType.Generate ||
                   collaborationIntent.SeeksDelegatedExecution;
        }

        if (attemptNumber > 1 && boundary != ClarificationBoundary.Format)
        {
            return false;
        }

        return true;
    }
}
