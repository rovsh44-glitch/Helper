using Helper.Runtime.WebResearch.Fetching;
using Helper.Runtime.WebResearch.Normalization;
using Helper.Runtime.WebResearch.Quality;
using Helper.Runtime.WebResearch.Ranking;
using Helper.Runtime.WebResearch.Rendering;
using Helper.Runtime.WebResearch.Safety;

namespace Helper.Runtime.WebResearch;

public static class WebSearchSessionCoordinatorFactory
{
    public static WebSearchSessionCoordinator Create(IWebSearchProviderClient providerClient)
    {
        var canonicalUrlResolver = new CanonicalUrlResolver();

        return Create(
            providerClient,
            new WebQueryPlanner(),
            new SearchIterationPolicy(),
            new SearchEvidenceSufficiencyPolicy(),
            NoopWebPageFetcher.Instance,
            new EvidenceBoundaryProjector(),
            new RenderedPageBudgetPolicy(),
            new SourceAuthorityScorer(),
            new SpamAndSeoDemotionPolicy(),
            canonicalUrlResolver,
            new DuplicateContentCollapsePolicy(canonicalUrlResolver),
            new EventClusterBuilder(),
            new WebDocumentQualityPolicy(),
            new MedicalEvidenceFloorPolicy(),
            new DomainAuthorityFloorPolicy(),
            new WebDocumentReranker(),
            new FetchStabilityPolicy());
    }

    public static WebSearchSessionCoordinator Create(
        IWebSearchProviderClient providerClient,
        IWebPageFetcher pageFetcher,
        IEvidenceBoundaryProjector evidenceBoundaryProjector,
        IRenderedPageBudgetPolicy renderedPageBudgetPolicy,
        IWebDocumentQualityPolicy? documentQualityPolicy = null)
    {
        var canonicalUrlResolver = new CanonicalUrlResolver();

        return Create(
            providerClient,
            new WebQueryPlanner(),
            new SearchIterationPolicy(),
            new SearchEvidenceSufficiencyPolicy(),
            pageFetcher,
            evidenceBoundaryProjector,
            renderedPageBudgetPolicy,
            new SourceAuthorityScorer(),
            new SpamAndSeoDemotionPolicy(),
            canonicalUrlResolver,
            new DuplicateContentCollapsePolicy(canonicalUrlResolver),
            new EventClusterBuilder(),
            documentQualityPolicy ?? new WebDocumentQualityPolicy(),
            new MedicalEvidenceFloorPolicy(),
            new DomainAuthorityFloorPolicy(),
            new WebDocumentReranker(),
            new FetchStabilityPolicy());
    }

    public static WebSearchSessionCoordinator Create(
        IWebSearchProviderClient providerClient,
        IWebQueryPlanner queryPlanner,
        ISearchIterationPolicy iterationPolicy,
        ISearchEvidenceSufficiencyPolicy evidenceSufficiencyPolicy,
        IWebPageFetcher pageFetcher,
        IEvidenceBoundaryProjector evidenceBoundaryProjector,
        IRenderedPageBudgetPolicy renderedPageBudgetPolicy,
        IWebDocumentQualityPolicy documentQualityPolicy)
    {
        var canonicalUrlResolver = new CanonicalUrlResolver();

        return Create(
            providerClient,
            queryPlanner,
            iterationPolicy,
            evidenceSufficiencyPolicy,
            pageFetcher,
            evidenceBoundaryProjector,
            renderedPageBudgetPolicy,
            new SourceAuthorityScorer(),
            new SpamAndSeoDemotionPolicy(),
            canonicalUrlResolver,
            new DuplicateContentCollapsePolicy(canonicalUrlResolver),
            new EventClusterBuilder(),
            documentQualityPolicy,
            new MedicalEvidenceFloorPolicy(),
            new DomainAuthorityFloorPolicy(),
            new WebDocumentReranker(),
            new FetchStabilityPolicy());
    }

    internal static WebSearchSessionCoordinator Create(
        IWebSearchProviderClient providerClient,
        IWebQueryPlanner queryPlanner,
        ISearchIterationPolicy iterationPolicy,
        ISearchEvidenceSufficiencyPolicy evidenceSufficiencyPolicy,
        IWebPageFetcher pageFetcher,
        IEvidenceBoundaryProjector evidenceBoundaryProjector)
    {
        var canonicalUrlResolver = new CanonicalUrlResolver();

        return Create(
            providerClient,
            queryPlanner,
            iterationPolicy,
            evidenceSufficiencyPolicy,
            pageFetcher,
            evidenceBoundaryProjector,
            new RenderedPageBudgetPolicy(),
            new SourceAuthorityScorer(),
            new SpamAndSeoDemotionPolicy(),
            canonicalUrlResolver,
            new DuplicateContentCollapsePolicy(canonicalUrlResolver),
            new EventClusterBuilder(),
            new WebDocumentQualityPolicy(),
            new MedicalEvidenceFloorPolicy(),
            new DomainAuthorityFloorPolicy(),
            new WebDocumentReranker(),
            new FetchStabilityPolicy());
    }

