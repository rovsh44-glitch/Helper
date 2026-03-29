using Helper.Runtime.Core;
using Helper.Runtime.Infrastructure;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using DocumentFormatType = Helper.Runtime.Core.DocumentFormat;

namespace Helper.Runtime.Knowledge;

public sealed partial class StructuredPdfParser : StructuredDocumentParserBase
{
    private readonly AILink? _ai;

    public StructuredPdfParser(AILink? ai = null)
    {
        _ai = ai;
    }

    public override string ParserVersion => "pdf_v2";

    public override bool CanParse(string extension) => string.Equals(extension, ".pdf", StringComparison.OrdinalIgnoreCase);

    public override async Task<DocumentParseResult> ParseStructuredAsync(string filePath, Func<double, Task>? onProgress = null, CancellationToken ct = default)
    {
        var pages = new List<DocumentPage>();
        var blocks = new List<DocumentBlock>();
        var warnings = new List<string>();
        var readingOrder = 0;

        try
        {
            using var pdf = PdfDocument.Open(filePath);
            var totalPages = pdf.NumberOfPages;
            for (var pageNumber = 1; pageNumber <= totalPages; pageNumber++)
            {
                ct.ThrowIfCancellationRequested();
                Page? page = null;
                try
                {
                    page = pdf.GetPage(pageNumber);
                }
                catch (Exception ex)
                {
                    warnings.Add($"page_{pageNumber}_pdfpig_page_failed:{ex.Message}");
                    await ReportParseProgressAsync(onProgress, pageNumber, totalPages);
                    continue;
                }

                var pageText = await ExtractPageTextAsync(filePath, pageNumber, page, warnings, ct);
                var pageBlocks = StructuredParserUtilities.SplitPlainTextIntoBlocks(pageText, pageNumber, ref readingOrder);
                pages.Add(new DocumentPage(pageNumber, pageText, pageBlocks, $"page-{pageNumber}"));
                blocks.AddRange(pageBlocks);
                await ReportParseProgressAsync(onProgress, pageNumber, totalPages);
            }
        }
        catch (Exception ex) when (_ai is not null && CanUseGhostscriptVisionFallback())
        {
            warnings.Add($"pdfpig_failed:{ex.Message}");
            return await ParseWithVisionOnlyAsync(filePath, warnings, onProgress, ct);
        }
        catch (Exception ex)
        {
            warnings.Add($"pdfpig_failed:{ex.Message}");
        }

        await ReportParseProgressAsync(onProgress, 100, 100);

        return StructuredParserUtilities.BuildDocument(
            filePath,
            DocumentFormatType.Pdf,
            ParserVersion,
            Path.GetFileName(filePath),
            pages,
            blocks,
            warnings);
    }

    private async Task<string> ExtractPageTextAsync(
        string filePath,
        int pageNumber,
        Page page,
        List<string> warnings,
        CancellationToken ct)
    {
        string pageText;
        try
        {
            pageText = ExtractTextFromPage(page);
        }
        catch (Exception ex)
        {
            warnings.Add($"page_{pageNumber}_pdfpig_text_failed:{ex.Message}");
            pageText = string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(pageText) && StructuredParserUtilities.LooksLikeDegradedTextExtraction(pageText))
        {
            warnings.Add($"page_{pageNumber}_pdf_text_degraded");
            if (CanUseGhostscriptVisionFallback())
            {
                try
                {
                    var visionText = await ExtractWithGhostscriptVisionAsync(filePath, pageNumber, ct);
                    if (StructuredParserUtilities.IsBetterTextExtraction(visionText, pageText))
                    {
                        pageText = visionText;
                        warnings.Add($"page_{pageNumber}_ghostscript_vision_replaced_degraded_pdf_text");
                    }
                }
                catch (Exception ex)
                {
                    warnings.Add($"page_{pageNumber}_ghostscript_vision_replace_failed:{ex.Message}");
                }
            }
            else
            {
                warnings.Add($"page_{pageNumber}_degraded_text_fallback_skipped:ghostscript_or_ai_unavailable");
            }
        }

        if (string.IsNullOrWhiteSpace(pageText) && CanUseAiVision())
        {
            try
            {
                pageText = await ExtractWithEmbeddedImageVisionAsync(page, ct);
            }
            catch (Exception ex)
            {
                warnings.Add($"page_{pageNumber}_embedded_image_vision_failed:{ex.Message}");
            }
        }

        if (string.IsNullOrWhiteSpace(pageText) && CanUseGhostscriptVisionFallback())
        {
            try
            {
                pageText = await ExtractWithGhostscriptVisionAsync(filePath, pageNumber, ct);
            }
            catch (Exception ex)
            {
                warnings.Add($"page_{pageNumber}_ghostscript_vision_failed:{ex.Message}");
            }
        }
        else if (string.IsNullOrWhiteSpace(pageText))
        {
            warnings.Add($"page_{pageNumber}_vision_fallback_skipped:pdf_text_empty_or_backend_unavailable");
        }

        return pageText;
    }

