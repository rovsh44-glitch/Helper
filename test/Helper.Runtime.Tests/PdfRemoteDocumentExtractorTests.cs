using Helper.Api.Hosting;
using Helper.Runtime.Core;

namespace Helper.Runtime.Tests;

public sealed class PdfRemoteDocumentExtractorTests
{
    [Fact]
    public async Task ExtractAsync_MapsParsedPdf_ToExtractedWebPage()
    {
        var extractor = new PdfRemoteDocumentExtractor(static (filePath, ct) =>
            Task.FromResult(new DocumentParseResult(
                DocumentId: "doc-1",
                SourcePath: filePath,
                Format: Helper.Runtime.Core.DocumentFormat.Pdf,
                Title: "Attention Residuals",
                ParserVersion: "pdf_v2",
                Pages: new[]
                {
                    new DocumentPage(1, "Attention residuals replace fixed residual accumulation.", Array.Empty<DocumentBlock>())
                },
                Blocks: new[]
                {
                    new DocumentBlock("b1", DocumentBlockType.Paragraph, "Attention residuals replace fixed residual accumulation.", 1, 1),
                    new DocumentBlock("b2", DocumentBlockType.Paragraph, "The paper reports scaling improvements and a Block AttnRes variant.", 2, 1)
                },
                PublishedYear: "2026")));

        var result = await extractor.ExtractAsync(
            new Uri("https://example.org/paper.pdf"),
            new Uri("https://example.org/paper.pdf"),
            "application/pdf",
            "%PDF-1.7 fake".Select(static c => (byte)c).ToArray(),
            CancellationToken.None);

        Assert.True(result.Handled);
        Assert.True(result.Success);
        Assert.Equal("document_extracted_pdf", result.Outcome);
        Assert.NotNull(result.ExtractedPage);
        Assert.Equal("Attention Residuals", result.ExtractedPage!.Title);
        Assert.Equal("2026", result.ExtractedPage.PublishedAt);
        Assert.Equal("application/pdf", result.ExtractedPage.ContentType);
        Assert.Equal(2, result.ExtractedPage.Passages.Count);
    }
}

