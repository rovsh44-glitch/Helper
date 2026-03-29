namespace Helper.Runtime.WebResearch.Extraction;

internal sealed class PlainTextWebSourceTypeExtractor : IWebSourceTypeExtractor
{
    private readonly IWebPageContentExtractor _contentExtractor;
    private readonly WebSourceTypeExtractionQualityPolicy _qualityPolicy;

    public PlainTextWebSourceTypeExtractor(
        IWebPageContentExtractor contentExtractor,
        WebSourceTypeExtractionQualityPolicy qualityPolicy)
    {
        _contentExtractor = contentExtractor;
        _qualityPolicy = qualityPolicy;
    }

    public string Id => "plain_text";

    public bool CanHandle(WebSourceTypeExtractionRequest request)
    {
        return request.DecodedContent is not null &&
               request.NormalizedContentType.Equals("text/plain", StringComparison.OrdinalIgnoreCase);
    }

    public Task<WebSourceTypeExtractionResult> ExtractAsync(WebSourceTypeExtractionRequest request, CancellationToken ct = default)
    {
        var trace = new List<string>
        {
            $"web_extract.route kind={request.SourceType.Kind} extractor={Id}"
        };
        var extractedPage = _contentExtractor.Extract(
            request.RequestedUri,
            request.ResolvedUri,
            request.ContentType,
            request.DecodedContent!);
        if (extractedPage is null)
        {
            trace.Add("web_extract.failed reason=content_extraction_failed");
            return Task.FromResult(new WebSourceTypeExtractionResult(
                Id,
                Handled: true,
                Success: false,
                Outcome: "extraction_failed",
                ExtractedPage: null,
                Trace: trace));
        }

        var qualityDecision = _qualityPolicy.Evaluate(request, extractedPage);
        trace.AddRange(qualityDecision.Trace);
        if (!qualityDecision.Allowed)
        {
            return Task.FromResult(new WebSourceTypeExtractionResult(
                Id,
                Handled: true,
                Success: false,
                Outcome: qualityDecision.ReasonCode,
                ExtractedPage: null,
                Trace: trace));
        }

        trace.Add($"web_extract.completed extractor={Id} passages={extractedPage.Passages.Count}");
        return Task.FromResult(new WebSourceTypeExtractionResult(
            Id,
            Handled: true,
            Success: true,
            Outcome: "extracted",
            ExtractedPage: extractedPage,
            Trace: trace));
    }
}

