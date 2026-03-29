using Helper.Runtime.WebResearch.Extraction;

namespace Helper.Runtime.Tests;

public sealed class WebPageContentExtractorTests
{
    [Fact]
    public void Extract_ParsesCanonicalTitleDateBodyAndPassages_FromHtml()
    {
        var extractor = new WebPageContentExtractor();
        var requested = new Uri("https://example.org/news/post?utm_source=test");
        var resolved = new Uri("https://example.org/news/post?utm_source=test");
        const string html = """
            <html>
            <head>
              <title>Ignored title</title>
              <meta property="og:title" content="Observed Article Title" />
              <meta property="article:published_time" content="2026-03-20T09:10:00Z" />
              <link rel="canonical" href="/news/post" />
            </head>
            <body>
              <article>
                <p>The first paragraph explains the release scope and includes enough text to remain useful after normalization.</p>
                <p>The second paragraph adds implementation details and deployment constraints that should appear in the extracted passages.</p>
              </article>
            </body>
            </html>
            """;

        var result = extractor.Extract(requested, resolved, "text/html; charset=utf-8", html);

        Assert.NotNull(result);
        Assert.Equal("https://example.org/news/post", result!.CanonicalUrl);
        Assert.Equal("Observed Article Title", result.Title);
        Assert.Equal("2026-03-20", result.PublishedAt);
        Assert.Contains("The first paragraph explains the release scope", result.Body, StringComparison.Ordinal);
        Assert.Contains("The second paragraph adds implementation details", result.Body, StringComparison.Ordinal);
        Assert.True(result.Passages.Count >= 1);
        Assert.Contains("deployment constraints", result.Passages[0].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Extract_BuildsPassages_FromPlainText()
    {
        var extractor = new WebPageContentExtractor();
        var requested = new Uri("https://example.org/plain");
        var resolved = new Uri("https://example.org/plain");
        const string content = """
            Plain text analysis title

            This plain text page contains a sufficiently long explanation of the observed behavior and should be retained as extracted body text.

            Another paragraph expands on edge cases and operational caveats so the passage builder has enough material to segment.
            """;

        var result = extractor.Extract(requested, resolved, "text/plain", content);

        Assert.NotNull(result);
        Assert.Equal("https://example.org/plain", result!.CanonicalUrl);
        Assert.Equal("Plain text analysis title", result.Title);
        Assert.Contains("sufficiently long explanation", result.Body, StringComparison.Ordinal);
        Assert.NotEmpty(result.Passages);
    }

    [Fact]
    public void Extract_PreservesDenseClinicalGuidanceParagraphs()
    {
        var extractor = new WebPageContentExtractor();
        var requested = new Uri("https://cr.minzdrav.gov.ru/recommendations/migraine");
        var resolved = new Uri("https://cr.minzdrav.gov.ru/recommendations/migraine");
        const string html = """
            <html>
            <head><title>Клинические рекомендации</title></head>
            <body>
              <article>
                <p>Показания: частые приступы.</p>
                <p>Критерии начала профилактики: четыре и более дней с головной болью в месяц.</p>
                <p>Контроль эффективности: снижение частоты приступов и переносимость терапии.</p>
              </article>
            </body>
            </html>
            """;

        var result = extractor.Extract(requested, resolved, "text/html; charset=utf-8", html);

        Assert.NotNull(result);
        Assert.Contains("Критерии начала профилактики", result!.Body, StringComparison.Ordinal);
        Assert.NotEmpty(result.Passages);
    }
}

