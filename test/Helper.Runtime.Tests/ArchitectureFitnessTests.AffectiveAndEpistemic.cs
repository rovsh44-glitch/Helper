namespace Helper.Runtime.Tests;

public partial class ArchitectureFitnessTests
{
    [Fact]
    public void Epistemic_And_InteractionState_Modules_Stay_Bounded_And_Acyclic()
    {
        var epistemicRoot = ResolveWorkspaceFile("src", "Helper.Api", "Conversation", "Epistemic");
        var interactionRoot = ResolveWorkspaceFile("src", "Helper.Api", "Conversation", "InteractionState");
        var behavioralPolicyPath = ResolveWorkspaceFile("src", "Helper.Api", "Conversation", "Epistemic", "BehavioralCalibrationPolicy.cs");
        var modePolicyPath = ResolveWorkspaceFile("src", "Helper.Api", "Conversation", "Epistemic", "EpistemicAnswerModePolicy.cs");
        var interactionAnalyzerPath = ResolveWorkspaceFile("src", "Helper.Api", "Conversation", "InteractionState", "InteractionStateAnalyzer.cs");
        var interactionProjectorPath = ResolveWorkspaceFile("src", "Helper.Api", "Conversation", "InteractionState", "InteractionPolicyProjector.cs");

        var interactionFiles = Directory.GetFiles(interactionRoot, "*.cs", SearchOption.TopDirectoryOnly)
            .Select(File.ReadAllText)
            .ToArray();
        var epistemicFiles = Directory.GetFiles(epistemicRoot, "*.cs", SearchOption.TopDirectoryOnly)
            .Select(File.ReadAllText)
            .ToArray();

        Assert.True(File.ReadAllLines(behavioralPolicyPath).Length <= ReadIntBudget("architecture", "services", "behavioralCalibrationPolicy", "maxLines"));
        Assert.True(CountMemberLikeDeclarations(File.ReadAllText(behavioralPolicyPath)) <= ReadIntBudget("architecture", "services", "behavioralCalibrationPolicy", "maxMembers"));
        Assert.True(File.ReadAllLines(interactionAnalyzerPath).Length <= ReadIntBudget("architecture", "services", "interactionStateAnalyzer", "maxLines"));
        Assert.True(CountMemberLikeDeclarations(File.ReadAllText(interactionAnalyzerPath)) <= ReadIntBudget("architecture", "services", "interactionStateAnalyzer", "maxMembers"));
        Assert.True(File.ReadAllLines(interactionProjectorPath).Length <= ReadIntBudget("architecture", "services", "interactionPolicyProjector", "maxLines"));
        Assert.True(CountMemberLikeDeclarations(File.ReadAllText(interactionProjectorPath)) <= ReadIntBudget("architecture", "services", "interactionPolicyProjector", "maxMembers"));

        Assert.All(interactionFiles, source =>
        {
            Assert.DoesNotContain("using Helper.Api.Conversation.Epistemic;", source, StringComparison.Ordinal);
            Assert.DoesNotContain("EpistemicAnswerMode", source, StringComparison.Ordinal);
            Assert.DoesNotContain("BehavioralCalibrationPolicy", source, StringComparison.Ordinal);
            Assert.DoesNotContain("EpistemicAnswerModePolicy", source, StringComparison.Ordinal);
        });

        Assert.All(epistemicFiles, source =>
        {
            Assert.DoesNotContain("using Helper.Api.Conversation.InteractionState;", source, StringComparison.Ordinal);
            Assert.DoesNotContain("InteractionStateSnapshot", source, StringComparison.Ordinal);
            Assert.DoesNotContain("InteractionPolicyProjection", source, StringComparison.Ordinal);
            Assert.DoesNotContain("InteractionStateAnalyzer", source, StringComparison.Ordinal);
        });

        Assert.DoesNotContain("ClaimExtractionService", string.Join("\n", interactionFiles), StringComparison.Ordinal);
        Assert.DoesNotContain("ClaimSourceMatcher", string.Join("\n", interactionFiles), StringComparison.Ordinal);
        Assert.DoesNotContain("EvidenceGradingService", string.Join("\n", interactionFiles), StringComparison.Ordinal);
        Assert.DoesNotContain("CitationGroundingService", string.Join("\n", interactionFiles), StringComparison.Ordinal);
        Assert.DoesNotContain("ClaimExtractionService", string.Join("\n", epistemicFiles), StringComparison.Ordinal);
        Assert.DoesNotContain("ClaimSourceMatcher", string.Join("\n", epistemicFiles), StringComparison.Ordinal);
        Assert.DoesNotContain("EvidenceGradingService", string.Join("\n", epistemicFiles), StringComparison.Ordinal);
        Assert.DoesNotContain("CitationGroundingService", string.Join("\n", epistemicFiles), StringComparison.Ordinal);
        Assert.Contains("EpistemicAnswerMode", File.ReadAllText(modePolicyPath), StringComparison.Ordinal);
    }

    [Fact]
    public void ChatTurnFinalizer_Remains_Coordinator_For_Epistemic_Decisions()
    {
        var finalizerPath = ResolveWorkspaceFile("src", "Helper.Api", "Conversation", "ChatTurnFinalizer.cs");
        var finalizer = File.ReadAllText(finalizerPath);

        Assert.True(File.ReadAllLines(finalizerPath).Length <= ReadIntBudget("architecture", "services", "chatTurnFinalizer", "maxLines"));
        Assert.True(CountReadonlyFields(finalizer) <= ReadIntBudget("architecture", "services", "chatTurnFinalizer", "maxCollaborators"));
        Assert.True(CountMemberLikeDeclarations(finalizer) <= ReadIntBudget("architecture", "services", "chatTurnFinalizer", "maxMembers"));
        Assert.Contains("IBehavioralCalibrationPolicy", finalizer, StringComparison.Ordinal);
        Assert.Contains("IEpistemicAnswerModePolicy", finalizer, StringComparison.Ordinal);
        Assert.DoesNotContain("HighRiskTokens", finalizer, StringComparison.Ordinal);
        Assert.DoesNotContain("FreshnessTokens", finalizer, StringComparison.Ordinal);
        Assert.DoesNotContain("DetectFrustration(", finalizer, StringComparison.Ordinal);
        Assert.DoesNotContain("DetectUrgency(", finalizer, StringComparison.Ordinal);
        Assert.DoesNotContain("DetectReassuranceNeed(", finalizer, StringComparison.Ordinal);
        Assert.DoesNotContain("ClaimExtractionService", finalizer, StringComparison.Ordinal);
        Assert.DoesNotContain("ClaimSourceMatcher", finalizer, StringComparison.Ordinal);
        Assert.DoesNotContain("EvidenceGradingService", finalizer, StringComparison.Ordinal);
        Assert.DoesNotContain("CitationGroundingService(", finalizer, StringComparison.Ordinal);
    }
}
