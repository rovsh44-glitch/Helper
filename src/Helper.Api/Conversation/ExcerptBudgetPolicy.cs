using System.Text.RegularExpressions;

namespace Helper.Api.Conversation;

internal interface IExcerptBudgetPolicy
{
    ExcerptBudgetDecision Apply(string? excerpt, int maxWords);
}

internal sealed record ExcerptBudgetDecision(
    string? Text,
    bool Included,
    bool Truncated,
    int OriginalWordCount,
    int RenderedWordCount);

internal sealed partial class ExcerptBudgetPolicy : IExcerptBudgetPolicy
{
    public ExcerptBudgetDecision Apply(string? excerpt, int maxWords)
    {
        var normalized = Normalize(excerpt);
        if (normalized.Length == 0 || maxWords <= 0)
        {
            return new ExcerptBudgetDecision(null, false, false, 0, 0);
        }

        var words = normalized
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0)
        {
            return new ExcerptBudgetDecision(null, false, false, 0, 0);
        }

        if (words.Length <= maxWords)
        {
            return new ExcerptBudgetDecision(normalized, true, false, words.Length, words.Length);
        }

        var clipped = string.Join(" ", words.Take(maxWords)).Trim();
        if (clipped.Length == 0)
        {
            return new ExcerptBudgetDecision(null, false, false, words.Length, 0);
        }

        return new ExcerptBudgetDecision($"{clipped}...", true, true, words.Length, maxWords);
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value
            .ReplaceLineEndings(" ")
            .Replace('\u00A0', ' ');
        normalized = CodeFenceRegex().Replace(normalized, " ");
        normalized = InlineCodeRegex().Replace(normalized, "$1");
        normalized = UrlRegex().Replace(normalized, " ");
        normalized = MarkdownLinkRegex().Replace(normalized, "$1");
        normalized = SpaceRegex().Replace(normalized, " ").Trim();

        return normalized.Trim(' ', '"', '\'', '`');
    }

    [GeneratedRegex("```[\\s\\S]*?```", RegexOptions.Compiled)]
    private static partial Regex CodeFenceRegex();

    [GeneratedRegex("`([^`]+)`", RegexOptions.Compiled)]
    private static partial Regex InlineCodeRegex();

    [GeneratedRegex(@"\[([^\]]+)\]\(([^)]+)\)", RegexOptions.Compiled)]
    private static partial Regex MarkdownLinkRegex();

    [GeneratedRegex(@"https?://\S+", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex UrlRegex();

    [GeneratedRegex(@"\s+", RegexOptions.Compiled)]
    private static partial Regex SpaceRegex();
}

