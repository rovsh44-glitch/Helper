using DocumentFormat.OpenXml.Packaging;
using Helper.Runtime.Core;
using WordParagraph = DocumentFormat.OpenXml.Wordprocessing.Paragraph;
using DocumentFormatType = Helper.Runtime.Core.DocumentFormat;

namespace Helper.Runtime.Knowledge;

public sealed class StructuredDocxParser : StructuredDocumentParserBase
{
    public override string ParserVersion => "docx_v2";

    public override bool CanParse(string extension) => string.Equals(extension, ".docx", StringComparison.OrdinalIgnoreCase);

    public override Task<DocumentParseResult> ParseStructuredAsync(string filePath, Func<double, Task>? onProgress = null, CancellationToken ct = default)
    {
        var blocks = new List<DocumentBlock>();
        var readingOrder = 0;

        using var document = WordprocessingDocument.Open(filePath, false);
        var paragraphs = document.MainDocumentPart?.Document?.Body?.Descendants<WordParagraph>() ?? Enumerable.Empty<WordParagraph>();
        foreach (var paragraph in paragraphs)
        {
            ct.ThrowIfCancellationRequested();
            var text = StructuredParserUtilities.NormalizeWhitespace(paragraph.InnerText);
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            var blockType = StructuredParserUtilities.GuessBlockType(text);
            blocks.Add(new DocumentBlock(
                BlockId: $"blk-1-{++readingOrder}",
                BlockType: blockType,
                Text: text,
                ReadingOrder: readingOrder,
                PageNumber: 1,
                HeadingLevel: blockType == DocumentBlockType.Heading ? 1 : null,
                IsTable: blockType == DocumentBlockType.Table,
                IsList: blockType == DocumentBlockType.ListItem,
                IsCaption: blockType == DocumentBlockType.Caption,
                Attributes: new Dictionary<string, string>()));
        }

        var page = new DocumentPage(1, string.Join(Environment.NewLine + Environment.NewLine, blocks.Select(static block => block.Text)), blocks, "page-1");
        return Task.FromResult(StructuredParserUtilities.BuildDocument(
            filePath,
            DocumentFormatType.Docx,
            ParserVersion,
            Path.GetFileName(filePath),
            new[] { page },
            blocks));
    }
}

