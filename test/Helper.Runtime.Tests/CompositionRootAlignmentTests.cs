namespace Helper.Runtime.Tests;

public class CompositionRootAlignmentTests
{
    [Fact]
    public void ApiCompositionRoot_UsesSharedRuntimeFactories()
    {
        var orchestration = ReadWorkspaceFile("src", "Helper.Api", "Hosting", "ServiceRegistrationExtensions.Orchestration.cs");
        var research = ReadWorkspaceFile("src", "Helper.Api", "Hosting", "ServiceRegistrationExtensions.ResearchAndTooling.cs");
        var generation = ReadWorkspaceFile("src", "Helper.Api", "Hosting", "ServiceRegistrationExtensions.GenerationAndTemplate.cs");

        Assert.Contains("PersonaRuntimeFactory.Create(", orchestration, StringComparison.Ordinal);
        Assert.Contains("TumenRuntimeFactory.CreateTumenOrchestrator(", orchestration, StringComparison.Ordinal);
        Assert.Contains("TumenRuntimeFactory.CreateGraphOrchestrator(", orchestration, StringComparison.Ordinal);
        Assert.Contains("LearningRuntimeFactory.CreateSyntheticTaskRunner(", orchestration, StringComparison.Ordinal);
        Assert.Contains("LearningRuntimeFactory.CreateSyntheticLearningService(", orchestration, StringComparison.Ordinal);
        Assert.Contains("HelperOrchestratorFactory.Create(", orchestration, StringComparison.Ordinal);

        Assert.Contains("ResearchRuntimeFactory.CreateSimpleResearcher(", research, StringComparison.Ordinal);
        Assert.Contains("ResearchRuntimeFactory.CreateResearchEngine(", research, StringComparison.Ordinal);
        Assert.DoesNotContain("new SimpleResearcher(", research, StringComparison.Ordinal);
        Assert.DoesNotContain("new ResearchEngine(", research, StringComparison.Ordinal);

        Assert.Contains("GenerationRuntimeFactory.CreateTemplateCertificationService(", generation, StringComparison.Ordinal);
        Assert.Contains("GenerationRuntimeFactory.CreateTemplatePromotionService(", generation, StringComparison.Ordinal);
        Assert.Contains("ProjectForgeOrchestratorFactory.Create(", generation, StringComparison.Ordinal);
        Assert.DoesNotContain("new TemplateCertificationService(", generation, StringComparison.Ordinal);
        Assert.DoesNotContain("new GenerationTemplatePromotionService(", generation, StringComparison.Ordinal);
        Assert.DoesNotContain("new ProjectForgeOrchestrator(", generation, StringComparison.Ordinal);
    }

    [Fact]
    public void CliCompositionRoot_UsesSameSharedRuntimeFactories()
    {
        var builder = ReadWorkspaceFile("src", "Helper.Runtime.Cli", "HelperCliRuntimeBuilder.cs");

        Assert.Contains("PersonaRuntimeFactory.Create(", builder, StringComparison.Ordinal);
        Assert.Contains("ResearchRuntimeFactory.CreateSimpleResearcher(", builder, StringComparison.Ordinal);
        Assert.Contains("GenerationRuntimeFactory.CreateTemplateCertificationService(", builder, StringComparison.Ordinal);
        Assert.Contains("GenerationRuntimeFactory.CreateTemplatePromotionService(", builder, StringComparison.Ordinal);
        Assert.Contains("ProjectForgeOrchestratorFactory.Create(", builder, StringComparison.Ordinal);
        Assert.Contains("TumenRuntimeFactory.CreateTumenOrchestrator(", builder, StringComparison.Ordinal);
        Assert.Contains("TumenRuntimeFactory.CreateGraphOrchestrator(", builder, StringComparison.Ordinal);
        Assert.Contains("LearningRuntimeFactory.CreateSyntheticLearningService(", builder, StringComparison.Ordinal);
        Assert.Contains("HelperOrchestratorFactory.Create(", builder, StringComparison.Ordinal);
        Assert.DoesNotContain("new ProjectForgeOrchestrator(", builder, StringComparison.Ordinal);
        Assert.DoesNotContain("new TemplateCertificationService(", builder, StringComparison.Ordinal);
        Assert.DoesNotContain("new ResearchEngine(", builder, StringComparison.Ordinal);
    }

    private static string ReadWorkspaceFile(params string[] segments)
    {
        return File.ReadAllText(TestWorkspaceRoot.ResolveFile(segments));
    }
}
