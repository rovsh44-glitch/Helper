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
        var normalizedMetadata = metadata is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(metadata, StringComparer.OrdinalIgnoreCase);
        var documentId = CreateDocumentId(filePath);
        normalizedMetadata.TryAdd("source_layer", "local_library");
        normalizedMetadata.TryAdd("source_format", NormalizeFormat(format, filePath));
        normalizedMetadata.TryAdd("source_id", documentId);
        normalizedMetadata.TryAdd("display_title", string.IsNullOrWhiteSpace(title) ? Path.GetFileName(filePath) : title.Trim());
        normalizedMetadata.TryAdd("source_path", filePath);
        normalizedMetadata.TryAdd("parser_name", parserVersion.Split('_', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? parserVersion);
        normalizedMetadata.TryAdd("parser_version", parserVersion);

        return new DocumentParseResult(
            DocumentId: documentId,
            SourcePath: filePath,
            Format: format,
            Title: title,
            ParserVersion: parserVersion,
            Pages: pages,
            Blocks: blocks,
            PublishedYear: DetectPublishedYear(title, blocks),
            Warnings: warnings ?? Array.Empty<string>(),
            StructureConfidence: EstimateStructureConfidence(blocks),
            Metadata: normalizedMetadata);
    }

    private static string NormalizeFormat(DocumentFormatType format, string filePath)
    {
        if (format != DocumentFormatType.Unknown)
        {
            return format.ToString().ToLowerInvariant();
        }

        var extension = Path.GetExtension(filePath).TrimStart('.').Trim();
        return string.IsNullOrWhiteSpace(extension) ? "unknown" : extension.ToLowerInvariant();
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

