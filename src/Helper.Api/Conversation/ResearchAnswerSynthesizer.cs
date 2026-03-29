using System.Text.RegularExpressions;
using Helper.Runtime.Core;

namespace Helper.Api.Conversation;

internal sealed record ResearchAnswerFrame(
    int Ordinal,
    string CitationLabel,
    string Title,
    string SourceCategory,
    string FocusText,
    bool FocusDerivedFromEvidence,
    string SupportingExcerpt,
    bool HasClaimContradiction,
    IReadOnlySet<string> TopicTokens,
    IReadOnlySet<string> Numbers,
    ResearchEvidenceItem Evidence);

internal sealed record ResearchSourceDisagreement(
    ResearchAnswerFrame Left,
    ResearchAnswerFrame Right,
    string Kind,
    string Summary);

internal sealed record ResearchAnswerSynthesisPlan(
    IReadOnlyList<ResearchAnswerFrame> Frames,
    ResearchSourceDisagreement? Disagreement,
    bool HasUnsupportedDetails);

internal interface IResearchAnswerSynthesizer
{
    ResearchAnswerSynthesisPlan? Build(
        IReadOnlyList<ClaimGrounding> groundedClaims,
        IReadOnlyList<ResearchEvidenceItem>? evidenceItems);
}

internal sealed partial class ResearchAnswerSynthesizer : IResearchAnswerSynthesizer
{
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "and", "with", "from", "that", "this", "these", "those", "into", "while", "about", "after", "before",
        "source", "sources", "guide", "guidance", "official", "documentation", "report", "reports", "article", "articles",
        "что", "это", "этот", "эта", "эти", "для", "при", "после", "перед", "между", "источник", "источники",
        "официальный", "документация", "отчёт", "отчет", "статья", "статьи"
    };

    public ResearchAnswerSynthesisPlan? Build(
        IReadOnlyList<ClaimGrounding> groundedClaims,
        IReadOnlyList<ResearchEvidenceItem>? evidenceItems)
    {
        if (evidenceItems is not { Count: > 0 } || groundedClaims.Count == 0)
        {
            return null;
        }

        var evidenceByOrdinal = evidenceItems
            .GroupBy(static item => item.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.First());
        var verifiedGroups = groundedClaims
            .Where(static claim => claim.SourceIndex.HasValue)
            .GroupBy(
                static claim => string.IsNullOrWhiteSpace(claim.EvidenceCitationLabel)
                    ? claim.SourceIndex!.Value.ToString()
                    : claim.EvidenceCitationLabel!,
                StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var first = group.First();
                return new
                {
                    CitationLabel = group.Key,
                    Ordinal = first.SourceIndex!.Value,
                    Claims = group.ToArray()
                };
            })
            .Where(group => evidenceByOrdinal.ContainsKey(group.Ordinal))
            .OrderBy(static group => group.Ordinal)
            .ThenBy(static group => group.CitationLabel, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (verifiedGroups.Length == 0)
        {
            return null;
        }

        var frames = verifiedGroups
            .Select(group => BuildFrame(group.Ordinal, group.CitationLabel, group.Claims, evidenceByOrdinal[group.Ordinal]))
            .ToArray();
        var disagreement = DetectDisagreement(frames);
        var hasUnsupportedDetails = groundedClaims.Any(static claim => !claim.SourceIndex.HasValue);

        return new ResearchAnswerSynthesisPlan(frames, disagreement, hasUnsupportedDetails);
    }

    private static ResearchAnswerFrame BuildFrame(
        int ordinal,
        string citationLabel,
        IReadOnlyList<ClaimGrounding> claims,
        ResearchEvidenceItem evidenceItem)
    {
        var representative = claims
            .OrderByDescending(static claim => claim.MatchConfidence)
            .ThenBy(static claim => claim.Claim, StringComparer.OrdinalIgnoreCase)
            .First();
        var focusText = NormalizeClaim(representative);
        if (string.IsNullOrWhiteSpace(focusText))
        {
            focusText = ResolveEvidenceExcerpt(representative, evidenceItem);
        }

        var excerpt = ResolveEvidenceExcerpt(representative, evidenceItem);
        var topicTokens = Tokenize($"{evidenceItem.Title} {focusText}");
        var numbers = ExtractNumbers($"{focusText} {excerpt}");

        return new ResearchAnswerFrame(
            Ordinal: ordinal,
            CitationLabel: citationLabel,
            Title: string.IsNullOrWhiteSpace(evidenceItem.Title) ? $"Source {ordinal}" : evidenceItem.Title.Trim(),
            SourceCategory: ResearchSourceCategoryClassifier.Resolve(evidenceItem),
            FocusText: focusText,
            FocusDerivedFromEvidence: string.IsNullOrWhiteSpace(NormalizeClaim(representative)),
            SupportingExcerpt: excerpt,
            HasClaimContradiction: claims.Any(static claim => claim.ContradictionDetected),
            TopicTokens: topicTokens,
            Numbers: numbers,
            Evidence: evidenceItem);
    }

    private static ResearchSourceDisagreement? DetectDisagreement(IReadOnlyList<ResearchAnswerFrame> frames)
    {
        if (frames.Count < 2)
        {
            return null;
        }

        for (var i = 0; i < frames.Count - 1; i++)
        {
            for (var j = i + 1; j < frames.Count; j++)
            {
                var left = frames[i];
                var right = frames[j];
                var lexicalOverlap = left.TopicTokens.Intersect(right.TopicTokens, StringComparer.OrdinalIgnoreCase).Count();
                var comparableForYearConflict = IsComparableForYearConflict(left, right, lexicalOverlap);
                var comparableForNumericConflict = IsComparableForNumericConflict(left, right, lexicalOverlap);
                var leftYears = ExtractYears(left.Numbers);
                var rightYears = ExtractYears(right.Numbers);
                var yearConflict = comparableForYearConflict &&
                                   leftYears.Count > 0 &&
                                   rightYears.Count > 0 &&
                                   leftYears.Intersect(rightYears, StringComparer.OrdinalIgnoreCase).Count() == 0 &&
                                   lexicalOverlap >= 2;
                var leftComparableNumbers = ExtractComparableNumbers(left.Numbers);
                var rightComparableNumbers = ExtractComparableNumbers(right.Numbers);
                var numericConflict = comparableForNumericConflict &&
                                      leftComparableNumbers.Count > 0 &&
                                      rightComparableNumbers.Count > 0 &&
                                      leftComparableNumbers.Intersect(rightComparableNumbers, StringComparer.OrdinalIgnoreCase).Count() == 0 &&
                                      lexicalOverlap >= 2;
                if (yearConflict || numericConflict)
                {
                    return new ResearchSourceDisagreement(
                        left,
                        right,
                        "numeric_conflict",
                        $"numeric conflict between [{left.CitationLabel}] and [{right.CitationLabel}]");
                }

                if ((left.HasClaimContradiction || right.HasClaimContradiction) &&
                    IsComparableForClaimContradiction(left, right, lexicalOverlap))
                {
                    return new ResearchSourceDisagreement(
                        left,
                        right,
                        "claim_contradiction",
                        $"claim contradiction around [{left.CitationLabel}] and [{right.CitationLabel}]");
                }
            }
        }

        return null;
    }

    internal static string NormalizeClaim(ClaimGrounding claim)
    {
        return (claim.Claim ?? string.Empty).Trim().TrimEnd('.', ';', ':');
    }

    internal static string ResolveEvidenceExcerpt(ClaimGrounding claim, ResearchEvidenceItem evidenceItem)
    {
        if (!string.IsNullOrWhiteSpace(claim.EvidencePassageId) && evidenceItem.Passages is { Count: > 0 })
        {
            var passage = evidenceItem.Passages.FirstOrDefault(p =>
                p.PassageId.Equals(claim.EvidencePassageId, StringComparison.OrdinalIgnoreCase));
            if (passage is not null && !string.IsNullOrWhiteSpace(passage.Text))
            {
                return passage.Text.Trim().TrimEnd('.', ';', ':');
            }
        }

        if (claim.EvidencePassageOrdinal.HasValue && evidenceItem.Passages is { Count: > 0 })
        {
            var passage = evidenceItem.Passages.FirstOrDefault(p => p.PassageOrdinal == claim.EvidencePassageOrdinal.Value);
            if (passage is not null && !string.IsNullOrWhiteSpace(passage.Text))
            {
                return passage.Text.Trim().TrimEnd('.', ';', ':');
            }
        }

        return (evidenceItem.Snippet ?? string.Empty).Trim().TrimEnd('.', ';', ':');
    }

    private static IReadOnlySet<string> Tokenize(string value)
    {
        return SpaceRegex()
            .Replace(NonWordRegex().Replace(value.ToLowerInvariant(), " "), " ")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static token => token.Length >= 4)
            .Where(token => !StopWords.Contains(token))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlySet<string> ExtractNumbers(string value)
    {
        var matches = NumberRegex().Matches(value);
        return matches
            .Select(static match => match.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlySet<string> ExtractComparableNumbers(IReadOnlySet<string> numbers)
    {
        return numbers
            .Where(static value => !IsYear(value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlySet<string> ExtractYears(IReadOnlySet<string> numbers)
    {
        var years = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var number in numbers)
        {
            if (IsYear(number))
            {
                years.Add(number);
            }
        }

        return years;
    }

    private static bool IsComparableForYearConflict(ResearchAnswerFrame left, ResearchAnswerFrame right, int lexicalOverlap)
    {
        if (lexicalOverlap < 2)
        {
            return false;
        }

        if (!string.Equals(left.SourceCategory, right.SourceCategory, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return left.SourceCategory is "release_note" or "clinical_guidance" or "primary_research";
    }

    private static bool IsComparableForNumericConflict(ResearchAnswerFrame left, ResearchAnswerFrame right, int lexicalOverlap)
    {
        if (lexicalOverlap < 2)
        {
            return false;
        }

        if (string.Equals(left.SourceCategory, right.SourceCategory, StringComparison.OrdinalIgnoreCase))
        {
            return left.SourceCategory is "release_note" or "timely_news" or "clinical_guidance" or "primary_research";
        }

        return IsEvidenceFamily(left.SourceCategory) && IsEvidenceFamily(right.SourceCategory);
    }

    private static bool IsComparableForClaimContradiction(ResearchAnswerFrame left, ResearchAnswerFrame right, int lexicalOverlap)
    {
        return lexicalOverlap >= 2 &&
               (string.Equals(left.SourceCategory, right.SourceCategory, StringComparison.OrdinalIgnoreCase) ||
                (IsEvidenceFamily(left.SourceCategory) && IsEvidenceFamily(right.SourceCategory)));
    }

    private static bool IsEvidenceFamily(string category)
    {
        return ResearchSourceCategoryClassifier.IsEvidenceFamily(category);
    }

    private static bool IsYear(string value)
    {
        return int.TryParse(value, out var numeric) && numeric is >= 1900 and <= 2100;
    }

    [GeneratedRegex("[^\\p{L}\\p{Nd}]+", RegexOptions.Compiled)]
    private static partial Regex NonWordRegex();

    [GeneratedRegex("\\s+", RegexOptions.Compiled)]
    private static partial Regex SpaceRegex();

    [GeneratedRegex("\\b\\d{2,4}\\b", RegexOptions.Compiled)]
    private static partial Regex NumberRegex();
}

