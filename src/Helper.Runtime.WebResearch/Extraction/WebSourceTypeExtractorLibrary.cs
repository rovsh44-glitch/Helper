namespace Helper.Runtime.WebResearch.Extraction;

internal sealed class WebSourceTypeExtractorLibrary
{
    private readonly IReadOnlyList<IWebSourceTypeExtractor> _extractors;

    public WebSourceTypeExtractorLibrary(
        IRemoteDocumentExtractor remoteDocumentExtractor,
        IWebPageContentExtractor contentExtractor)
    {
        var qualityPolicy = new WebSourceTypeExtractionQualityPolicy();
        _extractors = new IWebSourceTypeExtractor[]
        {
            new PdfWebSourceTypeExtractor(remoteDocumentExtractor),
            new PlainTextWebSourceTypeExtractor(contentExtractor, qualityPolicy),
            new InteractiveShellHtmlWebSourceTypeExtractor(contentExtractor, qualityPolicy),
            new DocumentLikeHtmlWebSourceTypeExtractor(contentExtractor, qualityPolicy),
            new GeneralHtmlWebSourceTypeExtractor(contentExtractor, qualityPolicy)
        };
    }

    public async Task<WebSourceTypeExtractionResult> ExtractAsync(
        WebSourceTypeExtractionRequest request,
        CancellationToken ct = default)
    {
        foreach (var extractor in _extractors)
        {
            if (!extractor.CanHandle(request))
            {
                continue;
            }

            return await extractor.ExtractAsync(request, ct).ConfigureAwait(false);
        }

        return new WebSourceTypeExtractionResult(
            ExtractorId: "none",
            Handled: false,
            Success: false,
            Outcome: "not_supported",
            ExtractedPage: null,
            Trace: new[]
            {
                $"web_extract.skipped kind={request.SourceType.Kind} reason=no_matching_extractor"
            });
    }
}

