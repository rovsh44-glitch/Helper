namespace Helper.Api.Conversation;

internal static class ResearchResponseQualityGuard
{
    public static bool TryRewrite(
        ChatTurnContext context,
        string output,
        bool isRussian,
        out string guardedOutput,
        out string? guardedNextStep)
    {
        ArgumentNullException.ThrowIfNull(context);
        guardedOutput = output;
        guardedNextStep = context.NextStep;

        if (context.Intent.Intent != Helper.Runtime.Core.IntentType.Research)
        {
            return false;
        }

        if (!LooksLikeMetaOnlyResearchOutput(output))
        {
            return false;
        }

        guardedOutput = isRussian
            ? "Сейчас я не могу ответственно утверждать вывод по этому запросу: вместо содержательного результата получился служебный meta-ответ без реального исследовательского выхода."
            : "I cannot responsibly provide a conclusion for this request right now: the result collapsed into a meta-response instead of a substantive research answer.";
        guardedNextStep = isRussian
            ? "Если хотите, я могу повторить поиск с нуля и сузить вопрос до одного проверяемого тезиса."
            : "If you want, I can restart the search from scratch and narrow this to one verifiable claim.";
        return true;
    }

    private static bool LooksLikeMetaOnlyResearchOutput(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return false;
        }

        return output.Contains("If youd like me to clarify something or adjust my response", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("If you'd like me to clarify something or adjust my response", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("specify what you'd like me to focus on next", StringComparison.OrdinalIgnoreCase);
    }
}
