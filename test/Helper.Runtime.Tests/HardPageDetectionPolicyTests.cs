using Helper.Runtime.WebResearch;
using Helper.Runtime.WebResearch.Rendering;

namespace Helper.Runtime.Tests;

public sealed class HardPageDetectionPolicyTests
{
    [Fact]
    public void Evaluate_ReturnsHardPage_ForJsShellWithoutUsableContent()
    {
        var policy = new HardPageDetectionPolicy();

        var decision = policy.Evaluate(
            new Uri("https://example.org/app"),
            new Uri("https://example.org/app"),
            "text/html",
            "<html><head><script src=\"/_next/static/chunk.js\"></script><script>window.__NUXT__={}</script></head><body><div id=\"__next\">Loading...</div></body></html>",
            extractedPage: null);

        Assert.True(decision.IsHardPage);
        Assert.Contains("js_shell_marker", decision.Signals);
        Assert.Contains("web_page_render.detected=yes", decision.Trace[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Evaluate_RejectsHardPage_WhenSubstantiveHttpExtractionAlreadyExists()
    {
        var policy = new HardPageDetectionPolicy();
        var extractedPage = new ExtractedWebPage(
            "https://example.org/article",
            "https://example.org/article",
            "https://example.org/article",
            "Article",
            "2026-03-21",
            "This article already contains a substantial extracted body with multiple paragraphs and should not be escalated to browser render fallback.\n\nThe second paragraph confirms there is enough text for ordinary grounding and synthesis.",
            new[]
            {
                new ExtractedWebPassage(1, "This article already contains a substantial extracted body with multiple paragraphs and should not be escalated to browser render fallback."),
                new ExtractedWebPassage(2, "The second paragraph confirms there is enough text for ordinary grounding and synthesis.")
            },
            "text/html");

        var decision = policy.Evaluate(
            new Uri("https://example.org/article"),
            new Uri("https://example.org/article"),
            "text/html",
            "<html><body><article><p>Substantive content.</p></article></body></html>",
            extractedPage);

        Assert.False(decision.IsHardPage);
        Assert.Equal("sufficient_http_content", decision.Reason);
    }
}

