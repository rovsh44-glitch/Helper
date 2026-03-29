using Helper.Runtime.WebResearch;
using Helper.Runtime.WebResearch.Normalization;

namespace Helper.Runtime.Tests;

public sealed class CanonicalUrlResolverTests
{
    [Fact]
    public void Resolve_RemovesTrackingAndTrailingSlash_FromDocumentUrl()
    {
        var resolver = new CanonicalUrlResolver();

        var resolution = resolver.Resolve(new WebSearchDocument(
            "https://www.example.org/news/post/?utm_source=feed&ref=home&id=42#section",
            "Post",
            "Snippet"));

        Assert.Equal("https://example.org/news/post?id=42", resolution.CanonicalUrl);
        Assert.Contains("host_www_removed", resolution.Reasons);
        Assert.Contains("trailing_slash_removed", resolution.Reasons);
    }

    [Fact]
    public void Resolve_PrefersExtractedPageCanonical_WhenAvailable()
    {
        var resolver = new CanonicalUrlResolver();
        var page = new ExtractedWebPage(
            RequestedUrl: "https://mirror.example.com/post?utm_source=mirror",
            ResolvedUrl: "https://mirror.example.com/post?utm_source=mirror",
            CanonicalUrl: "https://publisher.example.com/post/",
            Title: "Post",
            PublishedAt: "2026-03-21",
            Body: "Evidence body.",
            Passages: new[]
            {
                new ExtractedWebPassage(1, "Evidence body.")
            },
            ContentType: "text/html");

        var resolution = resolver.Resolve(new WebSearchDocument(
            "https://mirror.example.com/post?utm_source=mirror",
            "Post",
            "Snippet",
            ExtractedPage: page));

        Assert.Equal("https://publisher.example.com/post", resolution.CanonicalUrl);
        Assert.True(resolution.UsedExtractedCanonical);
        Assert.Contains("page_canonical", resolution.Reasons);
    }
}

