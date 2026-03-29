using Helper.Runtime.WebResearch;
using Helper.Runtime.WebResearch.Normalization;

namespace Helper.Runtime.Tests;

public sealed class DuplicateContentCollapsePolicyTests
{
    [Fact]
    public void Collapse_RemovesCanonicalDuplicates_KeepingMoreSpecificDocument()
    {
        var policy = new DuplicateContentCollapsePolicy();
        var page = new ExtractedWebPage(
            RequestedUrl: "https://example.org/post",
            ResolvedUrl: "https://example.org/post",
            CanonicalUrl: "https://example.org/post",
            Title: "Canonical Post",
            PublishedAt: "2026-03-21",
            Body: "Full evidence body for the canonical post.",
            Passages: new[]
            {
                new ExtractedWebPassage(1, "Full evidence body for the canonical post.")
            },
            ContentType: "text/html");

        var result = policy.Collapse(
            new[]
            {
                new WebSearchDocument("https://example.org/post/?utm_source=feed", "Post", "Thin snippet."),
                new WebSearchDocument("https://example.org/post", "Canonical Post", "Full snippet.", ExtractedPage: page)
            },
            "post_fetch");

        var document = Assert.Single(result.Documents);
        Assert.NotNull(document.ExtractedPage);
        Assert.Equal("https://example.org/post", document.Url);
        Assert.Contains(result.Trace, line => line.Contains("reason=canonical_url", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Collapse_RemovesContentMirrors_AcrossDifferentHosts()
    {
        var policy = new DuplicateContentCollapsePolicy();
        var original = new WebSearchDocument(
            "https://reuters.com/world/example",
            "Leaders sign climate pact - Reuters",
            "Leaders sign the climate pact after overnight talks and publish the same commitments.");
        var mirror = new WebSearchDocument(
            "https://mirror.example.org/reuters/world/example",
            "Leaders sign climate pact - Mirror",
            "Leaders sign the climate pact after overnight talks and publish the same commitments.");

        var result = policy.Collapse(new[] { original, mirror }, "provider");

        Assert.Single(result.Documents);
        Assert.Equal("https://reuters.com/world/example", result.Documents[0].Url);
        Assert.Contains(result.Trace, line => line.Contains("reason=content_mirror", StringComparison.OrdinalIgnoreCase));
    }
}

