using Helper.Runtime.Core;
using Helper.Runtime.Knowledge.Retrieval;
using Moq;

namespace Helper.Runtime.Tests;

public sealed class RetrievalContextExpansionServiceTests
{
    [Fact]
    public async Task ExpandAsync_WhenSourceIsParent_AddsChildrenByParentId()
    {
        var structuredStore = new Mock<IStructuredVectorStore>();
        structuredStore
            .Setup(store => store.SearchByMetadataAsync(
                It.Is<IReadOnlyDictionary<string, string>>(filters => filters["parent_id"] == "parent-1"),
                "knowledge_medicine_v2",
                4,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<KnowledgeChunk>
            {
                CreateChunk("child-point-1", "child-1", "child", parentId: "parent-1"),
                CreateChunk("child-point-2", "child-2", "child", parentId: "parent-1")
            });
        structuredStore
            .Setup(store => store.GetChunksByChunkIdsAsync(It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<KnowledgeChunk>());

        var service = new RetrievalContextExpansionService(structuredStore.Object);
        var expanded = new Dictionary<string, KnowledgeChunk>(StringComparer.OrdinalIgnoreCase);
        var source = CreateChunk("parent-point", "parent-1", "parent");

        await service.ExpandAsync(expanded, source, CancellationToken.None);

        Assert.Contains("child-point-1", expanded.Keys);
        Assert.Contains("child-point-2", expanded.Keys);
    }

    [Fact]
    public async Task ExpandAsync_WhenSourceIsChild_AddsParentAndSiblingButNotSelf()
    {
        var structuredStore = new Mock<IStructuredVectorStore>();
        structuredStore
            .Setup(store => store.GetChunkByChunkIdAsync("knowledge_medicine_v2", "parent-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateChunk("parent-point", "parent-1", "parent"));
        structuredStore
            .Setup(store => store.SearchByMetadataAsync(
                It.Is<IReadOnlyDictionary<string, string>>(filters => filters["parent_id"] == "parent-1"),
                "knowledge_medicine_v2",
                4,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<KnowledgeChunk>
            {
                CreateChunk("child-self-point", "child-1", "child", parentId: "parent-1"),
                CreateChunk("child-sibling-point", "child-2", "child", parentId: "parent-1")
            });
        structuredStore
            .Setup(store => store.GetChunksByChunkIdsAsync(It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<KnowledgeChunk>());

        var service = new RetrievalContextExpansionService(structuredStore.Object);
        var expanded = new Dictionary<string, KnowledgeChunk>(StringComparer.OrdinalIgnoreCase);
        var source = CreateChunk("child-self-point", "child-1", "child", parentId: "parent-1");

        await service.ExpandAsync(expanded, source, CancellationToken.None);

        Assert.Contains("parent-point", expanded.Keys);
        Assert.DoesNotContain("child-self-point", expanded.Keys);
        Assert.Contains("child-sibling-point", expanded.Keys);
    }

    private static KnowledgeChunk CreateChunk(string pointId, string chunkId, string chunkRole, string? parentId = null)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["collection"] = "knowledge_medicine_v2",
            ["chunk_id"] = chunkId,
            ["chunk_role"] = chunkRole,
            ["parent_id"] = parentId ?? string.Empty
        };

        return new KnowledgeChunk(
            pointId,
            "content",
            Array.Empty<float>(),
            metadata,
            "knowledge_medicine_v2");
    }
}

