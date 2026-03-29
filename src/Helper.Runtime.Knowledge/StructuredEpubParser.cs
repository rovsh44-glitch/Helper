using Helper.Runtime.Core;
using HtmlAgilityPack;
using VersOne.Epub;
using DocumentFormatType = Helper.Runtime.Core.DocumentFormat;

namespace Helper.Runtime.Knowledge;

public sealed class StructuredEpubParser : StructuredDocumentParserBase
{
    public override string ParserVersion => "epub_v2";

    public override bool CanParse(string extension) => string.Equals(extension, ".epub", StringComparison.OrdinalIgnoreCase);

    public override async Task<DocumentParseResult> ParseStructuredAsync(string filePath, Func<double, Task>? onProgress = null, CancellationToken ct = default)
    {
        var book = await EpubReader.ReadBookAsync(filePath);
        var pages = new List<DocumentPage>();
        var blocks = new List<DocumentBlock>();
        var readingOrder = 0;
        var syntheticPage = 0;

        foreach (var item in book.ReadingOrder)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(item?.Content))
            {
                continue;
            }

            syntheticPage++;
            var html = item.Content!;
            var sourceId = item.FilePath;
            var pageBlocks = StructuredParserUtilities.ExtractHtmlBlocks(html, syntheticPage, ref readingOrder, sourceId);
            var htmlDoc = new HtmlDocument();
            htmlDoc.LoadHtml(html);
            var rawText = StructuredParserUtilities.NormalizeWhitespace(HtmlEntity.DeEntitize(htmlDoc.DocumentNode.InnerText));
            pages.Add(new DocumentPage(syntheticPage, rawText, pageBlocks, sourceId));
            blocks.AddRange(pageBlocks);
            if (onProgress is not null)
            {
                await onProgress(Math.Clamp((double)syntheticPage / Math.Max(book.ReadingOrder.Count, 1) * 100d, 0d, 100d));
            }
        }

        return StructuredParserUtilities.BuildDocument(
            filePath,
            DocumentFormatType.Epub,
            ParserVersion,
            book.Title ?? Path.GetFileName(filePath),
            pages,
            blocks);
    }
}

