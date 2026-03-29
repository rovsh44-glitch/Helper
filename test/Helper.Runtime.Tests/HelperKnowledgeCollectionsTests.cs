using Helper.Runtime.Core;

namespace Helper.Runtime.Tests;

public sealed class HelperKnowledgeCollectionsTests
{
    [Fact]
    public void NormalizeWriteCollection_Uses_Helper_Collections()
    {
        Assert.Equal(HelperKnowledgeCollections.CanonicalDefault, HelperKnowledgeCollections.NormalizeWriteCollection(null));
        Assert.Equal(HelperKnowledgeCollections.CanonicalDefault, HelperKnowledgeCollections.NormalizeWriteCollection(HelperKnowledgeCollections.CanonicalDefault));
        Assert.Equal(HelperKnowledgeCollections.CanonicalSemantic, HelperKnowledgeCollections.NormalizeWriteCollection(HelperKnowledgeCollections.CanonicalSemantic));
        Assert.Equal(HelperKnowledgeCollections.CanonicalEpisodic, HelperKnowledgeCollections.NormalizeWriteCollection(HelperKnowledgeCollections.CanonicalEpisodic));
    }

    [Fact]
    public void ExpandReadCandidates_Returns_Helper_Collections_Only()
    {
        Assert.Equal(
            new[] { HelperKnowledgeCollections.CanonicalDefault },
            HelperKnowledgeCollections.ExpandReadCandidates(HelperKnowledgeCollections.CanonicalDefault));

        Assert.Equal(
            new[] { HelperKnowledgeCollections.CanonicalSemantic },
            HelperKnowledgeCollections.ExpandReadCandidates(HelperKnowledgeCollections.CanonicalSemantic));

        Assert.Equal(
            new[] { HelperKnowledgeCollections.CanonicalEpisodic },
            HelperKnowledgeCollections.ExpandReadCandidates(HelperKnowledgeCollections.CanonicalEpisodic));
    }
}
