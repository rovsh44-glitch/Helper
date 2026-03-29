namespace Helper.Runtime.WebResearch.Extraction;

internal sealed class GeneralHtmlWebSourceTypeExtractor : IWebSourceTypeExtractor
{
    private readonly IWebPageContentExtractor _contentExtractor;
    private readonly WebSourceTypeExtractionQualityPolicy _qualityPolicy;

    public GeneralHtmlWebSourceTypeExtractor(
        IWebPageContentExtractor contentExtractor,
        WebSourceTypeExtractionQualityPolicy qualityPolicy)
    {
        _contentExtractor = contentExtractor;
        _qualityPolicy = qualityPolicy;
    }

    public string Id => "general_html";

    public bool CanHandle(WebSourceTypeExtractionRequest request)
    {
        return request.DecodedContent is not null;
    }

    public Task<WebSourceTypeExtractionResult> ExtractAsync(WebSourceTypeExtractionRequest request, CancellationToken ct = default)
    {
        return HtmlSourceTypeExtractorSupport.ExtractAsync(Id, _contentExtractor, _qualityPolicy, request);
    }
}

