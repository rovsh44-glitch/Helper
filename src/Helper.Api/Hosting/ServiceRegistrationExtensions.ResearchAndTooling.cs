using Helper.Runtime.Core;
using Helper.Runtime.Infrastructure;
using Helper.Runtime.WebResearch;
using Helper.Runtime.WebResearch.Extraction;
using Helper.Runtime.WebResearch.Fetching;
using Helper.Runtime.WebResearch.Normalization;
using Helper.Runtime.WebResearch.Providers;
using Helper.Runtime.WebResearch.Quality;
using Helper.Runtime.WebResearch.Rendering;
using Helper.Runtime.WebResearch.Safety;

namespace Helper.Api.Hosting;

public static partial class ServiceRegistrationExtensions
{
    private static IServiceCollection AddHelperResearchAndToolingServices(this IServiceCollection services)
    {
        services.AddSingleton<ISafeDnsResolver, SafeDnsResolver>();
        services.AddSingleton<IWebFetchSecurityPolicy, WebFetchSecurityPolicy>();
        services.AddSingleton<IRedirectGuard, RedirectGuard>();
        services.AddSingleton<IContentTypeAdmissionPolicy, ContentTypeAdmissionPolicy>();
        services.AddSingleton<IDocumentSourceNormalizationPolicy, DocumentSourceNormalizationPolicy>();
        services.AddSingleton<IWebPageContentExtractor, WebPageContentExtractor>();
        services.AddSingleton<IRemoteDocumentExtractor, PdfRemoteDocumentExtractor>();
        services.AddSingleton<IHardPageDetectionPolicy, HardPageDetectionPolicy>();
        services.AddSingleton<IRenderedPageBudgetPolicy, RenderedPageBudgetPolicy>();
        services.AddSingleton<IBrowserRenderFallbackService, BrowserRenderFallbackService>();
        services.AddSingleton<IWebProviderHealthState, WebProviderHealthState>();
        services.AddSingleton<ISearchCostBudgetPolicy, SearchCostBudgetPolicy>();
        services.AddSingleton<ITurnLatencyBudgetPolicy, TurnLatencyBudgetPolicy>();
        services.AddSingleton<IWebQueryPlanner, WebQueryPlanner>();
        services.AddSingleton<ISearchIterationPolicy, SearchIterationPolicy>();
        services.AddSingleton<ISearchEvidenceSufficiencyPolicy, SearchEvidenceSufficiencyPolicy>();
        services.AddSingleton<IWebDocumentQualityPolicy, WebDocumentQualityPolicy>();
        services.AddSingleton<IWebContentSafetyPolicy, WebContentSafetyPolicy>();
        services.AddSingleton<IPromptInjectionSanitizer, PromptInjectionSanitizer>();
        services.AddSingleton<IEvidenceBoundaryProjector, EvidenceBoundaryProjector>();
        services.AddSingleton<IWebPageFetcher>(sp => WebPageFetcherFactory.Create(
            sp.GetRequiredService<IWebFetchSecurityPolicy>(),
            sp.GetRequiredService<IRedirectGuard>(),
            sp.GetRequiredService<IContentTypeAdmissionPolicy>(),
            sp.GetRequiredService<IWebPageContentExtractor>(),
            sp.GetRequiredService<IHardPageDetectionPolicy>(),
            sp.GetRequiredService<IRenderedPageBudgetPolicy>(),
            sp.GetRequiredService<IBrowserRenderFallbackService>(),
            sp.GetRequiredService<IDocumentSourceNormalizationPolicy>(),
            sp.GetRequiredService<IRemoteDocumentExtractor>(),
            null,
            null,
            null,
            null));
        services.AddSingleton<IWebSearchProvider>(sp => new LocalSearchProvider(
            WebSearchProviderSettings.ReadLocalBaseUrl(),
            sp.GetRequiredService<IWebFetchSecurityPolicy>(),
            sp.GetRequiredService<IRedirectGuard>()));
        services.AddSingleton<IWebSearchProvider>(sp => new SearxSearchProvider(
            WebSearchProviderSettings.ReadSearxBaseUrl(),
            sp.GetRequiredService<IWebFetchSecurityPolicy>(),
            sp.GetRequiredService<IRedirectGuard>()));
        services.AddSingleton<IWebSearchProviderClient>(sp => new WebSearchProviderMux(
            sp.GetServices<IWebSearchProvider>(),
            sp.GetRequiredService<IWebProviderHealthState>(),
            sp.GetRequiredService<ISearchCostBudgetPolicy>(),
            sp.GetRequiredService<ITurnLatencyBudgetPolicy>()));
        services.AddSingleton<IWebSearchSessionCoordinator>(sp => WebSearchSessionCoordinatorFactory.Create(
            sp.GetRequiredService<IWebSearchProviderClient>(),
            sp.GetRequiredService<IWebQueryPlanner>(),
            sp.GetRequiredService<ISearchIterationPolicy>(),
            sp.GetRequiredService<ISearchEvidenceSufficiencyPolicy>(),
            sp.GetRequiredService<IWebPageFetcher>(),
            sp.GetRequiredService<IEvidenceBoundaryProjector>(),
            sp.GetRequiredService<IRenderedPageBudgetPolicy>(),
            sp.GetRequiredService<IWebDocumentQualityPolicy>()));
        services.AddSingleton<ILocalBaselineAnswerService>(sp => new LocalBaselineAnswerService(
            sp.GetRequiredService<AILink>(),
            sp.GetRequiredService<IRetrievalContextAssembler>()));
        services.AddSingleton<SimpleResearcher>(sp => ResearchRuntimeFactory.CreateSimpleResearcher(
            sp.GetRequiredService<AILink>(),
            sp.GetRequiredService<ICodeExecutor>(),
            sp.GetRequiredService<IVectorStore>(),
            sp.GetRequiredService<IWebSearchSessionCoordinator>(),
            sp.GetRequiredService<ILocalBaselineAnswerService>()));
        services.AddSingleton<IResearchService>(sp => sp.GetRequiredService<SimpleResearcher>());
        services.AddSingleton<IResearchEngine>(sp => ResearchRuntimeFactory.CreateResearchEngine(
            sp.GetRequiredService<IResearchService>(),
            sp.GetRequiredService<ILibrarianAgent>(),
            sp.GetRequiredService<IGoalManager>(),
            sp.GetRequiredService<IStrategicPlanner>(),
            sp.GetRequiredService<ICriticService>(),
            sp.GetRequiredService<AILink>()));
        services.AddSingleton<IMaintenanceService, MaintenanceService>();
        services.AddSingleton<IToolService, ToolService>();
        services.AddSingleton<IModelOrchestrator, ModelOrchestrator>();

        return services;
    }
}

