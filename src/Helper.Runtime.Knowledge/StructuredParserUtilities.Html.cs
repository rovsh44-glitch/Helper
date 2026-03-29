using Helper.Runtime.Core;
using HtmlAgilityPack;

namespace Helper.Runtime.Knowledge;

internal static partial class StructuredParserUtilities
{
    public static IReadOnlyList<DocumentBlock> ExtractHtmlBlocks(string html, int pageNumber, ref int readingOrder, string? sourceId = null)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        var root = doc.DocumentNode.SelectSingleNode("//body") ?? doc.DocumentNode;
        var blocks = new List<DocumentBlock>();
        var sectionStack = new SortedDictionary<int, string>();

        foreach (var node in EnumerateMaterializableHtmlNodes(root))
        {
            var tag = node.Name.ToLowerInvariant();
            var text = NormalizeWhitespace(tag == "table" ? SerializeTable(node) : HtmlEntity.DeEntitize(node.InnerText));
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            DocumentBlockType blockType;
            int? headingLevel = null;
            switch (tag)
            {
                case "h1":
                case "h2":
                case "h3":
                case "h4":
                case "h5":
                case "h6":
                    headingLevel = int.Parse(tag.Substring(1), System.Globalization.CultureInfo.InvariantCulture);
                    blockType = DocumentBlockType.Heading;
                    sectionStack[headingLevel.Value] = text;
                    foreach (var staleLevel in sectionStack.Keys.Where(level => level > headingLevel.Value).ToList())
                    {
                        sectionStack.Remove(staleLevel);
                    }
                    break;
                case "li":
                    blockType = DocumentBlockType.ListItem;
                    break;
                case "table":
                    blockType = DocumentBlockType.Table;
                    break;
                case "caption":
                case "figcaption":
                    blockType = DocumentBlockType.Caption;
                    break;
                case "blockquote":
                    blockType = DocumentBlockType.Quote;
                    break;
                case "pre":
                case "code":
                    blockType = DocumentBlockType.Code;
                    break;
                default:
                    blockType = GuessBlockType(text);
                    break;
            }

            if (!ShouldMaterializeNode(tag, blockType))
            {
                continue;
            }

            blocks.Add(new DocumentBlock(
                BlockId: $"blk-{pageNumber}-{++readingOrder}",
                BlockType: blockType,
                Text: text,
                ReadingOrder: readingOrder,
                PageNumber: pageNumber,
                SectionPath: BuildSectionPath(sectionStack.Values),
                HeadingLevel: headingLevel,
                IsTable: blockType == DocumentBlockType.Table,
                IsList: blockType == DocumentBlockType.ListItem,
                IsCaption: blockType == DocumentBlockType.Caption,
                Attributes: sourceId is null ? new Dictionary<string, string>() : new Dictionary<string, string> { ["source_id"] = sourceId }));
        }

        return blocks;
    }

    private static IEnumerable<HtmlNode> EnumerateMaterializableHtmlNodes(HtmlNode root)
    {
        foreach (var child in root.ChildNodes.Where(static node => node.NodeType == HtmlNodeType.Element))
        {
            var tag = child.Name.ToLowerInvariant();
            if (ShouldSkipHtmlTag(tag))
            {
                continue;
            }

            if (IsPreferredBlockTag(tag))
            {
                yield return child;
                continue;
            }

            var yieldedNested = false;
            foreach (var nested in EnumerateMaterializableHtmlNodes(child))
            {
                yieldedNested = true;
                yield return nested;
            }

            if (yieldedNested)
            {
                continue;
            }

            var text = NormalizeWhitespace(HtmlEntity.DeEntitize(child.InnerText));
            if (!string.IsNullOrWhiteSpace(text))
            {
                yield return child;
            }
        }
    }

    private static bool IsPreferredBlockTag(string tag)
        => tag is "p" or "h1" or "h2" or "h3" or "h4" or "h5" or "h6" or "li" or "table" or "caption" or "figcaption" or "blockquote" or "pre" or "code" or "dd" or "dt";

    private static bool ShouldSkipHtmlTag(string tag)
        => tag is "html" or "body" or "head" or "title" or "meta" or "script" or "style" or "br" or "hr" or "svg" or "img" or "image";

    private static string BuildSectionPath(IEnumerable<string> sections)
        => string.Join(" > ", sections.Where(static value => !string.IsNullOrWhiteSpace(value)));

    private static bool ShouldMaterializeNode(string tag, DocumentBlockType blockType)
    {
        if (ShouldSkipHtmlTag(tag))
        {
            return false;
        }

        return blockType != DocumentBlockType.Unknown;
    }

    private static string SerializeTable(HtmlNode tableNode)
    {
        var rows = tableNode.SelectNodes(".//tr");
        if (rows is null || rows.Count == 0)
        {
            return HtmlEntity.DeEntitize(tableNode.InnerText);
        }

        var serializedRows = new List<string>();
        foreach (var row in rows)
        {
            var cells = row.SelectNodes("./th|./td");
            if (cells is null || cells.Count == 0)
            {
                continue;
            }

            serializedRows.Add(string.Join(" | ", cells.Select(static cell => NormalizeWhitespace(HtmlEntity.DeEntitize(cell.InnerText)))));
        }

        return string.Join(Environment.NewLine, serializedRows);
    }
}

