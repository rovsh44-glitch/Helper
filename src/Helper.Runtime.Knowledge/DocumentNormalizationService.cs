using Helper.Runtime.Core;

namespace Helper.Runtime.Knowledge;

public sealed class DocumentNormalizationService : IDocumentNormalizer
{
    public Task<DocumentParseResult> NormalizeAsync(DocumentParseResult document, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var headerCandidates = FindRepeatedEdgeBlocks(document.Pages, firstBlock: true);
        var footerCandidates = FindRepeatedEdgeBlocks(document.Pages, firstBlock: false);
        var normalizedBlocks = new List<DocumentBlock>(document.Blocks.Count);

        foreach (var block in document.Blocks.OrderBy(static block => block.ReadingOrder))
        {
            ct.ThrowIfCancellationRequested();
            var text = StructuredParserUtilities.NormalizeWhitespace(block.Text);
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            if (headerCandidates.Contains(text))
            {
                continue;
            }

            if (footerCandidates.Contains(text))
            {
                continue;
            }

            if (LooksLikeOcrNoise(text) && !block.IsTable && !block.IsList)
            {
                continue;
            }

            normalizedBlocks.Add(block with
            {
                Text = text,
                Attributes = block.Attributes is null
                    ? new Dictionary<string, string>()
                    : new Dictionary<string, string>(block.Attributes)
            });
        }

        var normalizedPages = document.Pages
            .Select(page => page with
            {
                RawText = StructuredParserUtilities.NormalizeWhitespace(page.RawText),
                Blocks = normalizedBlocks
                    .Where(block => block.PageNumber == page.PageNumber)
                    .OrderBy(static block => block.ReadingOrder)
                    .ToList()
            })
            .ToList();

        return Task.FromResult(document with
        {
            Pages = normalizedPages,
            Blocks = normalizedBlocks,
            Warnings = document.Warnings ?? Array.Empty<string>()
        });
    }

    private static HashSet<string> FindRepeatedEdgeBlocks(IEnumerable<DocumentPage> pages, bool firstBlock)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var pageCount = 0;

        foreach (var page in pages)
        {
            var orderedBlocks = page.Blocks.OrderBy(static block => block.ReadingOrder).ToList();
            var block = firstBlock ? orderedBlocks.FirstOrDefault() : orderedBlocks.LastOrDefault();
            if (block is null)
            {
                continue;
            }

            var text = StructuredParserUtilities.NormalizeWhitespace(block.Text);
            if (text.Length == 0 || text.Length > 120)
            {
                continue;
            }

            pageCount++;
            counts[text] = counts.TryGetValue(text, out var current) ? current + 1 : 1;
        }

        var threshold = Math.Max(3, (int)Math.Ceiling(pageCount * 0.35));
        return counts
            .Where(pair => pair.Value >= threshold)
            .Select(pair => pair.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static bool LooksLikeOcrNoise(string text)
    {
        if (text.Length < 6)
        {
            return false;
        }

        var lettersOrDigits = text.Count(char.IsLetterOrDigit);
        var weirdCharacters = text.Count(ch => !char.IsLetterOrDigit(ch) && !char.IsWhiteSpace(ch) && ch is not '.' and not ',' and not ':' and not ';' and not '-' and not '(' and not ')');
        return lettersOrDigits > 0 && weirdCharacters > lettersOrDigits && !text.Contains(' ');
    }
}

