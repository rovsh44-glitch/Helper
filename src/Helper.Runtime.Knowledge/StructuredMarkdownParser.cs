using System.Text;
using Helper.Runtime.Core;
using DocumentFormatType = Helper.Runtime.Core.DocumentFormat;

namespace Helper.Runtime.Knowledge;

public sealed class StructuredMarkdownParser : StructuredDocumentParserBase
{
    public override string ParserVersion => "markdown_v2";

    public override bool CanParse(string extension) => extension is ".md" or ".markdown";

    public override async Task<DocumentParseResult> ParseStructuredAsync(string filePath, Func<double, Task>? onProgress = null, CancellationToken ct = default)
    {
        var text = await StructuredParserUtilities.ReadTextWithFallbackAsync(filePath, ct);
        var blocks = new List<DocumentBlock>();
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var buffer = new StringBuilder();
        var sectionStack = new SortedDictionary<int, string>();
        var readingOrder = 0;

        void FlushParagraph()
        {
            var paragraph = StructuredParserUtilities.NormalizeWhitespace(buffer.ToString());
            if (string.IsNullOrWhiteSpace(paragraph))
            {
                buffer.Clear();
                return;
            }

            blocks.Add(new DocumentBlock(
                BlockId: $"blk-1-{++readingOrder}",
                BlockType: StructuredParserUtilities.GuessBlockType(paragraph),
                Text: paragraph,
                ReadingOrder: readingOrder,
                PageNumber: 1,
                SectionPath: string.Join(" > ", sectionStack.Values),
                IsList: paragraph.StartsWith("- ", StringComparison.Ordinal) || paragraph.StartsWith("* ", StringComparison.Ordinal),
                Attributes: new Dictionary<string, string>()));
            buffer.Clear();
        }

        foreach (var rawLine in lines)
        {
            ct.ThrowIfCancellationRequested();
            var line = rawLine.TrimEnd();
            if (string.IsNullOrWhiteSpace(line))
            {
                FlushParagraph();
                continue;
            }

            if (line.StartsWith('#'))
            {
                FlushParagraph();
                var level = line.TakeWhile(static ch => ch == '#').Count();
                var heading = StructuredParserUtilities.NormalizeWhitespace(line.TrimStart('#', ' '));
                sectionStack[level] = heading;
                foreach (var staleLevel in sectionStack.Keys.Where(existing => existing > level).ToList())
                {
                    sectionStack.Remove(staleLevel);
                }

                blocks.Add(new DocumentBlock(
                    BlockId: $"blk-1-{++readingOrder}",
                    BlockType: DocumentBlockType.Heading,
                    Text: heading,
                    ReadingOrder: readingOrder,
                    PageNumber: 1,
                    SectionPath: string.Join(" > ", sectionStack.Values),
                    HeadingLevel: level,
                    Attributes: new Dictionary<string, string>()));
                continue;
            }

            buffer.AppendLine(line);
        }

        FlushParagraph();

        var page = new DocumentPage(1, StructuredParserUtilities.NormalizeWhitespace(text), blocks, "page-1");
        if (onProgress is not null)
        {
            await onProgress(100);
        }

        return StructuredParserUtilities.BuildDocument(
            filePath,
            DocumentFormatType.Markdown,
            ParserVersion,
            Path.GetFileName(filePath),
            new[] { page },
            blocks);
    }
}

