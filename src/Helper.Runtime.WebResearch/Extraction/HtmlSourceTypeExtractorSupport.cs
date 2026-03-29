namespace Helper.Runtime.WebResearch.Extraction;

internal static class HtmlSourceTypeExtractorSupport
{
    public static Task<WebSourceTypeExtractionResult> ExtractAsync(
        string extractorId,
        IWebPageContentExtractor contentExtractor,
        WebSourceTypeExtractionQualityPolicy qualityPolicy,
        WebSourceTypeExtractionRequest request)
    {
        var trace = new List<string>
        {
            $"web_extract.route kind={request.SourceType.Kind} extractor={extractorId}"
        };
        var extractedPage = contentExtractor.Extract(
            request.RequestedUri,
            request.ResolvedUri,
            request.ContentType,
            request.DecodedContent!);
        if (extractedPage is null)
        {
            trace.Add("web_extract.failed reason=content_extraction_failed");
            return Task.FromResult(new WebSourceTypeExtractionResult(
                extractorId,
                Handled: true,
                Success: false,
                Outcome: "extraction_failed",
                ExtractedPage: null,
                Trace: trace));
        }

        var qualityDecision = qualityPolicy.Evaluate(request, extractedPage);
        trace.AddRange(qualityDecision.Trace);
        if (!qualityDecision.Allowed)
        {
            return Task.FromResult(new WebSourceTypeExtractionResult(
                extractorId,
                Handled: true,
                Success: false,
                Outcome: qualityDecision.ReasonCode,
                ExtractedPage: null,
                Trace: trace));
        }

        trace.Add($"web_extract.completed extractor={extractorId} passages={extractedPage.Passages.Count}");
        return Task.FromResult(new WebSourceTypeExtractionResult(
            extractorId,
            Handled: true,
            Success: true,
            Outcome: "extracted",
            ExtractedPage: extractedPage,
            Trace: trace));
    }
}

