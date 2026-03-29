using Helper.Runtime.Core;
using Helper.Runtime.Knowledge;

namespace Helper.Runtime.Tests;

public sealed class StructuredIndexQualityGateTests
{
    [Fact]
    public void EnsureAccepted_AllowsHealthyNarrativeChunks()
    {
        var chunks = Enumerable.Range(0, 25)
            .Select(index => new StructuredChunk(
                ChunkId: $"chunk-{index}",
                DocumentId: "doc-healthy",
                Text: $"This is a healthy narrative chunk number {index} with regular spacing, repeated context, and enough prose to look like a normal book paragraph for retrieval quality validation.",
                ChunkIndex: index,
                ChunkRole: ChunkRole.Standalone,
                ChunkTokenCount: 96))
            .ToArray();

        var flatContent = string.Join(Environment.NewLine, chunks.Select(static chunk => chunk.Text));
        var document = CreateDocument("doc-healthy", flatContent);

        StructuredIndexQualityGate.EnsureAccepted("healthy.md", document, chunks, flatContent);
    }

    [Fact]
    public void EnsureAccepted_RejectsSpacePoorAndLongWordChunks()
    {
        var collapsedToken = new string('A', 220);
        var malformedChunk = string.Concat(Enumerable.Repeat(collapsedToken, 3));
        var chunks = Enumerable.Range(0, 25)
            .Select(index => new StructuredChunk(
                ChunkId: $"chunk-{index}",
                DocumentId: "doc-malformed",
                Text: malformedChunk,
                ChunkIndex: index,
                ChunkRole: ChunkRole.Standalone,
                ChunkTokenCount: 180))
            .ToArray();

        var flatContent = string.Join(Environment.NewLine, chunks.Select(static chunk => chunk.Text));
        var document = CreateDocument("doc-malformed", flatContent);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            StructuredIndexQualityGate.EnsureAccepted("malformed.pdf", document, chunks, flatContent));

        Assert.Contains("space_poor_chunks=", ex.Message, StringComparison.Ordinal);
        Assert.Contains("max_word_len=", ex.Message, StringComparison.Ordinal);
    }

    private static DocumentParseResult CreateDocument(string documentId, string flatContent)
    {
        var block = new DocumentBlock(
            BlockId: "block-1",
            BlockType: DocumentBlockType.Paragraph,
            Text: flatContent,
            ReadingOrder: 0);

        return new DocumentParseResult(
            DocumentId: documentId,
            SourcePath: $"{documentId}.md",
            Format: Helper.Runtime.Core.DocumentFormat.Markdown,
            Title: documentId,
            ParserVersion: "test",
            Pages: Array.Empty<DocumentPage>(),
            Blocks: new[] { block },
            Warnings: Array.Empty<string>());
    }
}

