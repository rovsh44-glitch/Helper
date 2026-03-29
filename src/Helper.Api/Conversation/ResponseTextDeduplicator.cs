using System.Text.RegularExpressions;

namespace Helper.Api.Conversation;

internal interface IResponseTextDeduplicator
{
    string NormalizePreparedOutput(string preparedOutput, ComposerLocalization localization);
}

internal sealed class ResponseTextDeduplicator : IResponseTextDeduplicator
{
    private static readonly Regex ParagraphBreakRegex = new(@"\r?\n\s*\r?\n", RegexOptions.Compiled);
    private static readonly Regex SentenceSplitRegex = new(@"(?<=[\.\!\?])\s+", RegexOptions.Compiled);

    public string NormalizePreparedOutput(string preparedOutput, ComposerLocalization localization)
    {
        var normalized = string.IsNullOrWhiteSpace(preparedOutput)
            ? localization.NoConcreteSolutionGenerated
            : preparedOutput.Trim();
        return CollapseDuplicateContent(normalized);
    }

    private static string CollapseDuplicateContent(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return content;
        }

        var collapsedParagraphs = CollapseDuplicateParagraphs(content);
        return CollapseDuplicatedSentenceBlock(collapsedParagraphs);
    }

    private static string CollapseDuplicateParagraphs(string content)
    {
        var paragraphs = ParagraphBreakRegex
            .Split(content.Replace("\r\n", "\n", StringComparison.Ordinal))
            .Select(static paragraph => paragraph.Trim())
            .Where(static paragraph => paragraph.Length > 0)
            .ToArray();
        if (paragraphs.Length < 2)
        {
            return content.Trim();
        }

        var unique = new List<string>(paragraphs.Length);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var paragraph in paragraphs)
        {
            var key = Regex.Replace(paragraph, "\\s+", " ").Trim();
            if (!seen.Add(key))
            {
                continue;
            }

            unique.Add(paragraph);
        }

        return string.Join(Environment.NewLine + Environment.NewLine, unique).Trim();
    }

    private static string CollapseDuplicatedSentenceBlock(string content)
    {
        var normalized = Regex.Replace(content, "\\s+", " ").Trim();
        var sentences = SentenceSplitRegex
            .Split(normalized)
            .Select(static sentence => sentence.Trim())
            .Where(static sentence => sentence.Length > 0)
            .ToArray();

        if (sentences.Length < 4 || sentences.Length % 2 != 0)
        {
            return content.Trim();
        }

        var half = sentences.Length / 2;
        for (var index = 0; index < half; index++)
        {
            if (!string.Equals(sentences[index], sentences[index + half], StringComparison.OrdinalIgnoreCase))
            {
                return content.Trim();
            }
        }

        return string.Join(" ", sentences.Take(half)).Trim();
    }
}

