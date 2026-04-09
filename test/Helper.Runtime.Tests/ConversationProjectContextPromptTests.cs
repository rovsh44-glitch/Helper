using Helper.Api.Conversation;
using Helper.Api.Hosting;
using Helper.Runtime.Core;
using Moq;

namespace Helper.Runtime.Tests;

public sealed class ConversationProjectContextPromptTests
{
    [Fact]
    public async Task ConversationContextAssembler_Injects_ProjectContext_And_SharedUnderstanding_Blocks()
    {
        var reflection = new Mock<IReflectionService>(MockBehavior.Strict);
        var retrieval = new Mock<IRetrievalContextAssembler>(MockBehavior.Strict);
        var conversation = new ConversationState("conv-project-context")
        {
            ProjectContext = new ProjectContextState(
                "helper-public",
                "Helper Public",
                "Keep the public contract honest and prefer concrete remediation steps.",
                MemoryEnabled: false,
                new[] { "spec.md", "audit.json" },
                DateTimeOffset.UtcNow),
            SharedUnderstanding = new SharedUnderstandingState(
                PreferredInteractionMode: "guidance",
                PrefersDecisiveAction: true,
                AcceptedAssumptionsRecently: true,
                ClarificationWasUnhelpfulRecently: true,
                TemplateResistanceObserved: true,
                PrefersConciseReassurance: false,
                OverloadObservedRecently: false,
                FrustrationObservedRecently: false,
                UpdatedAtUtc: DateTimeOffset.UtcNow)
        };
        var assembler = new ConversationContextAssembler(reflection.Object, retrieval.Object, new ReasoningAwareRetrievalPolicy());
        var context = new ChatTurnContext
        {
            TurnId = "turn-project-context",
            Request = new ChatRequestDto("Summarize the next cleanup step.", conversation.Id, 8, null),
            Conversation = conversation,
            History = new[]
            {
                new ChatMessageDto("user", "Summarize the next cleanup step.", DateTimeOffset.UtcNow)
            },
            Intent = new IntentAnalysis(IntentType.Unknown, string.Empty),
            ExecutionMode = TurnExecutionMode.Fast
        };

        var assembly = await assembler.AssembleAsync(context, CancellationToken.None);

        Assert.Contains("Project context:", assembly.Prompt);
        Assert.Contains("Helper Public", assembly.Prompt);
        Assert.Contains("Reference artifacts: spec.md, audit.json", assembly.Prompt);
        Assert.Contains("Memory boundary: conversation_only", assembly.Prompt);
        Assert.Contains("Shared understanding:", assembly.Prompt);
        Assert.Contains("Avoid canned transitions", assembly.Prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("project_context", assembly.UsedLayers);
        Assert.Contains("shared_understanding", assembly.UsedLayers);
    }
}