    internal static WebSearchSessionCoordinator Create(
        IWebSearchProviderClient providerClient,
        IWebQueryPlanner queryPlanner,
        ISearchIterationPolicy iterationPolicy,
        ISearchEvidenceSufficiencyPolicy evidenceSufficiencyPolicy,
        IWebPageFetcher pageFetcher,
        IEvidenceBoundaryProjector evidenceBoundaryProjector,
        IRenderedPageBudgetPolicy renderedPageBudgetPolicy,
        ISourceAuthorityScorer authorityScorer,
        ISpamAndSeoDemotionPolicy spamDemotionPolicy,
        IWebDocumentQualityPolicy? documentQualityPolicy = null)
    {
        var canonicalUrlResolver = new CanonicalUrlResolver();

        return Create(
            providerClient,
            queryPlanner,
            iterationPolicy,
            evidenceSufficiencyPolicy,
            pageFetcher,
            evidenceBoundaryProjector,
            renderedPageBudgetPolicy,
            authorityScorer,
            spamDemotionPolicy,
            canonicalUrlResolver,
            new DuplicateContentCollapsePolicy(canonicalUrlResolver),
            new EventClusterBuilder(),
            documentQualityPolicy ?? new WebDocumentQualityPolicy(),
            new MedicalEvidenceFloorPolicy(),
            new DomainAuthorityFloorPolicy(),
            new WebDocumentReranker(),
            new FetchStabilityPolicy());
    }

    internal static WebSearchSessionCoordinator Create(
        IWebSearchProviderClient providerClient,
        IWebQueryPlanner queryPlanner,
        ISearchIterationPolicy iterationPolicy,
        ISearchEvidenceSufficiencyPolicy evidenceSufficiencyPolicy,
        IWebPageFetcher pageFetcher,
        IEvidenceBoundaryProjector evidenceBoundaryProjector,
        IRenderedPageBudgetPolicy renderedPageBudgetPolicy,
        ISourceAuthorityScorer authorityScorer,
        ISpamAndSeoDemotionPolicy spamDemotionPolicy,
        ICanonicalUrlResolver canonicalUrlResolver,
        IDuplicateContentCollapsePolicy duplicateCollapsePolicy,
        IEventClusterBuilder eventClusterBuilder,
        IWebDocumentQualityPolicy? documentQualityPolicy = null,
        IMedicalEvidenceFloorPolicy? medicalEvidenceFloorPolicy = null,
        IDomainAuthorityFloorPolicy? domainAuthorityFloorPolicy = null,
        IWebDocumentReranker? documentReranker = null,
        IFetchStabilityPolicy? fetchStabilityPolicy = null)
    {
        return Create(
            providerClient,
            queryPlanner,
            iterationPolicy,
            evidenceSufficiencyPolicy,
            CreateDocumentPipeline(
                pageFetcher,
                evidenceBoundaryProjector,
                renderedPageBudgetPolicy,
                authorityScorer,
                spamDemotionPolicy,
                canonicalUrlResolver,
                duplicateCollapsePolicy,
                eventClusterBuilder,
                documentQualityPolicy ?? new WebDocumentQualityPolicy(),
                medicalEvidenceFloorPolicy ?? new MedicalEvidenceFloorPolicy(),
                domainAuthorityFloorPolicy ?? new DomainAuthorityFloorPolicy(),
                documentReranker ?? new WebDocumentReranker(),
                fetchStabilityPolicy ?? new FetchStabilityPolicy()));
    }

    internal static WebSearchSessionCoordinator Create(
        IWebSearchProviderClient providerClient,
        IWebQueryPlanner queryPlanner,
        ISearchIterationPolicy iterationPolicy,
        ISearchEvidenceSufficiencyPolicy evidenceSufficiencyPolicy,
        IWebSearchDocumentPipeline documentPipeline)
    {
        return new WebSearchSessionCoordinator(
            providerClient,
            queryPlanner,
            iterationPolicy,
            evidenceSufficiencyPolicy,
            documentPipeline);
    }

    internal static IWebSearchDocumentPipeline CreateDocumentPipeline(
        IWebPageFetcher pageFetcher,
        IEvidenceBoundaryProjector evidenceBoundaryProjector,
        IRenderedPageBudgetPolicy renderedPageBudgetPolicy,
        ISourceAuthorityScorer authorityScorer,
        ISpamAndSeoDemotionPolicy spamDemotionPolicy,
        ICanonicalUrlResolver canonicalUrlResolver,
        IDuplicateContentCollapsePolicy duplicateCollapsePolicy,
        IEventClusterBuilder eventClusterBuilder,
        IWebDocumentQualityPolicy documentQualityPolicy,
        IMedicalEvidenceFloorPolicy medicalEvidenceFloorPolicy,
        IDomainAuthorityFloorPolicy domainAuthorityFloorPolicy,
        IWebDocumentReranker documentReranker,
        IFetchStabilityPolicy fetchStabilityPolicy)
    {
        var candidateNormalizer = new WebSearchCandidateNormalizer(
            authorityScorer,
            spamDemotionPolicy,
            canonicalUrlResolver,
            duplicateCollapsePolicy,
            documentQualityPolicy,
            medicalEvidenceFloorPolicy,
            domainAuthorityFloorPolicy,
            documentReranker,
            fetchStabilityPolicy);
        var diagnosticsSummarizer = new WebSearchFetchDiagnosticsSummarizer();
        var fetchEnricher = new WebSearchFetchEnricher(
            pageFetcher,
            evidenceBoundaryProjector,
            renderedPageBudgetPolicy,
            documentQualityPolicy,
            fetchStabilityPolicy,
            diagnosticsSummarizer);
        var postFetchSelectionPolicy = new PostFetchSelectionPolicy(
            pageFetcher,
            candidateNormalizer,
            domainAuthorityFloorPolicy);
        return new WebSearchDocumentPipeline(
            pageFetcher,
            candidateNormalizer,
            fetchEnricher,
            duplicateCollapsePolicy,
            eventClusterBuilder,
            postFetchSelectionPolicy);
    }
}

