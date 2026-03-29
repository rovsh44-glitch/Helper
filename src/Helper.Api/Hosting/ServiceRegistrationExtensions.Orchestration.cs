using Helper.Runtime.Core;
using Helper.Runtime.Evolution;
using Helper.Runtime.Generation;
using Helper.Runtime.Infrastructure;
using Helper.Runtime.Swarm;

namespace Helper.Api.Hosting;

public static partial class ServiceRegistrationExtensions
{
    private static IServiceCollection AddHelperOrchestrationServices(this IServiceCollection services, ApiRuntimeConfig runtimeConfig)
    {
        services.AddSingleton<IInternalObserver, InternalObserver>();
        services.AddSingleton<IIntentBcaster, IntentBcaster>();
        services.AddSingleton<IAtomicOrchestrator, AtomicOrchestrator>();
        services.AddSingleton<IMetacognitiveAgent, MetacognitiveAgent>();

        services.AddSingleton<IPersonaOrchestrator>(sp => PersonaRuntimeFactory.Create(
            sp.GetRequiredService<AILink>(),
            sp.GetRequiredService<IVectorStore>(),
            sp.GetRequiredService<IWebSearcher>()));

        services.AddSingleton<ICriticService, LlmCritic>();

        services.AddSingleton<TumenOrchestrator>(sp => TumenRuntimeFactory.CreateTumenOrchestrator(
            sp.GetRequiredService<AILink>(),
            sp.GetRequiredService<ICodeSanitizer>(),
            sp.GetRequiredService<IProjectForgeOrchestrator>(),
            sp.GetRequiredService<ITemplateRoutingService>(),
            sp.GetRequiredService<IBuildExecutor>(),
            sp.GetRequiredService<ICriticService>(),
            sp.GetRequiredService<IReflectionService>(),
            sp.GetRequiredService<IInternalObserver>(),
            sp.GetRequiredService<IAtomicOrchestrator>(),
            sp.GetRequiredService<IIntentBcaster>(),
            sp.GetRequiredService<IIdentifierSanitizer>(),
            sp.GetRequiredService<IGenerationPathSanitizer>(),
            sp.GetRequiredService<IMethodSignatureValidator>(),
            sp.GetRequiredService<IMethodSignatureNormalizer>(),
            sp.GetRequiredService<IUsingInferenceService>(),
            sp.GetRequiredService<IMethodBodySemanticGuard>(),
            sp.GetRequiredService<IBlueprintJsonSchemaValidator>(),
            sp.GetRequiredService<IBlueprintContractValidator>(),
            sp.GetRequiredService<IGeneratedFileAstValidator>(),
            sp.GetRequiredService<IGenerationPathPolicy>(),
            sp.GetRequiredService<IGenerationCompileGate>(),
            sp.GetRequiredService<IGenerationValidationReportWriter>(),
            sp.GetRequiredService<IGenerationHealthReporter>(),
            sp.GetRequiredService<IGenerationMetricsService>()));

        services.AddSingleton<IGraphOrchestrator>(sp => TumenRuntimeFactory.CreateGraphOrchestrator(
            sp.GetRequiredService<AILink>(),
            sp.GetRequiredService<TumenOrchestrator>(),
            sp.GetRequiredService<ICriticService>(),
            sp.GetRequiredService<IVisualInspector>(),
            sp.GetRequiredService<IComplexityAnalyzer>(),
            sp.GetRequiredService<IContextDistiller>()));

        services.AddSingleton<ILearningPathPolicy>(_ => new LearningPathPolicy());
        services.AddSingleton<IIndexingQueueStore, IndexingQueueStore>();
        services.AddSingleton<ILearningLifecycleController, LearningLifecycleController>();
        services.AddSingleton<ISyntheticTaskRunner>(sp => LearningRuntimeFactory.CreateSyntheticTaskRunner(
            sp.GetRequiredService<AILink>(),
            sp.GetRequiredService<IGraphOrchestrator>(),
            sp.GetRequiredService<ILearningPathPolicy>()));
        services.AddSingleton<ISyntheticLearningService>(sp => LearningRuntimeFactory.CreateSyntheticLearningService(
            sp.GetRequiredService<ILibrarianAgent>(),
            sp.GetRequiredService<IIndexingTelemetrySink>(),
            sp.GetRequiredService<ILearningPathPolicy>(),
            sp.GetRequiredService<IIndexingQueueStore>(),
            sp.GetRequiredService<ILearningLifecycleController>(),
            sp.GetRequiredService<ISyntheticTaskRunner>()));
        services.AddSingleton<ILearningCoordinator, LearningCoordinator>();

        services.AddSingleton<IHelperOrchestrator>(sp => HelperOrchestratorFactory.Create(
            sp.GetRequiredService<IModelOrchestrator>(),
            sp.GetRequiredService<IResearchEngine>(),
            sp.GetRequiredService<IMaintenanceService>(),
            sp.GetRequiredService<IProjectForgeOrchestrator>(),
            sp.GetRequiredService<TumenOrchestrator>(),
            sp.GetRequiredService<IReflectionService>(),
            sp.GetRequiredService<IGraphOrchestrator>(),
            sp.GetRequiredService<IToolService>(),
            sp.GetRequiredService<IConsciousnessService>(),
            sp.GetRequiredService<IAutoDebugger>(),
            sp.GetRequiredService<IArchitectMutation>(),
            sp.GetRequiredService<IExpertConsultant>(),
            sp.GetRequiredService<IBlueprintEngine>(),
            sp.GetRequiredService<ISurgicalToolbox>(),
            sp.GetRequiredService<IPlatformGuard>(),
            sp.GetRequiredService<IInternalObserver>(),
            sp.GetRequiredService<IIntentBcaster>(),
            sp.GetRequiredService<IMetacognitiveAgent>(),
            sp.GetRequiredService<ITemplateRoutingService>(),
            sp.GetRequiredService<IGenerationMetricsService>(),
            sp.GetRequiredService<IGenerationValidationReportWriter>(),
            sp.GetRequiredService<IGenerationHealthReporter>(),
            sp.GetRequiredService<IFailureEnvelopeFactory>(),
            sp.GetRequiredService<IGenerationStageTimeoutPolicy>(),
            sp.GetRequiredService<IFixStrategyRunner>(),
            sp.GetRequiredService<IGenerationTemplatePromotionService>(),
            sp.GetRequiredService<IRouteTelemetryService>()));

        return services;
    }
}

