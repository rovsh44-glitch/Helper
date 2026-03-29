using System.Text.RegularExpressions;
using Helper.Runtime.Core;
using DocumentFormatType = Helper.Runtime.Core.DocumentFormat;

namespace Helper.Runtime.Knowledge;

public sealed class StructureRecoveryService : IStructureRecoveryService
{
    private static readonly Regex FootnoteRegex = new(@"^\(?\d+\)?\s+[A-ZА-Я]", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CaptionRegex = new(@"^(figure|fig\.|table|рис\.|таблица)\s+\d+", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex NumberedHeadingRegex = new(@"^\d+(\.\d+){0,3}\.?\s+[\p{L}""'“‘(\[]", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex RomanHeadingRegex = new(@"^[ivxlcdm]+\.\s+[\p{L}""'“‘(\[]", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public Task<DocumentParseResult> RecoverAsync(DocumentParseResult document, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var sectionStack = new SortedDictionary<int, string>();
        var recoveredBlocks = new List<DocumentBlock>(document.Blocks.Count);

        foreach (var block in document.Blocks.OrderBy(static item => item.ReadingOrder))
        {
            ct.ThrowIfCancellationRequested();
            var blockType = RefineBlockType(block, document.Format);
            var headingLevel = block.HeadingLevel;
            if (blockType == DocumentBlockType.Heading)
            {
                headingLevel ??= InferHeadingLevel(block.Text, document.Format);
                sectionStack[headingLevel.Value] = block.Text.Trim();
                foreach (var stale in sectionStack.Keys.Where(level => level > headingLevel.Value).ToList())
                {
                    sectionStack.Remove(stale);
                }
            }

            var sectionPath = blockType == DocumentBlockType.Heading
                ? string.Join(" > ", sectionStack.Values)
                : block.SectionPath ?? string.Join(" > ", sectionStack.Values);

            recoveredBlocks.Add(block with
            {
                BlockType = blockType,
                HeadingLevel = headingLevel,
                SectionPath = string.IsNullOrWhiteSpace(sectionPath) ? null : sectionPath,
                IsCaption = blockType == DocumentBlockType.Caption,
                IsList = blockType == DocumentBlockType.ListItem,
                IsTable = blockType == DocumentBlockType.Table
            });
        }

        var recoveredPages = document.Pages
            .Select(page => page with
            {
                Blocks = recoveredBlocks
                    .Where(block => block.PageNumber == page.PageNumber)
                    .OrderBy(static block => block.ReadingOrder)
                    .ToList()
            })
            .ToList();

        var structureSignals = recoveredBlocks.Count(block => block.BlockType is DocumentBlockType.Heading or DocumentBlockType.ListItem or DocumentBlockType.Table or DocumentBlockType.Caption);
        var confidence = recoveredBlocks.Count == 0 ? 0 : Math.Round((double)structureSignals / recoveredBlocks.Count, 3);

        return Task.FromResult(document with
        {
            Pages = recoveredPages,
            Blocks = recoveredBlocks,
            StructureConfidence = Math.Max(document.StructureConfidence, confidence)
        });
    }

    private static DocumentBlockType RefineBlockType(DocumentBlock block, DocumentFormatType format)
    {
        if (block.BlockType is DocumentBlockType.Heading or DocumentBlockType.Table or DocumentBlockType.ListItem or DocumentBlockType.Caption)
        {
            return block.BlockType;
        }

        var text = block.Text.Trim();
        if (CaptionRegex.IsMatch(text))
        {
            return DocumentBlockType.Caption;
        }

        if (FootnoteRegex.IsMatch(text) && text.Length < 240)
        {
            return DocumentBlockType.Footnote;
        }

        if (text.StartsWith("- ", StringComparison.Ordinal) || text.StartsWith("* ", StringComparison.Ordinal))
        {
            return DocumentBlockType.ListItem;
        }

        if (text.Contains('|') && text.Count(static ch => ch == '|') >= 2)
        {
            return DocumentBlockType.Table;
        }

        if (LooksLikeHeading(text, format))
        {
            return DocumentBlockType.Heading;
        }

        return block.BlockType == DocumentBlockType.Unknown ? DocumentBlockType.Paragraph : block.BlockType;
    }

    private static int InferHeadingLevel(string text, DocumentFormatType format)
    {
        var numeric = Regex.Match(text, @"^(\d+(\.\d+){0,4})");
        if (numeric.Success)
        {
            return numeric.Value.Count(static ch => ch == '.') + 1;
        }

        return format switch
        {
            DocumentFormatType.Epub => 2,
            DocumentFormatType.Fb2 => 2,
            _ => 1
        };
    }

    private static bool LooksLikeHeading(string text, DocumentFormatType format)
    {
        if (Regex.IsMatch(text.Trim(), @"^\d+(\.\d+){0,4}\.?\s*$", RegexOptions.CultureInvariant))
        {
            return false;
        }

        if (text.Length > 160)
        {
            return false;
        }

        if (Regex.IsMatch(text, @"^(chapter|part|section|appendix)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            || NumberedHeadingRegex.IsMatch(text)
            || RomanHeadingRegex.IsMatch(text))
        {
            return true;
        }

        var letters = text.Count(char.IsLetter);
        if (letters == 0)
        {
            return false;
        }

        var uppercase = text.Count(char.IsUpper);
        var ratio = (double)uppercase / letters;
        if (ratio >= 0.55)
        {
            return true;
        }

        return format is DocumentFormatType.Epub or DocumentFormatType.Fb2 && text.EndsWith(':');
    }
}

