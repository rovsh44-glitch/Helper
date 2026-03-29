using System.Text.RegularExpressions;

namespace Helper.Runtime.WebResearch.Safety;

public sealed record SanitizedEvidenceText(
    string Text,
    bool WasSanitized,
    IReadOnlyList<string> Flags);

public interface IPromptInjectionSanitizer
{
    SanitizedEvidenceText Sanitize(string? text, IReadOnlyList<string>? knownFlags = null);
}

public sealed class PromptInjectionSanitizer : IPromptInjectionSanitizer
{
    private static readonly (string Flag, Regex Pattern)[] SanitizationPatterns =
    {
        ("instruction_override", BuildRegex(@"ignore\s+(all|any|the|previous|prior)\s+instructions?")),
        ("instruction_override", BuildRegex(@"disregard\s+(all|any|the|previous|prior)\s+instructions?")),
        ("system_prompt_reference", BuildRegex(@"system\s+prompt|developer\s+message|developer\s+instructions?")),
        ("role_injection", BuildRegex(@"\byou\s+are\s+(chatgpt|an\s+assistant|a\s+large\s+language\s+model)\b")),
        ("role_injection", BuildRegex(@"\bact\s+as\b")),
        ("tool_behavior_override", BuildRegex(@"tool\s+call|function\s+call|browse\s+the\s+web|execute\s+the\s+following")),
        ("response_constraint", BuildRegex(@"return\s+only|output\s+only|do\s+not\s+mention|do\s+not\s+cite")),
        ("prompt_delimiter", BuildRegex(@"begin\s+(system|developer|assistant|prompt)|end\s+(system|developer|assistant|prompt)")),
        ("jailbreak_phrase", BuildRegex(@"ignore\s+safety|bypass\s+policy|override\s+policy"))
    };

    private static readonly Regex FenceRegex = new(@"```+", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public SanitizedEvidenceText Sanitize(string? text, IReadOnlyList<string>? knownFlags = null)
    {
        var original = text ?? string.Empty;
        if (original.Length == 0)
        {
            return new SanitizedEvidenceText(string.Empty, false, knownFlags ?? Array.Empty<string>());
        }

        var flags = new HashSet<string>(knownFlags ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        var sanitized = FenceRegex.Replace(original, "'''");
        var wasSanitized = !string.Equals(sanitized, original, StringComparison.Ordinal);

        foreach (var (flag, pattern) in SanitizationPatterns)
        {
            if (!pattern.IsMatch(sanitized))
            {
                continue;
            }

            flags.Add(flag);
            sanitized = pattern.Replace(sanitized, "[instruction-like text removed from untrusted web content]");
            wasSanitized = true;
        }

        sanitized = WhitespaceRegex.Replace(sanitized.Replace('\r', ' ').Replace('\n', ' '), " ").Trim();
        if (sanitized.Length > 0 && sanitized.StartsWith("[instruction-like text removed from untrusted web content]", StringComparison.Ordinal) && sanitized.Length < 64)
        {
            sanitized = "[instruction-like text removed from untrusted web content]";
        }

        return new SanitizedEvidenceText(
            sanitized,
            wasSanitized,
            flags.OrderBy(static flag => flag, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static Regex BuildRegex(string pattern)
    {
        return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);
    }
}

