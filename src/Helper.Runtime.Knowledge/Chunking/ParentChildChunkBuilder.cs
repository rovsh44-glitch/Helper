using Helper.Runtime.Core;

namespace Helper.Runtime.Knowledge.Chunking;

public sealed class ParentChildChunkBuilder : IChunkBuilder
{
    private readonly SemanticChunkBoundaryService _semanticBoundaryService;

    public ParentChildChunkBuilder(SemanticChunkBoundaryService semanticBoundaryService)
    {
        _semanticBoundaryService = semanticBoundaryService;
    }

    public bool CanBuild(ChunkStrategyType strategy)
        => strategy == ChunkStrategyType.ParentChildStructural;

    public Task<IReadOnlyList<StructuredChunk>> BuildChunksAsync(DocumentParseResult document, ChunkPlan plan, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var chunks = new List<StructuredChunk>();
        var chunkIndex = 0;
        var previousChildId = string.Empty;
        var parentBudget = Math.Max(plan.MaxChunkTokens, plan.TargetChunkTokens * 2);

        var sections = ChunkingHelpers.CoalesceSmallGroups(
            document.Blocks
                .OrderBy(static block => block.ReadingOrder)
                .GroupBy(block => block.SectionPath ?? $"page:{block.PageNumber ?? 0}"),
            parentBudget);

        foreach (var section in sections)
        {
            ct.ThrowIfCancellationRequested();
            var sectionBlocks = section.Blocks.ToList();
            var pageNumbers = sectionBlocks
                .Select(static block => block.PageNumber)
                .Where(static page => page.HasValue)
                .Select(static page => page!.Value)
                .ToList();

            var parentText = string.Join(
                Environment.NewLine + Environment.NewLine,
                sectionBlocks.Select(static block => block.Text).Where(static text => !string.IsNullOrWhiteSpace(text)));

            var rawParentParts = ChunkingHelpers.SplitRecursively(parentText, parentBudget).ToList();
            var parentParts = _semanticBoundaryService.Refine(rawParentParts, _semanticBoundaryService.Enabled);
            foreach (var parentPart in parentParts)
            {
                var parentChunk = ChunkingHelpers.CreateChunk(
                    document.DocumentId,
                    parentPart,
                    ++chunkIndex,
                    ChunkRole.Parent,
                    section.Key,
                    pageNumbers.Count == 0 ? null : pageNumbers.Min(),
                    pageNumbers.Count == 0 ? null : pageNumbers.Max(),
                    metadata: new Dictionary<string, string> { ["record_type"] = "parent" });

                chunks.Add(parentChunk);

                var rawChildParts = ChunkingHelpers.SplitRecursively(parentPart, plan.TargetChunkTokens).ToList();
                var childParts = _semanticBoundaryService.Refine(rawChildParts, _semanticBoundaryService.Enabled);
                foreach (var childPart in childParts)
                {
                    var childChunk = ChunkingHelpers.CreateChunk(
                        document.DocumentId,
                        childPart,
                        ++chunkIndex,
                        ChunkRole.Child,
                        section.Key,
                        parentChunk.PageStart,
                        parentChunk.PageEnd,
                        parentId: parentChunk.ChunkId,
                        metadata: new Dictionary<string, string> { ["record_type"] = "child" });

                    if (!string.IsNullOrWhiteSpace(previousChildId))
                    {
                        childChunk = childChunk with { PrevChunkId = previousChildId };
                        var previousIndex = chunks.FindLastIndex(static chunk => chunk.ChunkRole == ChunkRole.Child);
                        if (previousIndex >= 0)
                        {
                            chunks[previousIndex] = chunks[previousIndex] with { NextChunkId = childChunk.ChunkId };
                        }
                    }

                    chunks.Add(childChunk);
                    previousChildId = childChunk.ChunkId;
                }
            }
        }

        return Task.FromResult<IReadOnlyList<StructuredChunk>>(chunks);
    }
}

