using Helper.Runtime.Core;

namespace Helper.Runtime.Infrastructure;

public static class ProjectForgeOrchestratorFactory
{
    public static IProjectForgeOrchestrator Create(
        ITemplateManager templateManager,
        ITemplateFactory templateFactory,
        IProjectPlanner projectPlanner,
        ICodeGenerator codeGenerator,
        IBuildValidator buildValidator,
        IForgeArtifactValidator forgeArtifactValidator,
        IAutoHealer autoHealer,
        IntegrityAuditor integrityAuditor,
        AILink ai)
    {
        return new ProjectForgeOrchestrator(
            templateManager,
            templateFactory,
            projectPlanner,
            codeGenerator,
            buildValidator,
            forgeArtifactValidator,
            autoHealer,
            integrityAuditor,
            ai);
    }
}
