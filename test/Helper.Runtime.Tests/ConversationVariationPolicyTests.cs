using Helper.Api.Conversation;
using Helper.Api.Hosting;
using Helper.Runtime.Core;

namespace Helper.Runtime.Tests;

public sealed class ConversationVariationPolicyTests
{
    [Fact]
    public void ConversationVariationPolicy_AvoidsRecentAssistantFingerprint_WhenAlternativesExist()
    {
        var policy = new ConversationVariationPolicy();
        var context = new ChatTurnContext
        {
            TurnId = "variation-header-turn",
            Request = new ChatRequestDto("summarize run", "conv-variation", 12, null),
            Conversation = new ConversationState("conv-variation"),
            History = new[]
            {
                new ChatMessageDto("assistant", "Execution Summary:\nturn=prev", DateTimeOffset.UtcNow.AddMinutes(-1))
            }
        };

        var choice = policy.Select(
            DialogAct.Summarize,
            VariationSlot.OperatorSummaryHeader,
            context,
            new[] { "Execution Summary:", "Run Summary:", "What Happened:" });

        Assert.NotEqual("Execution Summary:", choice);
    }

    [Fact]
    public void DialogActPlanner_SelectsUncertaintyAndNextStep_ForSoftBestEffortEvidenceTurn()
    {
        var planner = new DialogActPlanner();
        var context = new ChatTurnContext
        {
            TurnId = "dialog-act-evidence",
            Request = new ChatRequestDto("help", "conv-dialog-act", 12, null),
            Conversation = new ConversationState("conv-dialog-act"),
            History = Array.Empty<ChatMessageDto>(),
            ForceBestEffort = true,
            NextStep = "If you want, I can turn this into a checklist."
        };
        context.Sources.Add("https://example.org/source");

        var plan = planner.BuildPlan(context, ResponseCompositionMode.EvidenceBrief, "Short answer");

        Assert.Equal(DialogAct.Summarize, plan.PrimaryAct);
        Assert.Contains(DialogAct.UncertaintyAck, plan.Acts);
        Assert.Contains(DialogAct.NextStep, plan.Acts);
    }

    [Fact]
    public void ResponseComposer_VariesOperationalSummaryHeader_WhenHistoryContainsPreviousHeader()
    {
        var composer = ResponseComposerServiceFactory.CreateDefault();
        var context = new ChatTurnContext
        {
            TurnId = "turn-op-variation",
            Request = new ChatRequestDto("generate project", null, 12, null),
            Conversation = new ConversationState("conv-op-variation"),
            History = new[]
            {
                new ChatMessageDto("assistant", "Execution Summary:\nturn=prev", DateTimeOffset.UtcNow.AddMinutes(-1))
            },
            Intent = new IntentAnalysis(IntentType.Generate, "test"),
            ExecutionMode = TurnExecutionMode.Balanced,
            Confidence = 0.72,
            NextStep = "Open the generated project and run build."
        };
        context.ToolCalls.Add("helper.generate");

        var result = composer.Compose(context, "Project successfully generated at: D:\\PROJECTS\\calc");
        var firstLine = result.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n')[0];

        Assert.NotEqual("Execution Summary:", firstLine);
        Assert.Contains(firstLine, new[] { "Run Summary:", "What Happened:" });
    }

    [Fact]
    public void ChatTurnExecutionSupport_VariesDeterministicMemoryCapture_WhenHistoryContainsPriorAckTemplate()
    {
        var context = new ChatTurnContext
        {
            TurnId = "memory-variation-turn",
            Request = new ChatRequestDto("[1] remember: answer concise", "conv-memory-variation", 10, null),
            Conversation = new ConversationState("conv-memory-variation"),
            History = new[]
            {
                new ChatMessageDto("assistant", "Understood. I will keep this preference in mind for this conversation: \"answer concise\".", DateTimeOffset.UtcNow.AddMinutes(-1))
            },
            Intent = new IntentAnalysis(IntentType.Unknown, "test-model")
        };

        var applied = ChatTurnExecutionSupport.TryApplyDeterministicMemoryCapture(context, new ConversationVariationPolicy());

        Assert.True(applied);
        Assert.Contains("answer concise", context.ExecutionOutput, StringComparison.OrdinalIgnoreCase);
        Assert.False(context.ExecutionOutput.StartsWith("Understood. I will keep this preference in mind for this conversation", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("memory_captured", context.GroundingStatus);
    }

    [Fact]
    public void ConversationVariationPolicy_PrefersConciseFormalMemoryAckVariants_ForConciseFormalProfile()
    {
        var policy = new ConversationVariationPolicy();
        var context = new ChatTurnContext
        {
            TurnId = "memory-formal-concise",
            Request = new ChatRequestDto("remember: keep answers concise", "conv-memory-formal-concise", 10, null),
            Conversation = new ConversationState("conv-memory-formal-concise")
            {
                PreferredLanguage = "en",
                DetailLevel = "concise",
                Formality = "formal"
            },
            History = Array.Empty<ChatMessageDto>()
        };

        var lead = MemoryAcknowledgementCatalog.SelectLead(policy, context, isRussian: false);
        var nextStep = MemoryAcknowledgementCatalog.SelectNextStep(policy, context, isRussian: false);

        Assert.Equal("Understood. I will keep that in mind", lead);
        Assert.Equal("Please continue; I will keep that preference active in this conversation.", nextStep);
    }

    [Fact]
    public void ChatTurnExecutionSupport_UsesShortMemoryAcknowledgement_ForConciseCasualProfile()
    {
        var context = new ChatTurnContext
        {
            TurnId = "memory-concise-casual",
            Request = new ChatRequestDto("remember: answer concise", "conv-memory-concise-casual", 10, null),
            Conversation = new ConversationState("conv-memory-concise-casual")
            {
                PreferredLanguage = "en",
                DetailLevel = "concise",
                Formality = "casual"
            },
            History = Array.Empty<ChatMessageDto>(),
            Intent = new IntentAnalysis(IntentType.Unknown, "test-model")
        };

        var applied = ChatTurnExecutionSupport.TryApplyDeterministicMemoryCapture(context, new ConversationVariationPolicy());

        Assert.True(applied);
        Assert.StartsWith("Got it. I'll keep that in mind:", context.ExecutionOutput, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Go ahead; I'll keep that preference active.", context.NextStep);
    }

    [Fact]
    public void ConversationStyleTelemetryAnalyzer_DetectsMemoryAckTemplate_ForNewShortVariant()
    {
        var analyzer = new ConversationStyleTelemetryAnalyzer();
        var context = new ChatTurnContext
        {
            TurnId = "memory-telemetry-short",
            Request = new ChatRequestDto("remember: answer concise", "conv-memory-telemetry-short", 10, null),
            Conversation = new ConversationState("conv-memory-telemetry-short")
            {
                PreferredLanguage = "en",
                DetailLevel = "concise",
                Formality = "casual"
            },
            History = Array.Empty<ChatMessageDto>(),
            FinalResponse = "Got it. I'll keep that in mind: \"answer concise\".",
            ExecutionOutput = "Got it. I'll keep that in mind: \"answer concise\".",
            NextStep = "Go ahead; I'll keep that preference active.",
            GroundingStatus = "memory_captured"
        };

        var telemetry = analyzer.Analyze(context);

        Assert.True(telemetry.MemoryAckTemplateDetected);
    }
}


