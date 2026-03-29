using Helper.Runtime.WebResearch;
using Helper.Runtime.WebResearch.Extraction;

namespace Helper.Runtime.Tests;

public sealed class WebSourceTypeExtractorLibraryTests
{
    [Fact]
    public async Task ExtractAsync_UsesDocumentLikeHtmlExtractor_ForClinicalGuidance()
    {
        var library = new WebSourceTypeExtractorLibrary(NoopRemoteDocumentExtractor.Instance, new WebPageContentExtractor());
        var request = CreateRequest(
            "https://cr.minzdrav.gov.ru/recommendations/migraine",
            "text/html; charset=utf-8",
            """
            <html>
            <head><title>Клинические рекомендации</title></head>
            <body>
              <article>
                <p>Критерии начала профилактики: четыре и более дней с головной болью в месяц.</p>
                <p>Контроль эффективности: снижение частоты приступов и переносимость терапии.</p>
              </article>
            </body>
            </html>
            """);

        var result = await library.ExtractAsync(request, CancellationToken.None);

        Assert.True(result.Handled);
        Assert.True(result.Success);
        Assert.Equal("document_like_html", result.ExtractorId);
        Assert.NotNull(result.ExtractedPage);
        Assert.Contains(result.Trace, line => line.Contains("web_extract.route kind=clinical_guidance extractor=document_like_html", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExtractAsync_UsesGeneralHtmlExtractor_ForGeneralArticle()
    {
        var library = new WebSourceTypeExtractorLibrary(NoopRemoteDocumentExtractor.Instance, new WebPageContentExtractor());
        var request = CreateRequest(
            "https://example.org/news/article",
            "text/html; charset=utf-8",
            """
            <html>
            <head><title>General article</title></head>
            <body>
              <article>
                <p>This article provides a substantial explanation of an observed release and should be handled as a general article extraction path.</p>
                <p>The follow-up paragraph adds enough detail for passage extraction and downstream grounding.</p>
              </article>
            </body>
            </html>
            """);

        var result = await library.ExtractAsync(request, CancellationToken.None);

        Assert.True(result.Handled);
        Assert.True(result.Success);
        Assert.Equal("general_html", result.ExtractorId);
        Assert.NotNull(result.ExtractedPage);
    }

    [Fact]
    public async Task ExtractAsync_UsesDocumentLikeHtmlExtractor_ForAcademicPublisherArticle()
    {
        var library = new WebSourceTypeExtractorLibrary(NoopRemoteDocumentExtractor.Instance, new WebPageContentExtractor());
        var request = CreateRequest(
            "https://link.springer.com/article/10.1007/s10103-025-04318-w",
            "text/html; charset=utf-8",
            """
            <html>
            <head>
              <title>A systematic review on whole-body photobiomodulation for exercise performance and recovery</title>
            </head>
            <body>
              <article>
                <p>Abstract: Whole-body photobiomodulation has been evaluated for exercise performance and recovery outcomes.</p>
                <p>The review reports heterogeneous but potentially meaningful recovery effects across protocols.</p>
              </article>
            </body>
            </html>
            """);

        var result = await library.ExtractAsync(request, CancellationToken.None);

        Assert.True(result.Handled);
        Assert.True(result.Success);
        Assert.Equal("document_like_html", result.ExtractorId);
        Assert.NotNull(result.ExtractedPage);
        Assert.Contains(result.Trace, line => line.Contains("web_extract.route kind=academic_paper extractor=document_like_html", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExtractAsync_UsesRemotePdfExtractor_ForPdf()
    {
        var library = new WebSourceTypeExtractorLibrary(new StubRemoteDocumentExtractor(), new WebPageContentExtractor());
        var request = new WebSourceTypeExtractionRequest(
            new Uri("https://example.org/paper.pdf"),
            new Uri("https://example.org/paper.pdf"),
            "application/pdf",
            "application/pdf",
            System.Text.Encoding.ASCII.GetBytes("%PDF-1.7"),
            null,
            WebSourceTypeClassifier.Classify(
                new Uri("https://example.org/paper.pdf"),
                new Uri("https://example.org/paper.pdf"),
                "application/pdf"));

        var result = await library.ExtractAsync(request, CancellationToken.None);

        Assert.True(result.Handled);
        Assert.True(result.Success);
        Assert.Equal("remote_document_pdf", result.ExtractorId);
        Assert.NotNull(result.ExtractedPage);
    }

    private static WebSourceTypeExtractionRequest CreateRequest(string url, string contentType, string content)
    {
        var uri = new Uri(url);
        var normalizedContentType = WebSourceTypeExtractionSupport.NormalizeMediaType(contentType);
        return new WebSourceTypeExtractionRequest(
            uri,
            uri,
            contentType,
            normalizedContentType,
            System.Text.Encoding.UTF8.GetBytes(content),
            content,
            WebSourceTypeClassifier.Classify(uri, uri, normalizedContentType));
    }

    private sealed class StubRemoteDocumentExtractor : IRemoteDocumentExtractor
    {
        public Task<RemoteDocumentExtractionResult> ExtractAsync(
            Uri requestedUri,
            Uri resolvedUri,
            string? contentType,
            byte[] contentBytes,
            CancellationToken ct = default)
        {
            return Task.FromResult(new RemoteDocumentExtractionResult(
                Handled: true,
                Success: true,
                Outcome: "document_extracted_pdf",
                ExtractedPage: new ExtractedWebPage(
                    requestedUri.AbsoluteUri,
                    resolvedUri.AbsoluteUri,
                    resolvedUri.AbsoluteUri,
                    "Stub paper",
                    "2026",
                    "This PDF body is extracted by the remote document path.",
                    new[] { new ExtractedWebPassage(1, "This PDF body is extracted by the remote document path.") },
                    "application/pdf"),
                Trace: new[] { "web_document_extract.stub kind=pdf" }));
        }
    }
}

