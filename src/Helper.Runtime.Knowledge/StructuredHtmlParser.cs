using Helper.Runtime.Core;
using HtmlAgilityPack;
using DocumentFormatType = Helper.Runtime.Core.DocumentFormat;

namespace Helper.Runtime.Knowledge;

public sealed class StructuredHtmlParser : StructuredDocumentParserBase
{
    public override string ParserVersion => "html_v2";

    public override bool CanParse(string extension) => extension is ".html" or ".htm";

    public override async Task<DocumentParseResult> ParseStructuredAsync(string filePath, Func<double, Task>? onProgress = null, CancellationToken ct = default)
    {
        var html = await StructuredParserUtilities.ReadTextWithFallbackAsync(filePath, ct);
        var htmlDoc = new HtmlDocument();
        htmlDoc.LoadHtml(html);
        var readingOrder = 0;
        var blocks = StructuredParserUtilities.ExtractHtmlBlocks(html, 1, ref readingOrder, Path.GetFileName(filePath));
        var page = new DocumentPage(1, StructuredParserUtilities.NormalizeWhitespace(HtmlEntity.DeEntitize(htmlDoc.DocumentNode.InnerText)), blocks, Path.GetFileName(filePath));
        if (onProgress is not null)
        {
            await onProgress(100);
        }

        return StructuredParserUtilities.BuildDocument(
            filePath,
            DocumentFormatType.Html,
            ParserVersion,
            Path.GetFileName(filePath),
            new[] { page },
            blocks);
    }
}

