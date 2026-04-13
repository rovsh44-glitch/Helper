namespace Helper.Runtime.Tests;

public partial class ArchitectureFitnessTests
{
    [Fact]
    public void Phase5_StructuralCleanup_Reduces_Hotspots_And_Broad_Nullability_Suppression()
    {
        var conversationRoot = new FileInfo(ResolveWorkspaceFile("test", "Helper.Runtime.Tests", "ConversationRuntimeTests.cs"));
        var conversationResponse = new FileInfo(ResolveWorkspaceFile("test", "Helper.Runtime.Tests", "ConversationRuntimeTests.ResponseComposerAndFinalizer.cs"));
        var conversationPlanning = new FileInfo(ResolveWorkspaceFile("test", "Helper.Runtime.Tests", "ConversationRuntimeTests.PlanningAndMetrics.cs"));
        var conversationOrchestration = new FileInfo(ResolveWorkspaceFile("test", "Helper.Runtime.Tests", "ConversationRuntimeTests.OrchestrationAndExecution.cs"));
        var retrievalRoot = new FileInfo(ResolveWorkspaceFile("test", "Helper.Runtime.Tests", "RetrievalPipelineTests.cs"));
        var retrievalFoundation = new FileInfo(ResolveWorkspaceFile("test", "Helper.Runtime.Tests", "RetrievalPipelineTests.Foundation.cs"));
        var retrievalRoutingA = new FileInfo(ResolveWorkspaceFile("test", "Helper.Runtime.Tests", "RetrievalPipelineTests.GlobalRoutingA.cs"));
        var retrievalReranking = new FileInfo(ResolveWorkspaceFile("test", "Helper.Runtime.Tests", "RetrievalPipelineTests.DomainReranking.cs"));
        var retrievalRoutingB = new FileInfo(ResolveWorkspaceFile("test", "Helper.Runtime.Tests", "RetrievalPipelineTests.GlobalRoutingB.cs"));
        var hostingFiles = Directory.GetFiles(ResolveWorkspaceFile("src", "Helper.Api", "Hosting"), "*.cs", SearchOption.TopDirectoryOnly);

        Assert.True(conversationRoot.Length < 16_000, $"Expected split root ConversationRuntimeTests.cs to stay under 16 KB, got {conversationRoot.Length}.");
        Assert.True(retrievalRoot.Length < 4_000, $"Expected split root RetrievalPipelineTests.cs to stay under 4 KB, got {retrievalRoot.Length}.");

        Assert.True(conversationResponse.Length > 20_000);
        Assert.True(conversationPlanning.Length > 20_000);
        Assert.True(conversationOrchestration.Length > 20_000);
        Assert.True(retrievalFoundation.Length > 10_000);
        Assert.True(retrievalRoutingA.Length > 10_000);
        Assert.True(retrievalReranking.Length > 20_000);
        Assert.True(retrievalRoutingB.Length > 10_000);

        Assert.Contains("public partial class ConversationRuntimeTests", File.ReadAllText(conversationRoot.FullName), StringComparison.Ordinal);
        Assert.Contains("public partial class RetrievalPipelineTests", File.ReadAllText(retrievalRoot.FullName), StringComparison.Ordinal);

        foreach (var hostingFile in hostingFiles)
        {
            var text = File.ReadAllText(hostingFile);
            Assert.DoesNotContain("CS8600, CS8601, CS8602, CS8603, CS8604, CS8619, CS8622, CS8632", text, StringComparison.Ordinal);

            if (text.Contains("#pragma warning disable", StringComparison.Ordinal))
            {
                Assert.Contains("CS8600, CS8619, CS8622", text, StringComparison.Ordinal);
            }
        }

        Assert.True(File.Exists(ResolveWorkspaceFile("src", "SelfEvolvingAI.Infrastructure", "README.md")));
        Assert.True(File.Exists(ResolveWorkspaceFile("src", "SelfEvolvingAI.Infrastructure.Core", "README.md")));
    }
}
