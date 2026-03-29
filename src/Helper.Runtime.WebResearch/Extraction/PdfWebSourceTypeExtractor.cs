namespace Helper.Runtime.WebResearch.Extraction;

internal sealed class PdfWebSourceTypeExtractor : IWebSourceTypeExtractor
{
    private readonly IRemoteDocumentExtractor _remoteDocumentExtractor;

    public PdfWebSourceTypeExtractor(IRemoteDocumentExtractor remoteDocumentExtractor)
    {
        _remoteDocumentExtractor = remoteDocumentExtractor;
    }

    public string Id => "remote_document_pdf";

    public bool CanHandle(WebSourceTypeExtractionRequest request)
    {
        return request.SourceType.Kind.Equals("document_pdf", StringComparison.Ordinal) ||
               request.ResolvedUri.AbsolutePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ||
               request.RequestedUri.AbsolutePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ||
               WebSourceTypeExtractionSupport.LooksLikePdf(request.NormalizedContentType, request.ContentBytes);
    }

    public async Task<WebSourceTypeExtractionResult> ExtractAsync(WebSourceTypeExtractionRequest request, CancellationToken ct = default)
    {
        var result = await _remoteDocumentExtractor.ExtractAsync(
            request.RequestedUri,
            request.ResolvedUri,
            request.ContentType,
            request.ContentBytes,
            ct).ConfigureAwait(false);
        var trace = new List<string>
        {
            $"web_extract.route kind={request.SourceType.Kind} extractor={Id}"
        };
        trace.AddRange(result.Trace);
        return new WebSourceTypeExtractionResult(
            Id,
            Handled: result.Handled,
            Success: result.Success,
            Outcome: result.Outcome,
            ExtractedPage: result.ExtractedPage,
            Trace: trace);
    }
}

