using System.Text.RegularExpressions;
using Helper.Runtime.Core;

namespace Helper.Runtime.Knowledge.Chunking;

internal sealed class ChunkDescriptorEnrichmentService
{
    private const int MaxTitleWords = 14;
    private const int MaxSummaryChars = 240;
    private const int MaxSemanticTerms = 8;

    private static readonly Regex TermRegex = new(@"\p{L}[\p{L}\p{Nd}\-_]{2,}", RegexOptions.Compiled);
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "about", "after", "also", "and", "are", "been", "being", "between", "from", "into", "more", "over",
        "such", "than", "that", "their", "there", "these", "this", "those", "through", "under", "with", "within",
        "как", "или", "для", "что", "это", "этот", "эти", "при", "под", "над", "без", "между", "через", "после",
        "перед", "если", "когда", "почему", "потому", "глава", "section", "chapter", "part", "page", "pages"
    };

    public IReadOnlyList<StructuredChunk> Enrich(IReadOnlyList<StructuredChunk> chunks)
    {
        if (chunks.Count == 0)
        {
            return chunks;
        }

        var enriched = new List<StructuredChunk>(chunks.Count);
        foreach (var chunk in chunks)
        {
            enriched.Add(EnrichChunk(chunk));
        }

        return enriched;
    }

    private static StructuredChunk EnrichChunk(StructuredChunk chunk)
    {
        var normalizedText = StructuredParserUtilities.NormalizeWhitespace(chunk.Text);
        var sectionLeaf = ExtractSectionLeaf(chunk.SectionPath);
        var computedTitle = ComposeTitle(sectionLeaf, BuildLeadTitle(normalizedText), chunk.ChunkRole);
        var title = string.IsNullOrWhiteSpace(chunk.Title) ? computedTitle : chunk.Title!.Trim();
        var computedSummary = BuildSummary(normalizedText, title);
        var summary = string.IsNullOrWhiteSpace(chunk.Summary) ? computedSummary : chunk.Summary!.Trim();

        var metadata = chunk.Metadata is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(chunk.Metadata, StringComparer.OrdinalIgnoreCase);

        var semanticTerms = metadata.TryGetValue("semantic_terms", out var existingTerms) && !string.IsNullOrWhiteSpace(existingTerms)
            ? existingTerms.Trim()
            : string.Join(", ", BuildSemanticTerms(title, sectionLeaf, summary, normalizedText));

        AddIfPresent(metadata, "chunk_title", title);
        AddIfPresent(metadata, "chunk_summary", summary);
        AddIfPresent(metadata, "section_leaf", sectionLeaf);
        AddIfPresent(metadata, "semantic_terms", semanticTerms);
        metadata["chunk_scope"] = chunk.ChunkRole switch
        {
            ChunkRole.Parent => "parent_overview",
            ChunkRole.Child => "child_detail",
            _ => "standalone_detail"
        };
        metadata["chunk_descriptor_version"] = "v1";

        return chunk with
        {
            Title = title,
            Summary = summary,
            Metadata = metadata
        };
    }

    private static string? ExtractSectionLeaf(string? sectionPath)
    {
        var normalized = StructuredParserUtilities.NormalizeWhitespace(sectionPath);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        var parts = normalized
            .Split(">", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static part => !string.IsNullOrWhiteSpace(part))
            .ToArray();
        return parts.Length == 0 ? normalized : parts[^1];
    }

    private static string? BuildLeadTitle(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var firstSentence = SplitIntoSentences(text).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(firstSentence))
        {
            return null;
        }

        return LimitWords(firstSentence, MaxTitleWords);
    }

    private static string? ComposeTitle(string? sectionLeaf, string? leadTitle, ChunkRole role)
    {
        if (string.IsNullOrWhiteSpace(sectionLeaf))
        {
            return leadTitle;
        }

        if (string.IsNullOrWhiteSpace(leadTitle))
        {
            return sectionLeaf;
        }

        if (leadTitle.StartsWith(sectionLeaf, StringComparison.OrdinalIgnoreCase))
        {
            return leadTitle;
        }

        return role == ChunkRole.Parent
            ? sectionLeaf
            : $"{sectionLeaf} - {leadTitle}";
    }

    private static string? BuildSummary(string text, string? title)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var sentences = SplitIntoSentences(text)
            .Where(sentence => !string.Equals(sentence, title, StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToArray();
        var candidate = sentences.Length == 0 ? text : string.Join(" ", sentences);
        if (candidate.Length > MaxSummaryChars)
        {
            candidate = candidate[..MaxSummaryChars].TrimEnd();
        }

        return string.IsNullOrWhiteSpace(candidate) ? null : candidate;
    }

    private static IReadOnlyList<string> BuildSemanticTerms(string? title, string? sectionLeaf, string? summary, string text)
    {
        var corpus = string.Join(
            ' ',
            new[] { sectionLeaf, title, summary, text }
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Select(StructuredParserUtilities.NormalizeWhitespace));

        var terms = new List<string>(MaxSemanticTerms);
        foreach (Match match in TermRegex.Matches(corpus))
        {
            var token = match.Value.Trim('-', '_');
            if (token.Length < 4 || StopWords.Contains(token))
            {
                continue;
            }

            if (terms.Contains(token, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            terms.Add(token);
            if (terms.Count >= MaxSemanticTerms)
            {
                break;
            }
        }

        return terms;
    }

    private static IReadOnlyList<string> SplitIntoSentences(string text)
    {
        return text
            .Split(new[] { ". ", "! ", "? ", "\n" }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(StructuredParserUtilities.NormalizeWhitespace)
            .Where(static sentence => !string.IsNullOrWhiteSpace(sentence))
            .ToArray();
    }

    private static string LimitWords(string text, int maxWords)
    {
        var words = text
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Take(maxWords)
            .ToArray();
        return words.Length == 0 ? text : string.Join(' ', words);
    }

    private static void AddIfPresent(Dictionary<string, string> metadata, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            metadata[key] = value.Trim();
        }
    }
}

