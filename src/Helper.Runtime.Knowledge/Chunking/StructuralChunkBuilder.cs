using Helper.Runtime.Core;

namespace Helper.Runtime.Knowledge.Chunking;

public sealed class StructuralChunkBuilder : IChunkBuilder
{
    private readonly SemanticChunkBoundaryService _semanticBoundaryService;

    public StructuralChunkBuilder(SemanticChunkBoundaryService semanticBoundaryService)
    {
        _semanticBoundaryService = semanticBoundaryService;
    }

    public bool CanBuild(ChunkStrategyType strategy)
        => strategy == ChunkStrategyType.StructuralRecursive;

    public Task<IReadOnlyList<StructuredChunk>> BuildChunksAsync(DocumentParseResult document, ChunkPlan plan, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var chunks = new List<StructuredChunk>();
        var chunkIndex = 0;
        var previousChunkId = string.Empty;

        var sections = ChunkingHelpers.CoalesceSmallGroups(
            document.Blocks
                .OrderBy(static block => block.ReadingOrder)
                .GroupBy(block => block.SectionPath ?? $"page:{block.PageNumber ?? 0}"),
            plan.MaxChunkTokens);

        foreach (var section in sections)
        {
            ct.ThrowIfCancellationRequested();
            var sectionText = string.Join(
                Environment.NewLine + Environment.NewLine,
                section.Blocks.Select(static block => block.Text).Where(static text => !string.IsNullOrWhiteSpace(text)));

            var rawParts = ChunkingHelpers.SplitRecursively(sectionText, plan.MaxChunkTokens).ToList();
            var parts = _semanticBoundaryService.Refine(rawParts, _semanticBoundaryService.Enabled);
            foreach (var part in parts)
            {
                var pageNumbers = section.Blocks.Select(static block => block.PageNumber).Where(static page => page.HasValue).Select(static page => page!.Value).ToList();
                var chunk = ChunkingHelpers.CreateChunk(
                    document.DocumentId,
                    part,
                    ++chunkIndex,
                    ChunkRole.Standalone,
                    section.Key,
                    pageNumbers.Count == 0 ? null : pageNumbers.Min(),
                    pageNumbers.Count == 0 ? null : pageNumbers.Max());

                if (!string.IsNullOrWhiteSpace(previousChunkId))
                {
                    chunk = chunk with { PrevChunkId = previousChunkId };
                    chunks[^1] = chunks[^1] with { NextChunkId = chunk.ChunkId };
                }

                chunks.Add(chunk);
                previousChunkId = chunk.ChunkId;
            }
        }

        return Task.FromResult<IReadOnlyList<StructuredChunk>>(chunks);
    }
}

