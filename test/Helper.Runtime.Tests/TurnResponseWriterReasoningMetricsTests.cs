using Helper.Api.Backend.Application;
using Helper.Api.Conversation;
using Helper.Api.Hosting;
using Helper.Runtime.Core;
using Moq;

namespace Helper.Runtime.Tests;

public class TurnResponseWriterReasoningMetricsTests
{
    [Fact]
    public void TurnResponseWriter_EmitsReasoningEfficiencyMetrics_FromTurnContext()
    {
        var store = new InMemoryConversationStore();
        var checkpointManager = new Mock<ITurnCheckpointManager>();
        var auditScheduler = new Mock<IPostTurnAuditScheduler>();
        auditScheduler
            .Setup(x => x.TrySchedule(It.IsAny<ChatTurnContext>(), It.IsAny<ChatResponseDto>()))
            .Returns(false);

        var writer = new TurnResponseWriter(
            store,
            checkpointManager.Object,
            auditScheduler.Object,
            new TurnLifecycleStateMachine(),
            new TurnExecutionStateMachine(),
            new TurnRouteTelemetryRecorder());

        var state = store.GetOrCreate("conv-reasoning-writer");
        var context = new ChatTurnContext
        {
            TurnId = "turn-reasoning-writer",
            Request = new ChatRequestDto("Return only JSON with status and count", state.Id, 10, null),
            Conversation = state,
            History = new[]
            {
                new ChatMessageDto("user", "Return only JSON with status and count", DateTimeOffset.UtcNow, "turn-reasoning-writer")
            },
            FinalResponse = "{\"status\":\"ok\",\"count\":3}",
            ExecutionOutput = "{\"status\":\"ok\",\"count\":3}",
            Intent = new IntentAnalysis(IntentType.Research, "test-model"),
            IntentConfidence = 0.83,
            ExecutionMode = TurnExecutionMode.Deep,
            BudgetProfile = TurnBudgetProfile.Research,
            Confidence = 0.88,
            LifecycleState = TurnLifecycleState.Finalize,
            ExecutionState = TurnExecutionState.Finalized,
            LocalVerificationAppliedCount = 3,
            LocalVerificationPassCount = 2,
            LocalVerificationRejectCount = 1,
            ReasoningBranchingApplied = true,
            ReasoningCandidatesGenerated = 2,
            ReasoningCandidatesRejected = 1,
            ReasoningModelCallsUsed = 2,
            ApproximateReasoningTokenCost = 190,
            RetrievalChunksUsed = 4,
            ProceduralLessonsUsed = 1,
            SelectedReasoningStrategy = "format_guard",
            EstimatedTokensGenerated = 60
        };

        var response = writer.PersistCompletedTurn(state, context, branchId: "main", turnVersion: 1);

        Assert.NotNull(response.ReasoningMetrics);
        Assert.True(response.ReasoningMetrics!.PathActive);
        Assert.True(response.ReasoningMetrics.BranchingApplied);
        Assert.Equal(2, response.ReasoningMetrics.BranchesExplored);
        Assert.Equal(1, response.ReasoningMetrics.CandidatesRejected);
        Assert.Equal(3, response.ReasoningMetrics.LocalVerificationChecks);
        Assert.Equal(2, response.ReasoningMetrics.LocalVerificationPasses);
        Assert.Equal(1, response.ReasoningMetrics.LocalVerificationRejects);
        Assert.Equal(2, response.ReasoningMetrics.ModelCallsUsed);
        Assert.Equal(4, response.ReasoningMetrics.RetrievalChunksUsed);
        Assert.Equal(1, response.ReasoningMetrics.ProceduralLessonsUsed);
        Assert.Equal(190, response.ReasoningMetrics.ApproximateTokenCost);
        Assert.Equal("format_guard", response.ReasoningMetrics.SelectedStrategy);
    }

