using Helper.Runtime.Core;
using Helper.Runtime.Knowledge.Chunking;

namespace Helper.Runtime.Tests;

public sealed class ChunkDescriptorEnrichmentServiceTests
{
    [Fact]
    public void Enrich_AddsTitleSummaryAndSemanticTerms()
    {
        var service = new ChunkDescriptorEnrichmentService();
        var chunk = new StructuredChunk(
            ChunkId: "chunk-1",
            DocumentId: "doc-1",
            Text: "Профилактика мигрени обычно включает контроль триггеров, нормализацию сна и обсуждение профилактической терапии при частых приступах.",
            ChunkIndex: 1,
            ChunkRole: ChunkRole.Standalone,
            SectionPath: "Неврология > Профилактика мигрени");

        var enriched = service.Enrich(new[] { chunk }).Single();

        Assert.NotNull(enriched.Title);
        Assert.Contains("Профилактика мигрени", enriched.Title!, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(enriched.Summary);
        Assert.Contains("контроль триггеров", enriched.Summary!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("standalone_detail", enriched.Metadata!["chunk_scope"]);
        Assert.Contains("мигрени", enriched.Metadata["semantic_terms"], StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Профилактика мигрени", enriched.Metadata["section_leaf"]);
    }

    [Fact]
    public void Enrich_PreservesExistingTitleAndSummary_WhenAlreadyPresent()
    {
        var service = new ChunkDescriptorEnrichmentService();
        var chunk = new StructuredChunk(
            ChunkId: "chunk-2",
            DocumentId: "doc-1",
            Text: "Clinical recommendations discuss first-line prevention and when to escalate therapy.",
            ChunkIndex: 2,
            ChunkRole: ChunkRole.Parent,
            SectionPath: "Guidelines > Migraine prevention",
            Title: "Existing title",
            Summary: "Existing summary");

        var enriched = service.Enrich(new[] { chunk }).Single();

        Assert.Equal("Existing title", enriched.Title);
        Assert.Equal("Existing summary", enriched.Summary);
        Assert.Equal("parent_overview", enriched.Metadata!["chunk_scope"]);
        Assert.Equal("Existing title", enriched.Metadata["chunk_title"]);
        Assert.Equal("Existing summary", enriched.Metadata["chunk_summary"]);
    }
}

