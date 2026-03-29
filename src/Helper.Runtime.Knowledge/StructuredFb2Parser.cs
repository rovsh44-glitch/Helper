using System.Text;
using System.Xml;
using System.Xml.Linq;
using Helper.Runtime.Core;
using Helper.Runtime.Infrastructure;
using DocumentFormatType = Helper.Runtime.Core.DocumentFormat;

namespace Helper.Runtime.Knowledge;

public sealed class StructuredFb2Parser : StructuredDocumentParserBase
{
    public override string ParserVersion => "fb2_v2";

    public override bool CanParse(string extension) => string.Equals(extension, ".fb2", StringComparison.OrdinalIgnoreCase);

    public override Task<DocumentParseResult> ParseStructuredAsync(string filePath, Func<double, Task>? onProgress = null, CancellationToken ct = default)
    {
        EncodingBootstrap.EnsureCodePages();
        using var stream = File.OpenRead(filePath);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Ignore,
            CloseInput = true
        });

        var xml = XDocument.Load(reader, LoadOptions.None);
        var ns = xml.Root?.GetDefaultNamespace() ?? XNamespace.None;
        var blocks = new List<DocumentBlock>();
        var warnings = new List<string>();
        var readingOrder = 0;

        var title = xml.Descendants(ns + "book-title")
            .Select(static element => StructuredParserUtilities.NormalizeWhitespace(element.Value))
            .FirstOrDefault(static text => !string.IsNullOrWhiteSpace(text))
            ?? Path.GetFileName(filePath);

        var bodies = xml.Root?.Elements(ns + "body").ToList() ?? new List<XElement>();
        foreach (var body in bodies)
        {
            ParseFb2Section(body, blocks, ref readingOrder, 1, new Stack<string>());
        }

        if (blocks.Count == 0)
        {
            warnings.Add("fb2_body_empty");
        }

        var page = new DocumentPage(1, string.Join(Environment.NewLine + Environment.NewLine, blocks.Select(static block => block.Text)), blocks, "body-1");
        return Task.FromResult(StructuredParserUtilities.BuildDocument(
            filePath,
            DocumentFormatType.Fb2,
            ParserVersion,
            title,
            new[] { page },
            blocks,
            warnings));
    }

    private static void ParseFb2Section(
        XElement node,
        List<DocumentBlock> blocks,
        ref int readingOrder,
        int pageNumber,
        Stack<string> sectionStack)
    {
        foreach (var child in node.Elements())
        {
            switch (child.Name.LocalName)
            {
                case "title":
                {
                    var titleText = StructuredParserUtilities.NormalizeWhitespace(child.Value);
                    if (string.IsNullOrWhiteSpace(titleText))
                    {
                        break;
                    }

                    sectionStack.Push(titleText);
                    blocks.Add(new DocumentBlock(
                        BlockId: $"blk-{pageNumber}-{++readingOrder}",
                        BlockType: DocumentBlockType.Heading,
                        Text: titleText,
                        ReadingOrder: readingOrder,
                        PageNumber: pageNumber,
                        SectionPath: string.Join(" > ", sectionStack.Reverse()),
                        HeadingLevel: sectionStack.Count,
                        Attributes: new Dictionary<string, string>()));
                    break;
                }
                case "section":
                    ParseFb2Section(child, blocks, ref readingOrder, pageNumber, new Stack<string>(sectionStack.Reverse()));
                    break;
                case "p":
                case "subtitle":
                case "cite":
                case "epigraph":
                case "poem":
                {
                    var text = StructuredParserUtilities.NormalizeWhitespace(child.Value);
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        continue;
                    }

                    var blockType = child.Name.LocalName == "subtitle" ? DocumentBlockType.Heading : StructuredParserUtilities.GuessBlockType(text);
                    blocks.Add(new DocumentBlock(
                        BlockId: $"blk-{pageNumber}-{++readingOrder}",
                        BlockType: blockType,
                        Text: text,
                        ReadingOrder: readingOrder,
                        PageNumber: pageNumber,
                        SectionPath: string.Join(" > ", sectionStack.Reverse()),
                        HeadingLevel: blockType == DocumentBlockType.Heading ? sectionStack.Count + 1 : null,
                        IsList: blockType == DocumentBlockType.ListItem,
                        Attributes: new Dictionary<string, string>()));
                    break;
                }
            }
        }
    }
}

