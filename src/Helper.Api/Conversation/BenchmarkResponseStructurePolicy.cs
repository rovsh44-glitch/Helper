using System.Text.RegularExpressions;

namespace Helper.Api.Conversation;

internal interface IBenchmarkResponseStructurePolicy
{
    bool RequiresLocalFirstBenchmarkSections(string? systemInstruction);
    bool ContainsAllSections(string solution);
    bool IsSectionHeading(string text);
    string StripFollowUpTail(string solution);
    string? TryExtractSection(string solution, string heading);
}

internal sealed class BenchmarkResponseStructurePolicy : IBenchmarkResponseStructurePolicy
{
    private static readonly Regex BenchmarkHeadingRegex = new(
        @"(?im)^##\s+(Local Findings|Web Findings|Sources|Analysis|Conclusion|Opinion)\s*$",
        RegexOptions.Compiled);

    public bool RequiresLocalFirstBenchmarkSections(string? systemInstruction)
    {
        if (string.IsNullOrWhiteSpace(systemInstruction))
        {
            return false;
        }

        return systemInstruction.Contains("## Local Findings", StringComparison.Ordinal) &&
               systemInstruction.Contains("## Web Findings", StringComparison.Ordinal) &&
               systemInstruction.Contains("## Sources", StringComparison.Ordinal) &&
               systemInstruction.Contains("## Analysis", StringComparison.Ordinal) &&
               systemInstruction.Contains("## Conclusion", StringComparison.Ordinal) &&
               systemInstruction.Contains("## Opinion", StringComparison.Ordinal);
    }

    public bool ContainsAllSections(string solution)
    {
        if (string.IsNullOrWhiteSpace(solution))
        {
            return false;
        }

        return solution.Contains("## Local Findings", StringComparison.Ordinal) &&
               solution.Contains("## Web Findings", StringComparison.Ordinal) &&
               solution.Contains("## Sources", StringComparison.Ordinal) &&
               solution.Contains("## Analysis", StringComparison.Ordinal) &&
               solution.Contains("## Conclusion", StringComparison.Ordinal) &&
               solution.Contains("## Opinion", StringComparison.Ordinal);
    }

    public bool IsSectionHeading(string text)
        => !string.IsNullOrWhiteSpace(text) && BenchmarkHeadingRegex.IsMatch(text);

    public string StripFollowUpTail(string solution)
    {
        if (string.IsNullOrWhiteSpace(solution))
        {
            return string.Empty;
        }

        var trimmed = solution.Trim();
        var patterns = new[]
        {
            @"(?ims)\r?\n\r?\nЕсли продолжим, следующий шаг:.*$",
            @"(?ims)\r?\n\r?\nЧто можно сделать дальше:.*$",
            @"(?ims)\r?\n\r?\nСледующий шаг:.*$",
            @"(?ims)\r?\n\r?\nЕсли хотите, дальше могу:.*$",
            @"(?ims)\r?\n\r?\nIf you want, I can next:.*$",
            @"(?ims)\r?\n\r?\nUseful follow-up:.*$",
            @"(?ims)\r?\n\r?\nNext step:.*$"
        };

        foreach (var pattern in patterns)
        {
            trimmed = Regex.Replace(trimmed, pattern, string.Empty);
        }

        return trimmed.Trim();
    }

    public string? TryExtractSection(string solution, string heading)
    {
        var normalized = solution.Replace("\r\n", "\n", StringComparison.Ordinal);
        var startToken = $"## {heading}";
        var startIndex = normalized.IndexOf(startToken, StringComparison.Ordinal);
        if (startIndex < 0)
        {
            return null;
        }

        startIndex += startToken.Length;
        var nextIndex = normalized.IndexOf("\n## ", startIndex, StringComparison.Ordinal);
        var length = nextIndex >= 0 ? nextIndex - startIndex : normalized.Length - startIndex;
        if (length <= 0)
        {
            return null;
        }

        return normalized.Substring(startIndex, length).Trim();
    }
}

