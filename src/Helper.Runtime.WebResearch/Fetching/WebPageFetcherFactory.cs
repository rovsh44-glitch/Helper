using Helper.Runtime.WebResearch.Extraction;
using Helper.Runtime.WebResearch.Normalization;
using Helper.Runtime.WebResearch.Rendering;

namespace Helper.Runtime.WebResearch.Fetching;

public static class WebPageFetcherFactory
{
    public static WebPageFetcher Create(
        IWebFetchSecurityPolicy securityPolicy,
        IRedirectGuard redirectGuard,
        IContentTypeAdmissionPolicy contentTypeAdmissionPolicy,
        IWebPageContentExtractor contentExtractor,
        IHardPageDetectionPolicy hardPageDetectionPolicy,
        IRenderedPageBudgetPolicy renderedPageBudgetPolicy,
        IBrowserRenderFallbackService browserRenderFallbackService,
        IDocumentSourceNormalizationPolicy documentSourceNormalizationPolicy,
        IRemoteDocumentExtractor remoteDocumentExtractor,
        HttpMessageHandler? handler = null,
        HttpMessageHandler? tlsCompatibilityHandler = null,
        HttpMessageHandler? proxyAwareHandler = null,
        HttpMessageHandler? proxyTlsCompatibilityHandler = null)
    {
        return new WebPageFetcher(
            securityPolicy,
            redirectGuard,
            contentTypeAdmissionPolicy,
            contentExtractor,
            hardPageDetectionPolicy,
            renderedPageBudgetPolicy,
            browserRenderFallbackService,
            documentSourceNormalizationPolicy,
            remoteDocumentExtractor,
            handler,
            tlsCompatibilityHandler,
            proxyAwareHandler,
            proxyTlsCompatibilityHandler);
    }
}