    [Fact]
    public void TurnResponseWriter_EmitsAuditStatus_FromSchedulerDecision()
    {
        var store = new InMemoryConversationStore();
        var checkpointManager = new Mock<ITurnCheckpointManager>();
        var auditScheduler = new Mock<IPostTurnAuditScheduler>();
        auditScheduler
            .Setup(x => x.TrySchedule(It.IsAny<ChatTurnContext>(), It.IsAny<ChatResponseDto>()))
            .Callback<ChatTurnContext, ChatResponseDto>((ctx, _) =>
            {
                ctx.AuditEligible = true;
                ctx.AuditExpectedTrace = true;
                ctx.AuditStrictMode = true;
                ctx.AuditDecision = "scheduled";
                ctx.AuditOutstandingAtDecision = 0;
                ctx.AuditPendingAtDecision = 0;
                ctx.AuditMaxOutstandingAudits = 4;
            })
            .Returns(true);

        var writer = new TurnResponseWriter(
            store,
            checkpointManager.Object,
            auditScheduler.Object,
            new TurnLifecycleStateMachine(),
            new TurnExecutionStateMachine(),
            new TurnRouteTelemetryRecorder());

        var state = store.GetOrCreate("conv-audit-writer");
        var context = new ChatTurnContext
        {
            TurnId = "turn-audit-writer",
            Request = new ChatRequestDto("Research .NET observability with sources", state.Id, 10, null),
            Conversation = state,
            History = new[]
            {
                new ChatMessageDto("user", "Research .NET observability with sources", DateTimeOffset.UtcNow, "turn-audit-writer")
            },
            FinalResponse = "answer",
            ExecutionOutput = "answer",
            Intent = new IntentAnalysis(IntentType.Research, "test-model"),
            BudgetProfile = TurnBudgetProfile.Research,
            Confidence = 0.82,
            LifecycleState = TurnLifecycleState.Finalize,
            ExecutionState = TurnExecutionState.Finalized
        };

        var response = writer.PersistCompletedTurn(state, context, branchId: "main", turnVersion: 1);

        Assert.NotNull(response.AuditStatus);
        Assert.True(response.AuditStatus!.Eligible);
        Assert.True(response.AuditStatus.ExpectedTrace);
        Assert.True(response.AuditStatus.StrictMode);
        Assert.Equal("scheduled", response.AuditStatus.Decision);
        Assert.Equal(4, response.AuditStatus.MaxOutstandingAudits);
    }

    [Fact]
    public void TurnResponseWriter_EmitsStyleTelemetry_FromTurnContext()
    {
        var store = new InMemoryConversationStore();
        var checkpointManager = new Mock<ITurnCheckpointManager>();
        var auditScheduler = new Mock<IPostTurnAuditScheduler>();
        auditScheduler
            .Setup(x => x.TrySchedule(It.IsAny<ChatTurnContext>(), It.IsAny<ChatResponseDto>()))
            .Returns(false);

        var writer = new TurnResponseWriter(
            store,
            checkpointManager.Object,
            auditScheduler.Object,
            new TurnLifecycleStateMachine(),
            new TurnExecutionStateMachine(),
            new TurnRouteTelemetryRecorder());

        var state = store.GetOrCreate("conv-style-writer");
        var context = new ChatTurnContext
        {
            TurnId = "turn-style-writer",
            Request = new ChatRequestDto("remember: answer concise", state.Id, 10, null),
            Conversation = state,
            History = new[]
            {
                new ChatMessageDto("user", "remember: answer concise", DateTimeOffset.UtcNow, "turn-style-writer")
            },
            FinalResponse = "Understood. I will keep this preference in mind for this conversation: \"answer concise\".",
            ExecutionOutput = "Understood. I will keep this preference in mind for this conversation: \"answer concise\".",
            NextStep = "You can continue; I will apply that preference in this conversation.",
            GroundingStatus = "memory_captured",
            Confidence = 0.96,
            LifecycleState = TurnLifecycleState.Finalize,
            ExecutionState = TurnExecutionState.Finalized
        };
        context.Sources.Add("https://docs.example.org/conversation/preferences");

        var response = writer.PersistCompletedTurn(state, context, branchId: "main", turnVersion: 1);

        Assert.NotNull(response.StyleTelemetry);
        Assert.True(response.StyleTelemetry!.MemoryAckTemplateDetected);
        Assert.Equal("docs.example.org/conversation/preferences", response.StyleTelemetry.SourceFingerprint);
        Assert.False(response.StyleTelemetry.MixedLanguageDetected);
    }

    [Fact]
    public void TurnResponseWriter_EmitsSearchTrace_FromTurnContext()
    {
        var store = new InMemoryConversationStore();
        var checkpointManager = new Mock<ITurnCheckpointManager>();
        var auditScheduler = new Mock<IPostTurnAuditScheduler>();
        auditScheduler
            .Setup(x => x.TrySchedule(It.IsAny<ChatTurnContext>(), It.IsAny<ChatResponseDto>()))
            .Returns(false);

        var writer = new TurnResponseWriter(
            store,
            checkpointManager.Object,
            auditScheduler.Object,
            new TurnLifecycleStateMachine(),
            new TurnExecutionStateMachine(),
            new TurnRouteTelemetryRecorder());

        var state = store.GetOrCreate("conv-search-trace-writer");
        var context = new ChatTurnContext
        {
            TurnId = "turn-search-trace-writer",
            Request = new ChatRequestDto(
                "latest observability guidance",
                state.Id,
                10,
                null,
                LiveWebMode: "force_search",
                InputMode: "voice"),
            Conversation = state,
            History = new[]
            {
                new ChatMessageDto("user", "latest observability guidance", DateTimeOffset.UtcNow, "turn-search-trace-writer")
            },
            FinalResponse = "Here is the latest guidance.",
            ExecutionOutput = "Here is the latest guidance.",
            Intent = new IntentAnalysis(IntentType.Research, "test-model"),
            Confidence = 0.89,
            LifecycleState = TurnLifecycleState.Finalize,
            ExecutionState = TurnExecutionState.Finalized,
            ResolvedLiveWebRequirement = "web_required",
            ResolvedLiveWebReason = "user_forced_search"
        };
        context.LiveWebSignals.Add("user:force_search");
        context.IntentSignals.Add("web_search:live_fetch");
        context.ToolCalls.Add("research.search");
        context.RetrievalTrace.Add("web_search.iteration_count=2");
        context.ResearchEvidenceItems.Add(new ResearchEvidenceItem(
            1,
            "https://example.org/observability",
            "Observability guide",
            "Direct snippet from the fetched page.",
            EvidenceKind: "fetched_page",
            PublishedAt: "2026-03-21"));

        var response = writer.PersistCompletedTurn(state, context, branchId: "main", turnVersion: 1);

        Assert.NotNull(response.SearchTrace);
        Assert.Equal("force_search", response.SearchTrace!.RequestedMode);
        Assert.Equal("web_required", response.SearchTrace.ResolvedRequirement);
        Assert.Equal("executed_live_web", response.SearchTrace.Status);
        Assert.Equal("voice", response.SearchTrace.InputMode);
        Assert.Equal("voice", response.InputMode);
        Assert.Contains("user:force_search", response.SearchTrace.Signals!);
        Assert.Contains("web_search.iteration_count=2", response.SearchTrace.Events!);
        Assert.Single(response.SearchTrace.Sources!);
        Assert.Equal("Observability guide", response.SearchTrace.Sources![0].Title);
    }

