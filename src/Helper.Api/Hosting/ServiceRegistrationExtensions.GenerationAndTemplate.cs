using Helper.Runtime.Core;
using Helper.Runtime.Evolution;
using Helper.Runtime.Generation;
using Helper.Runtime.Infrastructure;
using Helper.Runtime.Swarm;

namespace Helper.Api.Hosting;

public static partial class ServiceRegistrationExtensions
{
    private static IServiceCollection AddHelperGenerationAndTemplateServices(this IServiceCollection services, ApiRuntimeConfig runtimeConfig)
    {
        services.AddSingleton<IBlueprintEngine, BlueprintEngine>();
        services.AddSingleton<IIdentifierSanitizer, IdentifierSanitizer>();
        services.AddSingleton<IGenerationPathSanitizer, GenerationPathSanitizer>();
        services.AddSingleton<IMethodSignatureValidator, MethodSignatureValidator>();
        services.AddSingleton<IMethodSignatureNormalizer, MethodSignatureNormalizer>();
        services.AddSingleton<ITypeTokenExtractor, TypeTokenExtractor>();
        services.AddSingleton<IUsingInferenceService, UsingInferenceService>();
        services.AddSingleton<IMethodBodySemanticGuard, MethodBodySemanticGuard>();
        services.AddSingleton<IBlueprintJsonSchemaValidator, BlueprintJsonSchemaValidator>();
        services.AddSingleton<IBlueprintContractValidator, BlueprintContractValidator>();
        services.AddSingleton<IGeneratedFileAstValidator, GeneratedFileAstValidator>();
        services.AddSingleton<IGenerationPathPolicy, GenerationPathPolicy>();
        services.AddSingleton<ICompileGateRepairService, CompileGateRepairService>();
        services.AddSingleton<CompileGateWorkspacePreparer>();
        services.AddSingleton<CompileGateFormatVerifier>();
        services.AddSingleton<IGenerationCompileGate, GenerationCompileGate>();
        services.AddSingleton<IGenerationValidationReportWriter, GenerationValidationReportWriter>();
        services.AddSingleton<IGenerationHealthReporter, GenerationHealthReporter>();
        services.AddSingleton<IGenerationMetricsService, GenerationMetricsService>();
        services.AddSingleton<ITemplatePromotionFeatureProfileService, TemplatePromotionFeatureProfileService>();
        services.AddSingleton<IGenerationStageTimeoutPolicy, GenerationStageTimeoutPolicy>();
        services.AddSingleton<IFixStrategyHistoryProvider, FileFixStrategyHistoryProvider>();
        services.AddSingleton<IFixSafetyPolicy, FixSafetyPolicy>();
        services.AddSingleton<IFixInvariantEvaluator, FixInvariantEvaluator>();
        services.AddSingleton<IFixPlanner>(sp => new GenerationFixPlanner(sp.GetRequiredService<IFixStrategyHistoryProvider>()));
        services.AddSingleton<IFixVerifier, GenerationFixVerifier>();
        services.AddSingleton<IFixAttemptLedger, FileFixAttemptLedger>();
        services.AddSingleton<IFixPatchApplier, DeterministicCompileGatePatchApplier>();
        services.AddSingleton<IFixPatchApplier, RuntimeConfigPatchApplier>();
        services.AddSingleton<IFixPatchApplier, LlmAutoHealerPatchApplier>();
        services.AddSingleton<IFixStrategyRunner, FixStrategyRunner>();

        services.AddSingleton<ITemplateManager>(_ => new ProjectTemplateManager(runtimeConfig.TemplatesRoot));
        services.AddSingleton<ITemplateFactory>(sp => new ProjectTemplateFactory(
            sp.GetRequiredService<AILink>(),
            sp.GetRequiredService<IProcessGuard>(),
            runtimeConfig.TemplatesRoot));
        services.AddSingleton<ITemplateRoutingService, TemplateRoutingService>();
        services.AddSingleton<ITemplateLifecycleService>(_ => new TemplateLifecycleService(runtimeConfig.TemplatesRoot));
        services.AddSingleton<ISurgeonAgent, SurgeonAgent>();
        services.AddSingleton<IEvolutionEngine, EvolutionEngine>();

        services.AddSingleton<ITemplateCertificationService>(sp => GenerationRuntimeFactory.CreateTemplateCertificationService(
            sp.GetRequiredService<ITemplateManager>(),
            sp.GetRequiredService<ITemplateLifecycleService>(),
            sp.GetRequiredService<IGenerationCompileGate>(),
            sp.GetRequiredService<IBuildValidator>(),
            sp.GetRequiredService<IForgeArtifactValidator>(),
            runtimeConfig.TemplatesRoot,
            runtimeConfig.RootPath));
        services.AddSingleton<IGenerationTemplatePromotionService>(sp => GenerationRuntimeFactory.CreateTemplatePromotionService(
            sp.GetRequiredService<ITemplateRoutingService>(),
            sp.GetRequiredService<ITemplateGeneralizer>(),
            sp.GetRequiredService<ITemplateLifecycleService>(),
            sp.GetRequiredService<IGenerationCompileGate>(),
            sp.GetRequiredService<ITemplatePromotionFeatureProfileService>(),
            sp.GetRequiredService<IGenerationMetricsService>(),
            sp.GetRequiredService<ITemplateCertificationService>(),
            runtimeConfig.TemplatesRoot));
        services.AddSingleton<IGenerationPromotionService, GenerationPromotionService>();
        services.AddSingleton<IParityCertificationService, ParityCertificationService>();
        services.AddSingleton<IParityGateEvaluator, ParityGateEvaluator>();
        services.AddSingleton<IParityWindowGateService, ParityWindowGateService>();
        services.AddSingleton<IGenerationParityBenchmarkService, GenerationParityBenchmarkService>();
        services.AddSingleton<IClosedLoopPredictabilityService, ClosedLoopPredictabilityService>();

        services.AddSingleton<IProjectForgeOrchestrator>(sp => ProjectForgeOrchestratorFactory.Create(
            sp.GetRequiredService<ITemplateManager>(),
            sp.GetRequiredService<ITemplateFactory>(),
            sp.GetRequiredService<IProjectPlanner>(),
            sp.GetRequiredService<ICodeGenerator>(),
            sp.GetRequiredService<IBuildValidator>(),
            sp.GetRequiredService<IForgeArtifactValidator>(),
            sp.GetRequiredService<IAutoHealer>(),
            sp.GetRequiredService<IntegrityAuditor>(),
            sp.GetRequiredService<AILink>()));

        return services;
    }
}

