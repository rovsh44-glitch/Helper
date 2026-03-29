using System.Text.RegularExpressions;

namespace Helper.Runtime.WebResearch.Safety;

public sealed record WebContentSafetyAssessment(
    string TrustLevel,
    bool InjectionSignalsDetected,
    IReadOnlyList<string> Flags,
    IReadOnlyDictionary<int, IReadOnlyList<string>> PassageFlags,
    IReadOnlyList<string> Trace);

public interface IWebContentSafetyPolicy
{
    WebContentSafetyAssessment Assess(ExtractedWebPage page);
}

public sealed class WebContentSafetyPolicy : IWebContentSafetyPolicy
{
    private static readonly (string Flag, Regex Pattern)[] DetectionPatterns =
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

    public WebContentSafetyAssessment Assess(ExtractedWebPage page)
    {
        var pageFlags = CollectFlags(page.Title, page.Body)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var passageFlags = new Dictionary<int, IReadOnlyList<string>>();

        foreach (var passage in page.Passages)
        {
            var flags = CollectFlags(passage.Text)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (flags.Length > 0)
            {
                passageFlags[passage.Ordinal] = flags;
                pageFlags.AddRange(flags);
            }
        }

        var distinctFlags = pageFlags
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var detected = distinctFlags.Length > 0;
        var trace = new List<string>
        {
            $"web_evidence_boundary.trust=untrusted_web_content",
            $"web_evidence_boundary.injection_detected={(detected ? "yes" : "no")}",
            $"web_evidence_boundary.flag_count={distinctFlags.Length}"
        };

        if (distinctFlags.Length > 0)
        {
            trace.Add($"web_evidence_boundary.flags={string.Join(",", distinctFlags)}");
        }

        foreach (var entry in passageFlags.OrderBy(static pair => pair.Key))
        {
            trace.Add($"web_evidence_boundary.passage[{entry.Key}].flags={string.Join(",", entry.Value)}");
        }

        return new WebContentSafetyAssessment(
            "untrusted_web_content",
            detected,
            distinctFlags,
            passageFlags,
            trace);
    }

    private static IEnumerable<string> CollectFlags(params string?[] segments)
    {
        foreach (var segment in segments)
        {
            if (string.IsNullOrWhiteSpace(segment))
            {
                continue;
            }

            foreach (var (flag, pattern) in DetectionPatterns)
            {
                if (pattern.IsMatch(segment))
                {
                    yield return flag;
                }
            }
        }
    }

    private static Regex BuildRegex(string pattern)
    {
        return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);
    }
}

