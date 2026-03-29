using System.Text.RegularExpressions;

namespace Helper.Runtime.WebResearch.Normalization;

internal sealed record DuplicateCollapseResult(
    IReadOnlyList<WebSearchDocument> Documents,
    IReadOnlyList<string> Trace);

internal interface IDuplicateContentCollapsePolicy
{
    DuplicateCollapseResult Collapse(IReadOnlyList<WebSearchDocument> documents, string stage);
}

internal sealed partial class DuplicateContentCollapsePolicy : IDuplicateContentCollapsePolicy
{
    private readonly ICanonicalUrlResolver _canonicalUrlResolver;

    public DuplicateContentCollapsePolicy(ICanonicalUrlResolver? canonicalUrlResolver = null)
    {
        _canonicalUrlResolver = canonicalUrlResolver ?? new CanonicalUrlResolver();
    }

    public DuplicateCollapseResult Collapse(IReadOnlyList<WebSearchDocument> documents, string stage)
    {
        if (documents.Count == 0)
        {
            return new DuplicateCollapseResult(Array.Empty<WebSearchDocument>(), Array.Empty<string>());
        }

        var kept = new List<WebSearchDocument>(documents.Count);
        var trace = new List<string>();
        var canonicalIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var fingerprintIndex = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var document in documents)
        {
            var canonical = _canonicalUrlResolver.Resolve(document).CanonicalUrl;
            if (canonicalIndex.TryGetValue(canonical, out var canonicalPosition))
            {
                var existing = kept[canonicalPosition];
                if (ShouldReplace(existing, document))
                {
                    kept[canonicalPosition] = document;
                    trace.Add($"web_normalization.duplicate_replaced stage={stage} reason=canonical_url canonical={canonical}");
                }
                else
                {
                    trace.Add($"web_normalization.duplicate_collapsed stage={stage} reason=canonical_url dropped={document.Url} kept={existing.Url}");
                }

                continue;
            }

            var mirrorFingerprint = BuildMirrorFingerprint(document);
            if (!string.IsNullOrWhiteSpace(mirrorFingerprint) &&
                fingerprintIndex.TryGetValue(mirrorFingerprint, out var mirrorPosition))
            {
                var existing = kept[mirrorPosition];
                if (ShouldReplace(existing, document))
                {
                    kept[mirrorPosition] = document;
                    trace.Add($"web_normalization.duplicate_replaced stage={stage} reason=content_mirror fingerprint={mirrorFingerprint}");
                }
                else
                {
                    trace.Add($"web_normalization.duplicate_collapsed stage={stage} reason=content_mirror dropped={document.Url} kept={existing.Url}");
                }

                continue;
            }

            canonicalIndex[canonical] = kept.Count;
            if (!string.IsNullOrWhiteSpace(mirrorFingerprint))
            {
                fingerprintIndex[mirrorFingerprint] = kept.Count;
            }

            kept.Add(document);
        }

        if (trace.Count > 0)
        {
            var collapseCount = trace.Count(line => line.Contains("duplicate_", StringComparison.OrdinalIgnoreCase));
            trace.Insert(0, $"web_normalization.duplicates_collapsed stage={stage} count={collapseCount}");
        }

        return new DuplicateCollapseResult(kept, trace);
    }

    private static bool ShouldReplace(WebSearchDocument current, WebSearchDocument candidate)
    {
        var currentScore = ScoreSpecificity(current);
        var candidateScore = ScoreSpecificity(candidate);
        return candidateScore > currentScore;
    }

    private static int ScoreSpecificity(WebSearchDocument document)
    {
        var score = 0;
        if (document.ExtractedPage is not null)
        {
            score += 3;
            score += document.ExtractedPage.Passages.Count;
        }

        if (!document.IsFallback)
        {
            score += 2;
        }

        score += Math.Min(document.Snippet?.Length ?? 0, 300) / 50;
        return score;
    }

    private static string? BuildMirrorFingerprint(WebSearchDocument document)
    {
        var title = NormalizeText(StripSourceSuffix(document.ExtractedPage?.Title ?? document.Title));
        var excerpt = NormalizeText(ResolveExcerpt(document));
        if (title.Length < 16 || excerpt.Length < 80)
        {
            return null;
        }

        var published = document.ExtractedPage?.PublishedAt?.Trim();
        var excerptPrefix = excerpt.Length <= 220 ? excerpt : excerpt[..220];
        return $"{published}|{title}|{excerptPrefix}";
    }

    private static string ResolveExcerpt(WebSearchDocument document)
    {
        if (document.ExtractedPage is { Passages.Count: > 0 } extractedPage)
        {
            var text = string.Join(" ", extractedPage.Passages.Take(2).Select(static passage => passage.Text));
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        if (!string.IsNullOrWhiteSpace(document.ExtractedPage?.Body))
        {
            return document.ExtractedPage.Body;
        }

        return document.Snippet ?? string.Empty;
    }

    private static string StripSourceSuffix(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return string.Empty;
        }

        var separators = new[] { " | ", " - ", " — ", " – ", " :: " };
        foreach (var separator in separators)
        {
            var index = title.LastIndexOf(separator, StringComparison.Ordinal);
            if (index > 20)
            {
                return title[..index];
            }
        }

        return title;
    }

    private static string NormalizeText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return SpaceRegex()
            .Replace(NonWordRegex().Replace(value.ToLowerInvariant(), " "), " ")
            .Trim();
    }

    [GeneratedRegex("[^\\p{L}\\p{Nd}]+", RegexOptions.Compiled)]
    private static partial Regex NonWordRegex();

    [GeneratedRegex("\\s+", RegexOptions.Compiled)]
    private static partial Regex SpaceRegex();
}

