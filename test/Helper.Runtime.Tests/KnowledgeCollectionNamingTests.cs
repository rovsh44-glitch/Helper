using Helper.Runtime.Knowledge;

namespace Helper.Runtime.Tests;

public sealed class KnowledgeCollectionNamingTests
{
    [Theory]
    [InlineData("D:\\library\\virology\\Encyclopedia of Virology.pdf", null)]
    [InlineData(null, "Bamford Dennis H., Zuckerman Mark (eds.) - Encyclopedia of Virology, 4ed. - 2021.pdf")]
    [InlineData("D:\\library\\math\\Атлас по геометрии.pdf", null)]
    public void IsReferenceLikeSource_ReturnsTrue_ForReferenceMarkers(string? sourcePath, string? title)
    {
        Assert.True(KnowledgeCollectionNaming.IsReferenceLikeSource(sourcePath, title));
    }

    [Fact]
    public void IsReferenceLikeSource_ReturnsFalse_ForRegularBook()
    {
        Assert.False(KnowledgeCollectionNaming.IsReferenceLikeSource(
            "D:\\library\\virology\\Fields Virology Volume 1.pdf",
            "Fields Virology Volume 1"));
    }
}