    [Fact]
    public void TurnResponseWriter_Emits_Epistemic_And_Interaction_Metadata()
    {
        var store = new InMemoryConversationStore();
        var checkpointManager = new Mock<ITurnCheckpointManager>();
        var auditScheduler = new Mock<IPostTurnAuditScheduler>();
        auditScheduler
            .Setup(x => x.TrySchedule(It.IsAny<ChatTurnContext>(), It.IsAny<ChatResponseDto>()))
            .Returns(false);

        var writer = new TurnResponseWriter(
            store,
            checkpointManager.Object,
            auditScheduler.Object,
            new TurnLifecycleStateMachine(),
            new TurnExecutionStateMachine(),
            new TurnRouteTelemetryRecorder());

        var state = store.GetOrCreate("conv-epistemic-interaction-writer");
        var context = new ChatTurnContext
        {
            TurnId = "turn-epistemic-interaction-writer",
            Request = new ChatRequestDto("Help me sanity-check this medical claim", state.Id, 10, null),
            Conversation = state,
            History = Array.Empty<ChatMessageDto>(),
            FinalResponse = "I will avoid a strong factual claim here.",
            ExecutionOutput = "I will avoid a strong factual claim here.",
            Confidence = 0.34,
            LifecycleState = TurnLifecycleState.Finalize,
            ExecutionState = TurnExecutionState.Finalized,
            RepairDriver = "interaction",
            EpistemicAnswerMode = Helper.Api.Conversation.Epistemic.EpistemicAnswerMode.Abstain,
            EpistemicRiskSnapshot = new Helper.Api.Conversation.Epistemic.EpistemicRiskSnapshot(
                GroundingStatus: "unverified",
                CitationCoverage: 0.15,
                VerifiedClaimRatio: 0.0,
                HasContradictions: false,
                HasWeakEvidence: true,
                HighRiskDomain: true,
                FreshnessSensitive: true,
                CurrentConfidence: 0.34,
                ConfidenceCeiling: 0.42,
                CalibrationThreshold: 0.78,
                AbstentionRecommended: true,
                Trace: new[] { "epistemic.abstention_recommended=true" }),
            InteractionState = new Helper.Api.Conversation.InteractionState.InteractionStateSnapshot(
                FrustrationLevel: Helper.Api.Conversation.InteractionState.InteractionSignalLevel.Moderate,
                UrgencyLevel: Helper.Api.Conversation.InteractionState.InteractionSignalLevel.Low,
                OverloadRisk: Helper.Api.Conversation.InteractionState.InteractionSignalLevel.Low,
                ReassuranceNeed: Helper.Api.Conversation.InteractionState.InteractionSignalLevel.High,
                ClarificationToleranceShift: -1,
                AssistantPressureRisk: Helper.Api.Conversation.InteractionState.InteractionSignalLevel.Moderate,
                Signals: new[] { "interaction.reassurance:lexical" })
        };

        var response = writer.PersistCompletedTurn(state, context, branchId: "main", turnVersion: 1);

        Assert.Equal("abstain", response.EpistemicAnswerMode);
        Assert.Equal("interaction", response.RepairDriver);
        Assert.NotNull(response.EpistemicRisk);
        Assert.Equal("abstain", response.EpistemicRisk!.AnswerMode);
        Assert.True(response.EpistemicRisk.AbstentionRecommended);
        Assert.NotNull(response.InteractionState);
        Assert.Equal("high", response.InteractionState!.ReassuranceNeed);
    }
}

