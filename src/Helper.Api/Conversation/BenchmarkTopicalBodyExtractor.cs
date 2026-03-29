using System.Text;
using System.Text.RegularExpressions;

namespace Helper.Api.Conversation;

internal interface IBenchmarkTopicalBodyExtractor
{
    bool TryExtract(ChatTurnContext context, string solution, out string topicalBody);
}

internal sealed class BenchmarkTopicalBodyExtractor : IBenchmarkTopicalBodyExtractor
{
    private static readonly HashSet<string> BenchmarkTopicStopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "что", "как", "чем", "когда", "если", "или", "для", "после", "затем", "потом", "сейчас", "обычно",
        "простыми", "словами", "нужно", "нужны", "нужен", "можно", "важно", "which", "what", "when", "with",
        "without", "about", "then", "than", "this", "that", "these", "those"
    };

    private readonly IBenchmarkResponseStructurePolicy _structurePolicy;
    private readonly IBenchmarkDraftQualityPolicy _qualityPolicy;

    public BenchmarkTopicalBodyExtractor(
        IBenchmarkResponseStructurePolicy structurePolicy,
        IBenchmarkDraftQualityPolicy qualityPolicy)
    {
        _structurePolicy = structurePolicy;
        _qualityPolicy = qualityPolicy;
    }

    public bool TryExtract(ChatTurnContext context, string solution, out string topicalBody)
    {
        topicalBody = string.Empty;
        if (string.IsNullOrWhiteSpace(solution))
        {
            return false;
        }

        var segments = new List<string>();
        if (_structurePolicy.ContainsAllSections(solution))
        {
            AddSectionIfUseful(segments, _structurePolicy.TryExtractSection(solution, "Local Findings"));
            AddSectionIfUseful(segments, _structurePolicy.TryExtractSection(solution, "Analysis"));
            AddSectionIfUseful(segments, _structurePolicy.TryExtractSection(solution, "Conclusion"));
        }
        else
        {
            AddSectionIfUseful(segments, solution);
        }

        if (segments.Count == 0)
        {
            return false;
        }

        var candidate = string.Join(" ", segments);
        candidate = _structurePolicy.StripFollowUpTail(candidate);
        candidate = _qualityPolicy.StripMetaFallbackContent(candidate);
        candidate = Regex.Replace(candidate, @"(?im)^##\s+.+$", string.Empty);
        candidate = Regex.Replace(candidate, @"\s+", " ").Trim();

        if (candidate.Length < 48 ||
            !HasTopicAnchor(context.Request.Message, candidate) ||
            _qualityPolicy.LooksLowQualityBenchmarkDraft(context, candidate))
        {
            return false;
        }

        if (_qualityPolicy.BenchmarkRequiresRussian(context) &&
            _qualityPolicy.LooksPredominantlyLatin(candidate))
        {
            return false;
        }

        topicalBody = candidate;
        return true;
    }

    private void AddSectionIfUseful(List<string> segments, string? rawSection)
    {
        if (string.IsNullOrWhiteSpace(rawSection))
        {
            return;
        }

        var section = _qualityPolicy.StripMetaFallbackContent(rawSection).Trim();
        if (string.IsNullOrWhiteSpace(section) ||
            _qualityPolicy.LooksLikeResearchFallback(section) ||
            _qualityPolicy.LooksLowQualityBenchmarkDraft(section) ||
            _structurePolicy.IsSectionHeading(section))
        {
            return;
        }

        segments.Add(section);
    }

    private static bool HasTopicAnchor(string topic, string candidate)
    {
        foreach (var token in topic.Split([' ', '\t', '\r', '\n', ',', '.', ';', ':', '!', '?', '(', ')', '[', ']', '{', '}', '"', '\''], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var normalized = NormalizeBenchmarkToken(token);
            if (normalized.Length < 4 || BenchmarkTopicStopWords.Contains(normalized))
            {
                continue;
            }

            if (candidate.Contains(normalized, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string NormalizeBenchmarkToken(string token)
    {
        var builder = new StringBuilder(token.Length);
        foreach (var ch in token)
        {
            if (char.IsLetterOrDigit(ch) || ch == '-')
            {
                builder.Append(char.ToLowerInvariant(ch));
            }
        }

        return builder.ToString();
    }
}

