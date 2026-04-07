namespace Helper.Api.Conversation;

public interface IDecisionExplanationProjector
{
    string BuildSummary(ChatTurnContext context, bool isRussian);
}

public sealed class DecisionExplanationProjector : IDecisionExplanationProjector
{
    public string BuildSummary(ChatTurnContext context, bool isRussian)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.RequiresClarification)
        {
            return isRussian
                ? "Остановился на одном уточнении, потому что без него есть риск ошибиться по смыслу или границе безопасности."
                : "Stopped for one clarification because the current request risks a wrong interpretation or missing safety boundary.";
        }

        if (context.ForceBestEffort)
        {
            return isRussian
                ? "Продолжил с ограниченными допущениями, чтобы не стопорить задачу лишним циклом уточнений."
                : "Continued with bounded assumptions to avoid blocking the task on an extra clarification loop.";
        }

        if (string.Equals(context.ResolvedLiveWebRequirement, "web_required", StringComparison.OrdinalIgnoreCase))
        {
            return isRussian
                ? "Перешел в режим с веб-проверкой, потому что вопрос требует актуальных или проверяемых внешних данных."
                : "Switched to live web grounding because the request needs current or externally verifiable evidence.";
        }

        if (context.UsedMemoryLayers.Contains("shared_understanding", StringComparer.OrdinalIgnoreCase) ||
            context.UsedMemoryLayers.Contains("project_memory", StringComparer.OrdinalIgnoreCase))
        {
            return isRussian
                ? "Опирался на накопленный контекст общения и текущий проектный контур, чтобы не начинать заново."
                : "Leaned on accumulated conversation and project context so the turn did not restart from zero.";
        }

        return isRussian
            ? "Выбрал прямой ответ без лишней ceremony, потому что запрос уже был достаточно определенным."
            : "Chose a direct answer without extra ceremony because the request was already specific enough.";
    }
}
