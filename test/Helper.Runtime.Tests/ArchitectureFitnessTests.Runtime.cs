using Helper.Runtime.Infrastructure;

namespace Helper.Runtime.Tests;

public partial class ArchitectureFitnessTests
{
    [Fact]
    public void ApiProject_DoesNotMaskSandboxSourceCompilation()
    {
        var apiProjectPath = ResolveWorkspaceFile("src", "Helper.Api", "Helper.Api.csproj");
        var projectText = File.ReadAllText(apiProjectPath);

        Assert.DoesNotContain("Compile Remove=\"sandbox\\**\\*.cs\"", projectText, StringComparison.Ordinal);
    }

    [Fact]
    public void SyntheticLearning_DefaultOutput_DoesNotWriteIntoSrc()
    {
        var path = LearningPathPolicy.ResolveDefaultActiveLearningOutputPath();

        Assert.NotNull(path);
        Assert.DoesNotContain($"{Path.DirectorySeparatorChar}src{Path.DirectorySeparatorChar}", path!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"{Path.DirectorySeparatorChar}runtime{Path.DirectorySeparatorChar}active_learning", path!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SyntheticLearning_RemainsCoordinator_OverExtractedCollaborators()
    {
        var servicePath = ResolveWorkspaceFile("src", "Helper.Runtime", "SyntheticLearningService.cs");
        var pathPolicyPath = ResolveWorkspaceFile("src", "Helper.Runtime", "LearningPathPolicy.cs");
        var queueStorePath = ResolveWorkspaceFile("src", "Helper.Runtime", "IndexingQueueStore.cs");
        var lifecyclePath = ResolveWorkspaceFile("src", "Helper.Runtime", "LearningLifecycleController.cs");
        var taskRunnerPath = ResolveWorkspaceFile("src", "Helper.Runtime", "SyntheticTaskRunner.cs");
        var service = File.ReadAllText(servicePath);
        var maxLines = ReadIntBudget("architecture", "services", "syntheticLearningService", "maxLines");
        var maxCollaborators = ReadIntBudget("architecture", "services", "syntheticLearningService", "maxCollaborators");
        var maxMembers = ReadIntBudget("architecture", "services", "syntheticLearningService", "maxMembers");

        Assert.True(File.Exists(pathPolicyPath));
        Assert.True(File.Exists(queueStorePath));
        Assert.True(File.Exists(lifecyclePath));
        Assert.True(File.Exists(taskRunnerPath));
        Assert.True(File.ReadAllLines(servicePath).Length <= maxLines, $"SyntheticLearningService should stay bounded, actual lines: {File.ReadAllLines(servicePath).Length}.");
        Assert.True(CountReadonlyFields(service) <= maxCollaborators, $"SyntheticLearningService exceeded collaborator budget: {CountReadonlyFields(service)} > {maxCollaborators}.");
        Assert.True(CountMemberLikeDeclarations(service) <= maxMembers, $"SyntheticLearningService exceeded member budget: {CountMemberLikeDeclarations(service)} > {maxMembers}.");
        Assert.Contains("ILearningPathPolicy", service, StringComparison.Ordinal);
        Assert.Contains("IIndexingQueueStore", service, StringComparison.Ordinal);
        Assert.Contains("ILearningLifecycleController", service, StringComparison.Ordinal);
        Assert.Contains("ISyntheticTaskRunner", service, StringComparison.Ordinal);
        Assert.DoesNotContain("HelperWorkspacePathResolver.ResolveHelperRoot()", service, StringComparison.Ordinal);
        Assert.DoesNotContain("new SemaphoreSlim(1, 1)", service, StringComparison.Ordinal);
        Assert.DoesNotContain("new FileStream(", service, StringComparison.Ordinal);
    }
}
