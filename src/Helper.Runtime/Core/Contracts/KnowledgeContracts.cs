using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Helper.Runtime.Core
{
    public record KnowledgeChunk(
        string Id,
        string Content,
        float[] Embedding,
        Dictionary<string, string> Metadata,
        string Collection = HelperKnowledgeCollections.CanonicalDefault);

    public enum DocumentFormat
    {
        Unknown,
        Pdf,
        Epub,
        Html,
        Docx,
        Fb2,
        Markdown,
        Djvu,
        Chm,
        Zim,
        Image,
        Text
    }

    public enum DocumentBlockType
    {
        Unknown,
        Paragraph,
        Heading,
        ListItem,
        Table,
        Caption,
        Footnote,
        Quote,
        Code,
        PageHeader,
        PageFooter,
        ImageDescription
    }

    public enum ChunkStrategyType
    {
        FixedRecursive,
        StructuralRecursive,
        ParentChildStructural,
        TableAware,
        LegalLike,
        SemanticRefined
    }

    public enum ChunkRole
    {
        Standalone,
        Parent,
        Child
    }

    public record DocumentBlock(
        string BlockId,
        DocumentBlockType BlockType,
        string Text,
        int ReadingOrder,
        int? PageNumber = null,
        string? SectionPath = null,
        int? HeadingLevel = null,
        bool IsTable = false,
        bool IsList = false,
        bool IsCaption = false,
        string? ParentBlockId = null,
        Dictionary<string, string>? Attributes = null);

    public record DocumentPage(
        int PageNumber,
        string RawText,
        IReadOnlyList<DocumentBlock> Blocks,
        string? SourceId = null);

    public record DocumentParseResult(
        string DocumentId,
        string SourcePath,
        DocumentFormat Format,
        string Title,
        string ParserVersion,
        IReadOnlyList<DocumentPage> Pages,
        IReadOnlyList<DocumentBlock> Blocks,
        string? Language = null,
        string? PublishedYear = null,
        IReadOnlyList<string>? Warnings = null,
        double StructureConfidence = 0,
        Dictionary<string, string>? Metadata = null);

    public record ChunkPlan(
        ChunkStrategyType Strategy,
        int TargetChunkTokens,
        int TargetOverlapTokens,
        int MaxChunkTokens,
        bool PreserveSections = true,
        bool PreservePageBoundaries = false,
        bool BuildParentChildGraph = false,
        bool TableAware = false,
        string MetadataSchemaVersion = "v2",
        string PipelineVersion = "v2");

    public record StructuredChunk(
        string ChunkId,
        string DocumentId,
        string Text,
        int ChunkIndex,
        ChunkRole ChunkRole,
        string? SectionPath = null,
        int? PageStart = null,
        int? PageEnd = null,
        string? ParentId = null,
        string? PrevChunkId = null,
        string? NextChunkId = null,
        string? Title = null,
        string? Summary = null,
        int ChunkTokenCount = 0,
        Dictionary<string, string>? Metadata = null);

    public record ParentChunk(StructuredChunk Chunk);

    public record ChildChunk(StructuredChunk Chunk, string ParentChunkId);

    public record IndexingTelemetry(
        string PipelineVersion,
        string? ParserVersion = null,
        string? ChunkingStrategy = null,
        string? CurrentSection = null,
        int? CurrentPageStart = null,
        int? CurrentPageEnd = null);

}