    private async Task<DocumentParseResult> ParseWithVisionOnlyAsync(string filePath, List<string> warnings, Func<double, Task>? onProgress, CancellationToken ct)
    {
        if (!CanUseGhostscriptVisionFallback())
        {
            throw new InvalidOperationException("PDF vision fallback is unavailable because Ghostscript was not found.");
        }

        var pages = new List<DocumentPage>();
        var blocks = new List<DocumentBlock>();
        var readingOrder = 0;
        var pageCount = ResolveVisionOnlyPageCount(filePath);

        for (var pageNumber = 1; pageNumber <= pageCount; pageNumber++)
        {
            ct.ThrowIfCancellationRequested();
            var pageText = string.Empty;
            try
            {
                pageText = await ExtractWithGhostscriptVisionAsync(filePath, pageNumber, ct);
            }
            catch (Exception ex)
            {
                warnings.Add($"page_{pageNumber}_vision_only_failed:{ex.Message}");
            }

            var pageBlocks = StructuredParserUtilities.SplitPlainTextIntoBlocks(pageText, pageNumber, ref readingOrder);
            pages.Add(new DocumentPage(pageNumber, pageText, pageBlocks, $"page-{pageNumber}"));
            blocks.AddRange(pageBlocks);
            await ReportParseProgressAsync(onProgress, pageNumber, pageCount);
        }

        await ReportParseProgressAsync(onProgress, 100, 100);

        return StructuredParserUtilities.BuildDocument(
            filePath,
            DocumentFormatType.Pdf,
            ParserVersion,
            Path.GetFileName(filePath),
            pages,
            blocks,
            warnings);
    }

    private static Task ReportParseProgressAsync(Func<double, Task>? onProgress, int processedPages, int totalPages)
    {
        if (onProgress is null)
        {
            return Task.CompletedTask;
        }

        var safeTotal = Math.Max(totalPages, 1);
        var progress = Math.Clamp((double)processedPages / safeTotal * 100d, 0d, 100d);
        return onProgress(progress);
    }

    private static string ExtractTextFromPage(Page page)
    {
        var candidates = new List<string>(capacity: 3);

        TryAddTextCandidate(candidates, page.Text);

        try
        {
            TryAddTextCandidate(candidates, string.Join(" ", page.GetWords().Select(static word => word.Text)));
        }
        catch
        {
        }

        try
        {
            TryAddTextCandidate(candidates, string.Concat(page.Letters.Select(static letter => letter.Value)));
        }
        catch
        {
        }

        return StructuredParserUtilities.SelectBestTextExtraction(candidates.ToArray());
    }

    private static void TryAddTextCandidate(ICollection<string> candidates, string? rawText)
    {
        var normalized = StructuredParserUtilities.NormalizeWhitespace(rawText);
        if (!string.IsNullOrWhiteSpace(normalized) && !StructuredParserUtilities.LooksLikeLowValueExtraction(normalized))
        {
            candidates.Add(normalized);
        }
    }

    private bool CanUseAiVision()
        => _ai is not null;

    private bool CanUseGhostscriptVisionFallback()
        => _ai is not null && VisionFallbackAvailable.Value;

    private string ResolveVisionModel()
        => _ai?.GetBestModel("vision") ?? DefaultVisionModel;
}

