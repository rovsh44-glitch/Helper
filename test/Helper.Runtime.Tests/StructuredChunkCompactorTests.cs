using Helper.Runtime.Core;
using Helper.Runtime.Knowledge.Chunking;

namespace Helper.Runtime.Tests;

public sealed class StructuredChunkCompactorTests
{
    [Fact]
    public void Compact_MergesLeadingTinyStandaloneChunkIntoFollowingChunk()
    {
        var plan = new ChunkPlan(
            Strategy: ChunkStrategyType.StructuralRecursive,
            TargetChunkTokens: 240,
            TargetOverlapTokens: 50,
            MaxChunkTokens: 380);

        var chunks = new[]
        {
            CreateChunk("doc-1", 1, ChunkRole.Standalone, "INTRODUCTION", 8),
            CreateChunk("doc-1", 2, ChunkRole.Standalone, "This is a substantial body paragraph with enough distinct words, explanatory detail, retrieval context, and normal narrative spacing to represent a realistic chapter segment from a technical or literary work.", 180),
            CreateChunk("doc-1", 3, ChunkRole.Standalone, "Another substantial paragraph follows with additional terminology, examples, and supporting context so that it should remain a separate chunk after the leading heading fragment has been merged into the previous body paragraph.", 170)
        };

        var compacted = StructuredChunkCompactor.Compact(chunks, plan);

        Assert.Equal(2, compacted.Count);
        Assert.Contains("INTRODUCTION", compacted[0].Text, StringComparison.Ordinal);
        Assert.Contains("substantial body paragraph", compacted[0].Text, StringComparison.Ordinal);
        Assert.Equal(1, compacted[0].ChunkIndex);
        Assert.Equal(compacted[1].ChunkId, compacted[0].NextChunkId);
        Assert.Equal(compacted[0].ChunkId, compacted[1].PrevChunkId);
    }

    [Fact]
    public void Compact_DoesNotMergeChildrenAcrossDifferentParents()
    {
        var plan = new ChunkPlan(
            Strategy: ChunkStrategyType.ParentChildStructural,
            TargetChunkTokens: 240,
            TargetOverlapTokens: 50,
            MaxChunkTokens: 380,
            BuildParentChildGraph: true);

        var parentOne = CreateChunk("doc-2", 1, ChunkRole.Parent, "Parent one text", 180);
        var childOne = CreateChunk("doc-2", 2, ChunkRole.Child, "Short child", 12, parentId: parentOne.ChunkId);
        var parentTwo = CreateChunk("doc-2", 3, ChunkRole.Parent, "Parent two text", 180);
        var childTwo = CreateChunk("doc-2", 4, ChunkRole.Child, "Another short child", 14, parentId: parentTwo.ChunkId);

        var compacted = StructuredChunkCompactor.Compact(new[] { parentOne, childOne, parentTwo, childTwo }, plan);

        Assert.Equal(4, compacted.Count);
        Assert.Equal(ChunkRole.Parent, compacted[0].ChunkRole);
        Assert.Equal(ChunkRole.Child, compacted[1].ChunkRole);
        Assert.Equal(parentOne.ChunkId, compacted[1].ParentId);
        Assert.Equal(parentTwo.ChunkId, compacted[3].ParentId);
        Assert.Null(compacted[0].PrevChunkId);
        Assert.Null(compacted[0].NextChunkId);
    }

    private static StructuredChunk CreateChunk(string documentId, int index, ChunkRole role, string text, int tokens, string? parentId = null)
        => new(
            ChunkId: $"{role}-{index}",
            DocumentId: documentId,
            Text: text,
            ChunkIndex: index,
            ChunkRole: role,
            ParentId: parentId,
            ChunkTokenCount: tokens);
}

