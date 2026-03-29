namespace Helper.Runtime.WebResearch.Extraction;

internal sealed record WebSourceTypeExtractionRequest(
    Uri RequestedUri,
    Uri ResolvedUri,
    string? ContentType,
    string NormalizedContentType,
    byte[] ContentBytes,
    string? DecodedContent,
    WebSourceTypeProfile SourceType);

internal sealed record WebSourceTypeExtractionResult(
    string ExtractorId,
    bool Handled,
    bool Success,
    string Outcome,
    ExtractedWebPage? ExtractedPage,
    IReadOnlyList<string> Trace);

internal interface IWebSourceTypeExtractor
{
    string Id { get; }

    bool CanHandle(WebSourceTypeExtractionRequest request);

    Task<WebSourceTypeExtractionResult> ExtractAsync(WebSourceTypeExtractionRequest request, CancellationToken ct = default);
}

