namespace Helper.Runtime.WebResearch.Extraction;

internal sealed class DocumentLikeHtmlWebSourceTypeExtractor : IWebSourceTypeExtractor
{
    private readonly IWebPageContentExtractor _contentExtractor;
    private readonly WebSourceTypeExtractionQualityPolicy _qualityPolicy;

    public DocumentLikeHtmlWebSourceTypeExtractor(
        IWebPageContentExtractor contentExtractor,
        WebSourceTypeExtractionQualityPolicy qualityPolicy)
    {
        _contentExtractor = contentExtractor;
        _qualityPolicy = qualityPolicy;
    }

    public string Id => "document_like_html";

    public bool CanHandle(WebSourceTypeExtractionRequest request)
    {
        return request.DecodedContent is not null &&
               request.SourceType.DocumentLike &&
               !request.SourceType.Kind.Equals("document_pdf", StringComparison.Ordinal);
    }

    public Task<WebSourceTypeExtractionResult> ExtractAsync(WebSourceTypeExtractionRequest request, CancellationToken ct = default)
    {
        return HtmlSourceTypeExtractorSupport.ExtractAsync(Id, _contentExtractor, _qualityPolicy, request);
    }
}

