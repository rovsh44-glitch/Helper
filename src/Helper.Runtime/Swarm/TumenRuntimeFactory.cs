using Helper.Runtime.Core;
using Helper.Runtime.Generation;
using Helper.Runtime.Infrastructure;

namespace Helper.Runtime.Swarm;

public static class TumenRuntimeFactory
{
    public static TumenOrchestrator CreateTumenOrchestrator(
        AILink ai,
        ICodeSanitizer codeSanitizer,
        IProjectForgeOrchestrator projectForgeOrchestrator,
        ITemplateRoutingService templateRoutingService,
        IBuildExecutor buildExecutor,
        ICriticService criticService,
        IReflectionService reflectionService,
        IInternalObserver internalObserver,
        IAtomicOrchestrator atomicOrchestrator,
        IIntentBcaster intentBcaster,
        IIdentifierSanitizer identifierSanitizer,
        IGenerationPathSanitizer generationPathSanitizer,
        IMethodSignatureValidator methodSignatureValidator,
        IMethodSignatureNormalizer methodSignatureNormalizer,
        IUsingInferenceService usingInferenceService,
        IMethodBodySemanticGuard methodBodySemanticGuard,
        IBlueprintJsonSchemaValidator blueprintJsonSchemaValidator,
        IBlueprintContractValidator blueprintContractValidator,
        IGeneratedFileAstValidator generatedFileAstValidator,
        IGenerationPathPolicy generationPathPolicy,
        IGenerationCompileGate generationCompileGate,
        IGenerationValidationReportWriter generationValidationReportWriter,
        IGenerationHealthReporter generationHealthReporter,
        IGenerationMetricsService generationMetricsService)
    {
        return new TumenOrchestrator(
            ai,
            codeSanitizer,
            projectForgeOrchestrator,
            templateRoutingService,
            buildExecutor,
            criticService,
            reflectionService,
            internalObserver,
            atomicOrchestrator,
            intentBcaster,
            identifierSanitizer,
            generationPathSanitizer,
            methodSignatureValidator,
            methodSignatureNormalizer,
            usingInferenceService,
            methodBodySemanticGuard,
            blueprintJsonSchemaValidator,
            blueprintContractValidator,
            generatedFileAstValidator,
            generationPathPolicy,
            generationCompileGate,
            generationValidationReportWriter,
            generationHealthReporter,
            generationMetricsService);
    }

    public static IGraphOrchestrator CreateGraphOrchestrator(
        AILink ai,
        TumenOrchestrator tumenOrchestrator,
        ICriticService criticService,
        IVisualInspector visualInspector,
        IComplexityAnalyzer complexityAnalyzer,
        IContextDistiller contextDistiller)
    {
        return new GraphOrchestrator(
            ai,
            tumenOrchestrator,
            criticService,
            visualInspector,
            complexityAnalyzer,
            contextDistiller);
    }
}
