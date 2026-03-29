using Helper.Runtime.Core;

namespace Helper.Runtime.Knowledge.Chunking;

public sealed class RecursiveChunkBuilder : IChunkBuilder
{
    private readonly SemanticChunkBoundaryService _semanticBoundaryService;

    public RecursiveChunkBuilder(SemanticChunkBoundaryService semanticBoundaryService)
    {
        _semanticBoundaryService = semanticBoundaryService;
    }

    public bool CanBuild(ChunkStrategyType strategy)
        => strategy is ChunkStrategyType.FixedRecursive or ChunkStrategyType.TableAware or ChunkStrategyType.SemanticRefined;

    public Task<IReadOnlyList<StructuredChunk>> BuildChunksAsync(DocumentParseResult document, ChunkPlan plan, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var chunks = new List<StructuredChunk>();
        var chunkIndex = 0;
        var previousChunkId = string.Empty;
        var overlapSeed = string.Empty;
        var groupedBlocks = document.Blocks
            .OrderBy(static block => block.ReadingOrder)
            .GroupBy(block =>
                plan.PreservePageBoundaries
                    ? $"page:{block.PageNumber}"
                    : plan.PreserveSections
                        ? block.SectionPath ?? "document"
                        : "document");

        foreach (var group in groupedBlocks)
        {
            ct.ThrowIfCancellationRequested();
            var text = string.Join(
                Environment.NewLine + Environment.NewLine,
                group.Select(static block => block.Text).Where(static text => !string.IsNullOrWhiteSpace(text)));

            var rawParts = ChunkingHelpers.SplitRecursively(text, plan.MaxChunkTokens).ToList();
            var parts = _semanticBoundaryService.Refine(
                rawParts,
                plan.Strategy == ChunkStrategyType.SemanticRefined || _semanticBoundaryService.Enabled);

            foreach (var part in parts)
            {
                var effectiveText = string.IsNullOrWhiteSpace(overlapSeed) ? part : $"{overlapSeed} {part}".Trim();
                var pageNumbers = group.Select(static block => block.PageNumber).Where(static page => page.HasValue).Select(static page => page!.Value).ToList();
                var chunk = ChunkingHelpers.CreateChunk(
                    document.DocumentId,
                    effectiveText,
                    ++chunkIndex,
                    ChunkRole.Standalone,
                    plan.PreserveSections ? group.First().SectionPath : null,
                    pageNumbers.Count == 0 ? null : pageNumbers.Min(),
                    pageNumbers.Count == 0 ? null : pageNumbers.Max());

                if (!string.IsNullOrWhiteSpace(previousChunkId))
                {
                    chunk = chunk with { PrevChunkId = previousChunkId };
                    chunks[^1] = chunks[^1] with { NextChunkId = chunk.ChunkId };
                }

                chunks.Add(chunk);
                previousChunkId = chunk.ChunkId;
                overlapSeed = ChunkingHelpers.ComputeOverlap(part, plan.TargetOverlapTokens);
            }
        }

        return Task.FromResult<IReadOnlyList<StructuredChunk>>(chunks);
    }
}

