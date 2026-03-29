using Helper.Runtime.Core;
using Helper.Runtime.Knowledge;
using Helper.Runtime.WebResearch;
using Helper.Runtime.WebResearch.Extraction;

namespace Helper.Api.Hosting;

internal sealed class PdfRemoteDocumentExtractor : IRemoteDocumentExtractor
{
    private readonly Func<string, CancellationToken, Task<DocumentParseResult>> _parseAsync;

    public PdfRemoteDocumentExtractor()
        : this(static (filePath, ct) => new StructuredPdfParser().ParseStructuredAsync(filePath, onProgress: null, ct))
    {
    }

    internal PdfRemoteDocumentExtractor(Func<string, CancellationToken, Task<DocumentParseResult>> parseAsync)
    {
        _parseAsync = parseAsync;
    }

    public async Task<RemoteDocumentExtractionResult> ExtractAsync(
        Uri requestedUri,
        Uri resolvedUri,
        string? contentType,
        byte[] contentBytes,
        CancellationToken ct = default)
    {
        if (!LooksLikePdf(requestedUri, resolvedUri, contentType, contentBytes))
        {
            return new RemoteDocumentExtractionResult(
                Handled: false,
                Success: false,
                Outcome: "not_supported",
                ExtractedPage: null,
                Trace: new[]
                {
                    $"web_document_extract.skipped reason=unsupported requested={requestedUri} resolved={resolvedUri} content_type={contentType ?? "unknown"}"
                });
        }

        var tempRoot = Path.Combine(Path.GetTempPath(), "HELPER", "webresearch_docs", Guid.NewGuid().ToString("N"));
        var tempFilePath = Path.Combine(tempRoot, ResolveFileName(resolvedUri));
        try
        {
            Directory.CreateDirectory(tempRoot);
            await File.WriteAllBytesAsync(tempFilePath, contentBytes, ct).ConfigureAwait(false);
            var parsed = await _parseAsync(tempFilePath, ct).ConfigureAwait(false);
            var body = BuildBody(parsed);
            if (string.IsNullOrWhiteSpace(body))
            {
                return new RemoteDocumentExtractionResult(
                    Handled: true,
                    Success: false,
                    Outcome: "document_extraction_failed",
                    ExtractedPage: null,
                    Trace: BuildTrace(requestedUri, resolvedUri, parsed, "empty_document_body"));
            }

            var passages = BuildPassages(parsed, body);
            if (passages.Count == 0)
            {
                return new RemoteDocumentExtractionResult(
                    Handled: true,
                    Success: false,
                    Outcome: "document_extraction_failed",
                    ExtractedPage: null,
                    Trace: BuildTrace(requestedUri, resolvedUri, parsed, "empty_document_passages"));
            }

            var publishedAt = ResolvePublishedAt(parsed);
            var title = ResolveTitle(parsed, resolvedUri);
            var page = new ExtractedWebPage(
                RequestedUrl: requestedUri.AbsoluteUri,
                ResolvedUrl: resolvedUri.AbsoluteUri,
                CanonicalUrl: resolvedUri.AbsoluteUri,
                Title: title,
                PublishedAt: publishedAt,
                Body: body,
                Passages: passages,
                ContentType: "application/pdf");

            var trace = BuildTrace(requestedUri, resolvedUri, parsed, "pdf_parsed").ToList();
            trace.Add($"web_document_extract.passages={passages.Count}");
            return new RemoteDocumentExtractionResult(
                Handled: true,
                Success: true,
                Outcome: "document_extracted_pdf",
                ExtractedPage: page,
                Trace: trace);
        }
        catch (Exception ex)
        {
            return new RemoteDocumentExtractionResult(
                Handled: true,
                Success: false,
                Outcome: "document_extraction_failed",
                ExtractedPage: null,
                Trace: new[]
                {
                    $"web_document_extract.failed kind=pdf requested={requestedUri} resolved={resolvedUri} reason={ex.Message}"
                });
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    private static bool LooksLikePdf(Uri requestedUri, Uri resolvedUri, string? contentType, byte[] contentBytes)
    {
        if (!string.IsNullOrWhiteSpace(contentType) &&
            contentType.Contains("application/pdf", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (requestedUri.AbsolutePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ||
            resolvedUri.AbsolutePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return contentBytes.Length >= 5 &&
               contentBytes[0] == (byte)'%' &&
               contentBytes[1] == (byte)'P' &&
               contentBytes[2] == (byte)'D' &&
               contentBytes[3] == (byte)'F' &&
               contentBytes[4] == (byte)'-';
    }

    private static string ResolveFileName(Uri resolvedUri)
    {
        var fileName = Path.GetFileName(resolvedUri.AbsolutePath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return "document.pdf";
        }

        return fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
            ? fileName
            : fileName + ".pdf";
    }

    private static string ResolveTitle(DocumentParseResult parsed, Uri resolvedUri)
    {
        if (!string.IsNullOrWhiteSpace(parsed.Title))
        {
            return parsed.Title.Trim();
        }

        var fileName = Path.GetFileNameWithoutExtension(resolvedUri.AbsolutePath);
        return string.IsNullOrWhiteSpace(fileName) ? resolvedUri.Host : fileName;
    }

    private static string? ResolvePublishedAt(DocumentParseResult parsed)
    {
        if (!string.IsNullOrWhiteSpace(parsed.PublishedYear))
        {
            return parsed.PublishedYear.Trim();
        }

        if (parsed.Metadata is { Count: > 0 })
        {
            foreach (var key in new[] { "published_at", "published", "publication_year", "year" })
            {
                if (parsed.Metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }
        }

        return null;
    }

    private static string BuildBody(DocumentParseResult parsed)
    {
        var sections = parsed.Blocks
            .Select(static block => block.Text?.Trim())
            .Where(static text => !string.IsNullOrWhiteSpace(text))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (sections.Count == 0)
        {
            sections = parsed.Pages
                .Select(static page => page.RawText?.Trim())
                .Where(static text => !string.IsNullOrWhiteSpace(text))
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        return string.Join("\n\n", sections);
    }

    private static IReadOnlyList<ExtractedWebPassage> BuildPassages(DocumentParseResult parsed, string body)
    {
        var sections = parsed.Blocks
            .Select(static block => block.Text?.Trim())
            .Where(static text => !string.IsNullOrWhiteSpace(text))
            .Where(static text => text!.Length >= 64)
            .Take(6)
            .ToArray();

        if (sections.Length < 2)
        {
            sections = parsed.Blocks
                .Select(static block => block.Text?.Trim())
                .Where(static text => !string.IsNullOrWhiteSpace(text))
                .Where(static text => text!.Length >= 24)
                .Distinct(StringComparer.Ordinal)
                .Take(6)
                .ToArray();
        }

        if (sections.Length == 0)
        {
            sections = body
                .Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(static paragraph => paragraph.Length >= 64)
                .Take(4)
                .ToArray();
        }

        return sections
            .Select(static (text, index) => new ExtractedWebPassage(index + 1, text!))
            .ToArray();
    }

    private static IReadOnlyList<string> BuildTrace(Uri requestedUri, Uri resolvedUri, DocumentParseResult parsed, string outcome)
    {
        var trace = new List<string>
        {
            $"web_document_extract.outcome={outcome} kind=pdf requested={requestedUri} resolved={resolvedUri}",
            $"web_document_extract.pages={parsed.Pages.Count}",
            $"web_document_extract.blocks={parsed.Blocks.Count}"
        };

        if (parsed.Warnings is { Count: > 0 })
        {
            trace.Add($"web_document_extract.warnings={parsed.Warnings.Count}");
        }

        return trace;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Best effort cleanup only.
        }
    }
}

