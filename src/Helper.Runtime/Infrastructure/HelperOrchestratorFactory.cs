using Helper.Runtime.Core;
using Helper.Runtime.Generation;
using Helper.Runtime.Swarm;

namespace Helper.Runtime.Infrastructure;

public static class HelperOrchestratorFactory
{
    public static IHelperOrchestrator Create(
        IModelOrchestrator modelOrchestrator,
        IResearchEngine researchEngine,
        IMaintenanceService maintenanceService,
        IProjectForgeOrchestrator projectForgeOrchestrator,
        TumenOrchestrator tumenOrchestrator,
        IReflectionService reflectionService,
        IGraphOrchestrator graphOrchestrator,
        IToolService toolService,
        IConsciousnessService consciousnessService,
        IAutoDebugger autoDebugger,
        IArchitectMutation architectMutation,
        IExpertConsultant expertConsultant,
        IBlueprintEngine blueprintEngine,
        ISurgicalToolbox surgicalToolbox,
        IPlatformGuard platformGuard,
        IInternalObserver internalObserver,
        IIntentBcaster intentBcaster,
        IMetacognitiveAgent metacognitiveAgent,
        ITemplateRoutingService templateRoutingService,
        IGenerationMetricsService generationMetricsService,
        IGenerationValidationReportWriter generationValidationReportWriter,
        IGenerationHealthReporter generationHealthReporter,
        IFailureEnvelopeFactory failureEnvelopeFactory,
        IGenerationStageTimeoutPolicy generationStageTimeoutPolicy,
        IFixStrategyRunner fixStrategyRunner,
        IGenerationTemplatePromotionService generationTemplatePromotionService,
        IRouteTelemetryService? routeTelemetryService = null)
    {
        return new HelperOrchestrator(
            modelOrchestrator,
            researchEngine,
            maintenanceService,
            projectForgeOrchestrator,
            tumenOrchestrator,
            reflectionService,
            graphOrchestrator,
            toolService,
            consciousnessService,
            autoDebugger,
            architectMutation,
            expertConsultant,
            blueprintEngine,
            surgicalToolbox,
            platformGuard,
            internalObserver,
            intentBcaster,
            metacognitiveAgent,
            templateRoutingService,
            generationMetricsService,
            generationValidationReportWriter,
            generationHealthReporter,
            failureEnvelopeFactory,
            generationStageTimeoutPolicy,
            fixStrategyRunner,
            generationTemplatePromotionService,
            routeTelemetryService);
    }
}
