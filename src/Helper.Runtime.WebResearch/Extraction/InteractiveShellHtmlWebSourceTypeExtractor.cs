namespace Helper.Runtime.WebResearch.Extraction;

internal sealed class InteractiveShellHtmlWebSourceTypeExtractor : IWebSourceTypeExtractor
{
    private readonly IWebPageContentExtractor _contentExtractor;
    private readonly WebSourceTypeExtractionQualityPolicy _qualityPolicy;

    public InteractiveShellHtmlWebSourceTypeExtractor(
        IWebPageContentExtractor contentExtractor,
        WebSourceTypeExtractionQualityPolicy qualityPolicy)
    {
        _contentExtractor = contentExtractor;
        _qualityPolicy = qualityPolicy;
    }

    public string Id => "interactive_shell_html";

    public bool CanHandle(WebSourceTypeExtractionRequest request)
    {
        return request.DecodedContent is not null &&
               request.SourceType.Kind.Equals("interactive_shell", StringComparison.Ordinal);
    }

    public Task<WebSourceTypeExtractionResult> ExtractAsync(WebSourceTypeExtractionRequest request, CancellationToken ct = default)
    {
        return HtmlSourceTypeExtractorSupport.ExtractAsync(Id, _contentExtractor, _qualityPolicy, request);
    }
}

