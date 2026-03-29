using Helper.Runtime.Core;
using DocumentFormatType = Helper.Runtime.Core.DocumentFormat;

namespace Helper.Runtime.Knowledge;

internal static partial class StructuredParserUtilities
{
    public static DocumentParseResult BuildDocument(
        string filePath,
        DocumentFormatType format,
        string parserVersion,
        string title,
        IReadOnlyList<DocumentPage> pages,
        IReadOnlyList<DocumentBlock> blocks,
        IReadOnlyList<string>? warnings = null,
        Dictionary<string, string>? metadata = null)
    {
        return new DocumentParseResult(
            DocumentId: CreateDocumentId(filePath),
            SourcePath: filePath,
            Format: format,
            Title: title,
            ParserVersion: parserVersion,
            Pages: pages,
            Blocks: blocks,
            PublishedYear: DetectPublishedYear(title, blocks),
            Warnings: warnings ?? Array.Empty<string>(),
            StructureConfidence: EstimateStructureConfidence(blocks),
            Metadata: metadata ?? new Dictionary<string, string>());
    }

    private static double EstimateStructureConfidence(IReadOnlyList<DocumentBlock> blocks)
    {
        if (blocks.Count == 0)
        {
            return 0;
        }

        var scored = blocks.Count(block => block.BlockType is DocumentBlockType.Heading or DocumentBlockType.ListItem or DocumentBlockType.Table or DocumentBlockType.Caption);
        return Math.Round((double)scored / blocks.Count, 3);
    }
}

