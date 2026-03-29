using Helper.Runtime.WebResearch;
using Helper.Runtime.WebResearch.Normalization;

namespace Helper.Runtime.Tests;

public sealed class EventClusterBuilderTests
{
    [Fact]
    public void Build_GroupsSameEventCoverage_WithoutCollapsingDistinctSources()
    {
        var builder = new EventClusterBuilder();
        var documents = new[]
        {
            new WebSearchDocument(
                "https://reuters.com/world/climate-pact",
                "Leaders sign climate pact after overnight talks - Reuters",
                "Leaders sign climate pact after overnight talks in Geneva.",
                ExtractedPage: BuildPage("https://reuters.com/world/climate-pact", "Leaders sign climate pact after overnight talks - Reuters")),
            new WebSearchDocument(
                "https://apnews.com/world/climate-pact",
                "Leaders sign climate pact after overnight talks - AP News",
                "Leaders sign climate pact after overnight talks in Geneva.",
                ExtractedPage: BuildPage("https://apnews.com/world/climate-pact", "Leaders sign climate pact after overnight talks - AP News")),
            new WebSearchDocument(
                "https://example.org/unrelated",
                "Different story",
                "Completely different topic.")
        };

        var result = builder.Build(documents);

        var cluster = Assert.Single(result.Clusters);
        Assert.Equal(2, cluster.Documents.Count);
        Assert.Equal(2, cluster.Hosts.Count);
        Assert.Contains(result.Trace, line => line.Contains("event_cluster[1]", StringComparison.OrdinalIgnoreCase));
    }

    private static ExtractedWebPage BuildPage(string url, string title)
    {
        return new ExtractedWebPage(
            RequestedUrl: url,
            ResolvedUrl: url,
            CanonicalUrl: url,
            Title: title,
            PublishedAt: "2026-03-21",
            Body: "Leaders sign climate pact after overnight talks in Geneva.",
            Passages: new[]
            {
                new ExtractedWebPassage(1, "Leaders sign climate pact after overnight talks in Geneva.")
            },
            ContentType: "text/html");
    }
}

