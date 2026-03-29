using Helper.Runtime.Core;
using DocumentFormatType = Helper.Runtime.Core.DocumentFormat;

namespace Helper.Runtime.Knowledge.Chunking;

public sealed class ChunkingStrategyResolver : IChunkingStrategyResolver
{
    public Task<ChunkPlan> ResolveAsync(DocumentParseResult document, string domain, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var normalizedDomain = string.IsNullOrWhiteSpace(domain) ? "generic" : domain.Trim().ToLowerInvariant();
        var structureConfidence = document.StructureConfidence;
        var hasTables = document.Blocks.Any(static block => block.BlockType == DocumentBlockType.Table);
        var isLongDocument = document.Blocks.Count > 120 || ChunkingHelpers.EstimateTokenCount(string.Join("\n", document.Blocks.Select(static block => block.Text))) > 4500;
        var semanticRefinementEnabled = SemanticChunkBoundaryService.IsFeatureEnabled();

        var strategy = ChunkStrategyType.FixedRecursive;
        var overlap = 50;
        var maxTokens = 350;
        var targetTokens = 240;
        var parentChild = false;
        var preserveSections = structureConfidence >= 0.1;

        if (hasTables || KnowledgeCollectionNaming.IsEncyclopediaLikeDomain(normalizedDomain))
        {
            strategy = ChunkStrategyType.TableAware;
            targetTokens = 220;
            maxTokens = 300;
        }
        else if (normalizedDomain is "history" or "medicine" or "programming" or "psychology" or "virology" or "analysis_strategy" or "social_sciences")
        {
            strategy = structureConfidence >= 0.12 ? ChunkStrategyType.ParentChildStructural : ChunkStrategyType.StructuralRecursive;
            targetTokens = 260;
            maxTokens = 380;
            overlap = 60;
            parentChild = strategy == ChunkStrategyType.ParentChildStructural;
        }
        else if (structureConfidence >= 0.12)
        {
            strategy = ChunkStrategyType.StructuralRecursive;
            targetTokens = 250;
            maxTokens = 360;
            overlap = 55;
        }

        if (semanticRefinementEnabled && !hasTables && !preserveSections && isLongDocument)
        {
            strategy = ChunkStrategyType.SemanticRefined;
            targetTokens = 250;
            maxTokens = 340;
            overlap = 60;
        }

        if (isLongDocument && strategy == ChunkStrategyType.StructuralRecursive)
        {
            strategy = ChunkStrategyType.ParentChildStructural;
            parentChild = true;
        }

        return Task.FromResult(new ChunkPlan(
            Strategy: strategy,
            TargetChunkTokens: targetTokens,
            TargetOverlapTokens: overlap,
            MaxChunkTokens: maxTokens,
            PreserveSections: preserveSections,
            PreservePageBoundaries: document.Format is DocumentFormatType.Pdf or DocumentFormatType.Djvu,
            BuildParentChildGraph: parentChild,
            TableAware: hasTables,
            MetadataSchemaVersion: "v2",
            PipelineVersion: "v2"));
    }
}

