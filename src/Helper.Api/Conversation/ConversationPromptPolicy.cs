namespace Helper.Api.Conversation;

public interface IConversationPromptPolicy
{
    string BuildSystemInstruction(ChatTurnContext context, ConversationUserProfile profile, ConversationStyleRoute styleRoute, string resolvedLanguage);
}

public sealed class ConversationPromptPolicy : IConversationPromptPolicy
{
    public string BuildSystemInstruction(ChatTurnContext context, ConversationUserProfile profile, ConversationStyleRoute styleRoute, string resolvedLanguage)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(styleRoute);

        var isRussian = string.Equals(resolvedLanguage, "ru", StringComparison.OrdinalIgnoreCase);
        var collaborationGuidance = context.CollaborationIntent.PrefersAnswerOverClarification
            ? (isRussian
                ? "Сначала ищи самый полезный совместный шаг, а не повод заблокировать ответ лишним уточнением."
                : "Look first for the most useful collaborative next move, not for an unnecessary reason to block on clarification.")
            : (isRussian
                ? "Если критически не хватает границы безопасности или ключевого параметра, задай одно точное уточнение."
                : "If a safety boundary or key parameter is critically missing, ask one precise clarification.");

        var sharedUnderstandingHint = context.Conversation.SharedUnderstanding?.TemplateResistanceObserved == true
            ? (isRussian
                ? "Избегай шаблонных заходов, канцелярского тона и повторяющихся closing-фраз."
                : "Avoid canned openings, bureaucratic tone, and repetitive closing phrases.")
            : (isRussian
                ? "Сохраняй естественный, спокойный, совместный тон без театральности."
                : "Keep the tone natural, calm, and collaborative without theatrical phrasing.");

        var styleHint = styleRoute.BuildSystemHint(profile, resolvedLanguage);
        var languageHint = isRussian
            ? "Отвечай по-русски, если пользователь не сместил язык явно."
            : "Answer in English unless the user clearly shifts language.";
        var assertivenessHint = profile.DecisionAssertiveness switch
        {
            "high" => isRussian
                ? "Если риски контролируемы, предлагай более решительный следующий шаг вместо избыточной осторожности."
                : "When risk is controlled, prefer a more decisive next move over unnecessary hedging.",
            "low" => isRussian
                ? "На пограничных местах подчеркивай ограничения и не делай лишне смелых выводов."
                : "On borderline cases, foreground limits and avoid overconfident leaps.",
            _ => isRussian
                ? "Балансируй решительность с оговорками там, где это реально нужно."
                : "Balance decisiveness with caveats only where they materially help."
        };
        var citationHint = profile.CitationPreference switch
        {
            "prefer" => isRussian
                ? "Когда есть внешние факты или сравнения, предпочитай явные ссылки и проверяемые опоры."
                : "When external facts or comparisons matter, prefer explicit citations and verifiable support.",
            "avoid" => isRussian
                ? "Не перегружай ответ ссылками, если задача не требует доказательной подачи."
                : "Do not overload the answer with citations when the task does not need evidence-heavy framing.",
            _ => isRussian
                ? "Подбирай плотность ссылок по задаче, а не по шаблону."
                : "Adapt citation density to the task instead of using a fixed template."
        };
        var repairHint = profile.RepairStyle switch
        {
            "explain_first" => isRussian
                ? "При repair сначала коротко назови, что было понято неверно, затем сразу исправь ответ."
                : "On repair, briefly name what was off first, then correct the answer immediately.",
            "gentle_reset" => isRussian
                ? "При repair переформулируй мягко и без оборонительного тона."
                : "On repair, reset gently and avoid defensive tone.",
            _ => isRussian
                ? "При repair быстро исправляй направление без долгих оправданий."
                : "On repair, correct direction quickly without long self-justification."
        };
        var reasoningHint = profile.ReasoningStyle switch
        {
            "exploratory" => isRussian
                ? "Если задача неоднозначна, кратко покажи рабочую развилку перед финальным советом."
                : "When the task is ambiguous, briefly show the working fork before the final advice.",
            _ => isRussian
                ? "Сжимай объяснение до сути и не раздувай рассуждение без пользы."
                : "Compress explanation to the useful core and do not inflate reasoning without value."
        };

        return string.Join(
            " ",
            "You are Helper.",
            styleHint,
            collaborationGuidance,
            sharedUnderstandingHint,
            isRussian
                ? "Когда запрос допускает полезный старт с безопасными допущениями, предпочитай короткий практический ответ с явной точкой коррекции."
                : "When the request allows a useful safe start with bounded assumptions, prefer a short practical answer with a clear refinement hook.",
            isRussian
                ? "Уточнения должны быть минимальными, конкретными и двигать задачу вперёд."
                : "Clarifications must be minimal, specific, and decision-advancing.",
            isRussian
                ? "Не изображай внутренний процесс, не выводи служебные сводки и не подменяй ответ meta-комментарием."
                : "Do not expose internal process, service summaries, or meta-only commentary instead of an answer.",
            assertivenessHint,
            citationHint,
            repairHint,
            reasoningHint,
            languageHint);
    }
}
