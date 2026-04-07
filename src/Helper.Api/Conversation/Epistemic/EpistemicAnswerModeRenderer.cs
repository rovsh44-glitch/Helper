using Helper.Api.Hosting;

namespace Helper.Api.Conversation.Epistemic;

internal static class EpistemicAnswerModeRenderer
{
    public static string Apply(ChatTurnContext context, string content, ComposerLocalization localization)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (string.IsNullOrWhiteSpace(content))
        {
            return content;
        }

        return context.EpistemicAnswerMode switch
        {
            EpistemicAnswerMode.BestEffortHypothesis => EnsureBestEffortFraming(content, localization),
            EpistemicAnswerMode.NeedsVerification => AppendIfMissing(content, localization.NeedsVerificationNotice),
            EpistemicAnswerMode.Abstain => EnsureAbstentionBody(context, content, localization),
            _ => content
        };
    }

    private static string EnsureBestEffortFraming(string content, ComposerLocalization localization)
    {
        if (content.Contains(localization.BestEffortLead, StringComparison.OrdinalIgnoreCase) ||
            content.Contains(localization.BestEffortLabel, StringComparison.OrdinalIgnoreCase))
        {
            return content;
        }

        return $"{localization.BestEffortLead}\n\n{content}".TrimEnd();
    }

    private static string EnsureAbstentionBody(ChatTurnContext context, string content, ComposerLocalization localization)
    {
        if (ShouldPreserveExplanation(context, content))
        {
            return $"{localization.AbstentionLead}\n\n{content.Trim()}".TrimEnd();
        }

        return localization.AbstentionLead;
    }

    private static string AppendIfMissing(string content, string note)
    {
        if (content.Contains(note, StringComparison.OrdinalIgnoreCase))
        {
            return content;
        }

        return $"{content}\n\n{note}".TrimEnd();
    }

    private static bool ShouldPreserveExplanation(ChatTurnContext context, string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        if (context.UncertaintyFlags.Contains("meta_only_research_output_rewritten"))
        {
            return true;
        }

        return content.Contains("meta-ответ", StringComparison.OrdinalIgnoreCase) ||
               content.Contains("meta-only filler", StringComparison.OrdinalIgnoreCase) ||
               content.Contains("could not produce", StringComparison.OrdinalIgnoreCase) ||
               content.Contains("не удалось", StringComparison.OrdinalIgnoreCase);
    }
}
