using Helper.Runtime.Core;
using Helper.Runtime.Infrastructure;

namespace Helper.Runtime.Generation;

public static class GenerationRuntimeFactory
{
    public static TemplateCertificationService CreateTemplateCertificationService(
        ITemplateManager templateManager,
        ITemplateLifecycleService templateLifecycleService,
        IGenerationCompileGate generationCompileGate,
        IBuildValidator buildValidator,
        IForgeArtifactValidator forgeArtifactValidator,
        string templatesRoot,
        string workspaceRoot)
    {
        return new TemplateCertificationService(
            templateManager,
            templateLifecycleService,
            generationCompileGate,
            buildValidator,
            forgeArtifactValidator,
            templatesRoot,
            workspaceRoot);
    }

    public static IGenerationTemplatePromotionService CreateTemplatePromotionService(
        ITemplateRoutingService templateRoutingService,
        ITemplateGeneralizer templateGeneralizer,
        ITemplateLifecycleService templateLifecycleService,
        IGenerationCompileGate generationCompileGate,
        ITemplatePromotionFeatureProfileService templatePromotionFeatureProfileService,
        IGenerationMetricsService generationMetricsService,
        ITemplateCertificationService templateCertificationService,
        string templatesRoot)
    {
        return new GenerationTemplatePromotionService(
            templateRoutingService,
            templateGeneralizer,
            templateLifecycleService,
            generationCompileGate,
            templatePromotionFeatureProfileService,
            generationMetricsService,
            templateCertificationService,
            templatesRoot);
    }
}
