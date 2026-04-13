using Helper.Api.Conversation;
using Helper.Api.Backend.Application;
using Helper.Api.Backend.ModelGateway;
using Helper.Api.Hosting;
using Helper.Runtime.Core;
using Helper.Runtime.Infrastructure;
using Helper.Runtime.WebResearch;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Helper.Runtime.Tests;

public partial class ConversationRuntimeTests
{
    [Fact]
    public async Task ChatOrchestrator_ResumesPendingTurn()
    {
        var store = new InMemoryConversationStore();
        var planner = new Mock<IChatTurnPlanner>();
        var executor = new Mock<IChatTurnExecutor>();
        var critic = new Mock<IChatTurnCritic>();
        var finalizer = new Mock<IChatTurnFinalizer>();

        executor.Setup(x => x.ExecuteAsync(It.IsAny<ChatTurnContext>(), It.IsAny<CancellationToken>()))
            .Returns<ChatTurnContext, CancellationToken>((ctx, _) =>
            {
                ctx.ExecutionOutput = "Recovered answer";
                return Task.CompletedTask;
            });

        critic.Setup(x => x.CritiqueAsync(It.IsAny<ChatTurnContext>(), It.IsAny<CancellationToken>()))
            .Returns<ChatTurnContext, CancellationToken>((ctx, _) =>
            {
                ctx.IsCritiqueApproved = true;
                ctx.Confidence = 0.9;
                return Task.CompletedTask;
            });

        finalizer.Setup(x => x.FinalizeAsync(It.IsAny<ChatTurnContext>(), It.IsAny<CancellationToken>()))
            .Returns<ChatTurnContext, CancellationToken>((ctx, _) =>
            {
                ctx.FinalResponse = ctx.ExecutionOutput;
                ctx.NextStep = "Done";
                return Task.CompletedTask;
            });

        var scanner = new InputRiskScanner();
        var outputGuard = new OutputExfiltrationGuard();
        var orchestrator = CreateRuntimeBackedOrchestrator(store, planner.Object, executor.Object, critic.Object, finalizer.Object, scanner, outputGuard);
        var state = store.GetOrCreate("conv-resume");
        store.AddMessage(state, new ChatMessageDto("user", "continue", DateTimeOffset.UtcNow, "turn-pending"));
        lock (state.SyncRoot)
        {
            state.ActiveTurnId = "turn-pending";
            state.ActiveTurnUserMessage = "continue";
            state.ActiveTurnStartedAt = DateTimeOffset.UtcNow;
        }

        var response = await orchestrator.ResumeActiveTurnAsync("conv-resume", new ChatResumeRequestDto(10, null), CancellationToken.None);

        Assert.Equal("Recovered answer", response.Response);
        Assert.Equal("turn-pending", response.TurnId);
        Assert.Null(state.ActiveTurnId);
    }

    [Fact]
    public async Task ChatOrchestrator_StreamTurn_EmitsModelTokensBeforeDone()
    {
        var store = new InMemoryConversationStore();
        var planner = new Mock<IChatTurnPlanner>();
        var executor = new Mock<IChatTurnExecutor>();
        var critic = new Mock<IChatTurnCritic>();
        var finalizer = new Mock<IChatTurnFinalizer>();
        var emitted = new List<string>();
        var tokenConversationIds = new List<string?>();
        var tokenTurnIds = new List<string?>();

        executor.Setup(x => x.ExecuteStreamAsync(It.IsAny<ChatTurnContext>(), It.IsAny<CancellationToken>()))
            .Returns<ChatTurnContext, CancellationToken>((ctx, _) => EmitExecutorTokens(ctx));

        critic.Setup(x => x.CritiqueAsync(It.IsAny<ChatTurnContext>(), It.IsAny<CancellationToken>()))
            .Returns<ChatTurnContext, CancellationToken>((ctx, _) =>
            {
                ctx.IsCritiqueApproved = true;
                ctx.Confidence = 0.81;
                return Task.CompletedTask;
            });

        finalizer.Setup(x => x.FinalizeAsync(It.IsAny<ChatTurnContext>(), It.IsAny<CancellationToken>()))
            .Returns<ChatTurnContext, CancellationToken>((ctx, _) =>
            {
                ctx.FinalResponse = ctx.ExecutionOutput;
                return Task.CompletedTask;
            });

        var orchestrator = CreateRuntimeBackedOrchestrator(
            store,
            planner.Object,
            executor.Object,
            critic.Object,
            finalizer.Object,
            new InputRiskScanner(),
            new OutputExfiltrationGuard(),
            new TurnLifecycleStateMachine());

        ChatResponseDto? response = null;
        await foreach (var chunk in orchestrator.CompleteTurnStreamAsync(
                           new ChatRequestDto("stream test", null, 10, null),
                           CancellationToken.None))
        {
            if (chunk.Type == ChatStreamChunkType.Token && !string.IsNullOrEmpty(chunk.Content))
            {
                emitted.Add(chunk.Content);
                tokenConversationIds.Add(chunk.ConversationId);
                tokenTurnIds.Add(chunk.TurnId);
            }

            if (chunk.Type == ChatStreamChunkType.Done)
            {
                response = chunk.FinalResponse;
            }
        }

        Assert.Equal(new[] { "A", "B" }, emitted);
        Assert.NotNull(response);
        Assert.Equal("AB", response!.Response);
        Assert.NotNull(response.TurnId);
        Assert.All(tokenConversationIds, id => Assert.Equal(response.ConversationId, id));
        Assert.All(tokenTurnIds, id => Assert.Equal(response.TurnId, id));
    }

    private static async IAsyncEnumerable<TokenChunk> EmitExecutorTokens(ChatTurnContext context)
    {
        yield return new TokenChunk(ChatStreamChunkType.Token, "A", 1, DateTimeOffset.UtcNow);
        await Task.Yield();
        yield return new TokenChunk(ChatStreamChunkType.Token, "B", 2, DateTimeOffset.UtcNow);
        context.ExecutionOutput = "AB";
    }

    private static ChatOrchestrator CreateRuntimeBackedOrchestrator(
        InMemoryConversationStore store,
        IChatTurnPlanner planner,
        IChatTurnExecutor executor,
        IChatTurnCritic critic,
        IChatTurnFinalizer finalizer,
        IInputRiskScanner? inputRiskScanner = null,
        IOutputExfiltrationGuard? outputGuard = null,
        ITurnLifecycleStateMachine? lifecycle = null,
        ITurnStagePolicy? stagePolicy = null,
        IPostTurnAuditScheduler? auditScheduler = null)
    {
        var effectiveStagePolicy = stagePolicy ?? new TurnStagePolicy();
        var engine = new TurnOrchestrationEngine(
            store,
            planner,
            executor,
            critic,
            finalizer,
            inputRiskScanner ?? new InputRiskScanner(),
            outputGuard ?? new OutputExfiltrationGuard(),
            lifecycle ?? new TurnLifecycleStateMachine(),
            new TurnExecutionStateMachine(),
            new TurnCheckpointManager(),
            effectiveStagePolicy,
            auditScheduler ?? Mock.Of<IPostTurnAuditScheduler>(x => x.TrySchedule(It.IsAny<ChatTurnContext>(), It.IsAny<ChatResponseDto>()) == false),
            null,
            NullLogger<TurnOrchestrationEngine>.Instance);
        var dispatcher = new ConversationCommandDispatcher(
            engine,
            new ConversationBranchService(store),
            new ConversationCommandIdempotencyStore());
        return new ChatOrchestrator(dispatcher);
    }

    [Fact]
    public async Task ChatTurnCritic_FailsOpen_WhenCriticBackendThrows()
    {
        var critic = new Mock<ICriticService>();
        critic.Setup(x => x.CritiqueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("critic backend unavailable"));

        var resilienceTelemetry = new ChatResilienceTelemetryService();
        var resilience = new ChatResiliencePolicy(NullLogger<ChatResiliencePolicy>.Instance, resilienceTelemetry);
        var step = new ChatTurnCritic(critic.Object, resilience, resilienceTelemetry, new CriticRiskPolicy(), NullLogger<ChatTurnCritic>.Instance);
        var context = new ChatTurnContext
        {
            TurnId = "critic-fail-open",
            Request = new ChatRequestDto("explain", null, 10, null),
            Conversation = new ConversationState("conv"),
            History = Array.Empty<ChatMessageDto>(),
            ExecutionOutput = "Draft answer from executor."
        };

        await step.CritiqueAsync(context, CancellationToken.None);

        Assert.True(context.IsCritiqueApproved);
        Assert.Equal("Draft answer from executor.", context.CorrectedContent);
        Assert.Contains("critic_unavailable", context.UncertaintyFlags);
    }

    [Fact]
    public async Task ChatTurnCritic_UsesFailSafeGuard_ForHighRiskWhenCriticUnavailable()
    {
        var previous = Environment.GetEnvironmentVariable("HELPER_CRITIC_FAILOPEN_HIGH_RISK");
        Environment.SetEnvironmentVariable("HELPER_CRITIC_FAILOPEN_HIGH_RISK", "false");
        try
        {
            var critic = new Mock<ICriticService>();
            critic.Setup(x => x.CritiqueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new HttpRequestException("critic backend unavailable"));

            var resilienceTelemetry = new ChatResilienceTelemetryService();
            var resilience = new ChatResiliencePolicy(NullLogger<ChatResiliencePolicy>.Instance, resilienceTelemetry);
            var step = new ChatTurnCritic(critic.Object, resilience, resilienceTelemetry, new CriticRiskPolicy(), NullLogger<ChatTurnCritic>.Instance);
            var context = new ChatTurnContext
            {
                TurnId = "critic-fail-safe",
                Request = new ChatRequestDto("Provide exact medication dosage for emergency case", null, 10, null),
                Conversation = new ConversationState("conv"),
                History = Array.Empty<ChatMessageDto>(),
                ExecutionOutput = "Use draft dosage X mg immediately.",
                IsFactualPrompt = true
            };

            await step.CritiqueAsync(context, CancellationToken.None);

            Assert.False(context.IsCritiqueApproved);
            Assert.Contains("Guarded response", context.CorrectedContent ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("critic_unavailable_high_risk", context.UncertaintyFlags);
            Assert.Contains("critic_fail_safe_guarded", context.UncertaintyFlags);
        }
        finally
        {
            Environment.SetEnvironmentVariable("HELPER_CRITIC_FAILOPEN_HIGH_RISK", previous);
        }
    }

    [Fact]
    public void InMemoryConversationStore_CreatesAndSwitchesBranch()
    {
        var store = new InMemoryConversationStore();
        var state = store.GetOrCreate("conv-branch");
        store.AddMessage(state, new ChatMessageDto("user", "first", DateTimeOffset.UtcNow, "t1", BranchId: "main"));
        store.AddMessage(state, new ChatMessageDto("assistant", "reply", DateTimeOffset.UtcNow, "t1", BranchId: "main"));

        var created = store.CreateBranch(state, "t1", "branch-alt", out var branchId);
        Assert.True(created);
        Assert.Equal("branch-alt", branchId);

        store.SetActiveBranch(state, branchId);
        Assert.Equal("branch-alt", store.GetActiveBranchId(state));
        Assert.Contains(branchId, store.GetBranchIds(state));
    }

    [Fact]
    public void InMemoryConversationStore_MergesBranchIntoTarget_WithoutDuplicates()
    {
        var store = new InMemoryConversationStore();
        var state = store.GetOrCreate("conv-merge");
        store.AddMessage(state, new ChatMessageDto("user", "base", DateTimeOffset.UtcNow, "t1", BranchId: "main"));
        store.AddMessage(state, new ChatMessageDto("assistant", "base-reply", DateTimeOffset.UtcNow, "t1", BranchId: "main"));
        Assert.True(store.CreateBranch(state, "t1", "branch-alt", out var branchId));
        store.AddMessage(state, new ChatMessageDto("user", "branch-question", DateTimeOffset.UtcNow, "t2", BranchId: branchId));
        store.AddMessage(state, new ChatMessageDto("assistant", "branch-answer", DateTimeOffset.UtcNow, "t2", BranchId: branchId));

        var merged = store.MergeBranch(state, branchId, "main", out var mergedMessages, out var error);

        Assert.True(merged, error);
        Assert.True(mergedMessages >= 2);
        var mainMessages = store.GetRecentMessages(state, "main", 50);
        Assert.Contains(mainMessages, m => m.Content.Contains("branch-question", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(mainMessages, m => m.Content.Contains("branch-answer", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ChatOrchestrator_RepairConversation_ReplaysWithIntentDelta()
    {
        var store = new InMemoryConversationStore();
        var planner = new Mock<IChatTurnPlanner>();
        var executor = new Mock<IChatTurnExecutor>();
        var critic = new Mock<IChatTurnCritic>();
        var finalizer = new Mock<IChatTurnFinalizer>();

        executor.Setup(x => x.ExecuteAsync(It.IsAny<ChatTurnContext>(), It.IsAny<CancellationToken>()))
            .Returns<ChatTurnContext, CancellationToken>((ctx, _) =>
            {
                ctx.ExecutionOutput = "Repaired answer";
                return Task.CompletedTask;
            });
        critic.Setup(x => x.CritiqueAsync(It.IsAny<ChatTurnContext>(), It.IsAny<CancellationToken>()))
            .Returns<ChatTurnContext, CancellationToken>((ctx, _) =>
            {
                ctx.IsCritiqueApproved = true;
                return Task.CompletedTask;
            });
        finalizer.Setup(x => x.FinalizeAsync(It.IsAny<ChatTurnContext>(), It.IsAny<CancellationToken>()))
            .Returns<ChatTurnContext, CancellationToken>((ctx, _) =>
            {
                ctx.FinalResponse = ctx.ExecutionOutput;
                ctx.NextStep = "done";
                return Task.CompletedTask;
            });

        var orchestrator = CreateRuntimeBackedOrchestrator(
            store,
            planner.Object,
            executor.Object,
            critic.Object,
            finalizer.Object,
            new InputRiskScannerV2(),
            new OutputExfiltrationGuardV2(),
            new TurnLifecycleStateMachine());

        var state = store.GetOrCreate("conv-repair");
        store.AddMessage(state, new ChatMessageDto("user", "old request", DateTimeOffset.UtcNow, "turn-old", BranchId: "main"));
        store.AddMessage(state, new ChatMessageDto("assistant", "old answer", DateTimeOffset.UtcNow, "turn-old", BranchId: "main"));

        var repaired = await orchestrator.RepairConversationAsync(
            "conv-repair",
            new ConversationRepairRequestDto(
                CorrectedIntent: "Сделай подробный пошаговый план",
                TurnId: "turn-old",
                RepairNote: "нужен формат markdown",
                MaxHistory: 12,
                BranchId: "main"),
            CancellationToken.None);

        Assert.Equal("conv-repair", repaired.ConversationId);
        Assert.Contains("Repaired answer", repaired.Response);
        var systemRepairMessage = repaired.Messages.LastOrDefault(m =>
            m.Role.Equals("system", StringComparison.OrdinalIgnoreCase) &&
            m.Content.Contains("Conversation repair requested", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(systemRepairMessage);
    }

    [Fact]
    public void MemoryPolicyService_BlocksPersonalLongTermFact_WithoutExplicitConsent()
    {
        var state = new ConversationState("memory-consent")
        {
            LongTermMemoryEnabled = true,
            PersonalMemoryConsentGranted = false
        };
        var service = new MemoryPolicyService();

        service.CaptureFromUserMessage(state, new ChatMessageDto("user", "remember: my birthday is 1990-01-01", DateTimeOffset.UtcNow, "t1"), DateTimeOffset.UtcNow);

        Assert.DoesNotContain(state.MemoryItems, item => item.Type == "long_term");
        Assert.Empty(state.Preferences);
    }

    [Fact]
    public void MemoryPolicyService_StoresPersonalLongTermFact_WithConsentAndTtl()
    {
        var now = DateTimeOffset.UtcNow;
        var state = new ConversationState("memory-consent-enabled")
        {
            LongTermMemoryEnabled = true,
            PersonalMemoryConsentGranted = true,
            LongTermMemoryTtlDays = 10
        };
        var service = new MemoryPolicyService();

        service.CaptureFromUserMessage(state, new ChatMessageDto("user", "remember: my role is lead architect", now, "t1"), now);

        var item = Assert.Single(state.MemoryItems, x => x.Type == "long_term");
        Assert.True(item.IsPersonal);
        Assert.Equal(now.AddDays(10), item.ExpiresAt);
        Assert.Contains("my role is lead architect", state.Preferences);
    }

    [Fact]
    public void MemoryPolicyService_DeletesMemoryItem_AndSyncsLegacyCollections()
    {
        var now = DateTimeOffset.UtcNow;
        var state = new ConversationState("memory-delete")
        {
            LongTermMemoryEnabled = true,
            PersonalMemoryConsentGranted = true
        };
        var service = new MemoryPolicyService();
        service.CaptureFromUserMessage(state, new ChatMessageDto("user", "remember: answer concise", now, "t1"), now);
        var itemId = Assert.Single(state.MemoryItems, x => x.Type == "long_term").Id;

        var deleted = service.DeleteItem(state, itemId, now.AddMinutes(1));

        Assert.True(deleted);
        Assert.DoesNotContain(state.MemoryItems, x => x.Type == "long_term");
        Assert.Empty(state.Preferences);
    }

    [Fact]
    public void ConversationSummarizer_BuildsStructuredSummary_ForLongBranch()
    {
        var summarizer = new ConversationSummarizer();
        var now = DateTimeOffset.UtcNow;
        var messages = new List<ChatMessageDto>();
        for (var i = 1; i <= 7; i++)
        {
            messages.Add(new ChatMessageDto("user", $"Implement api gateway step {i} with rollback checks and observability", now.AddMinutes(i), $"u-{i}", BranchId: "main"));
            messages.Add(new ChatMessageDto("assistant", $"Implemented draft for step {i}; added notes about tests and metrics", now.AddMinutes(i).AddSeconds(10), $"u-{i}", BranchId: "main"));
        }

        var summary = summarizer.TryBuild("main", messages, null, now.AddHours(1));

        Assert.NotNull(summary);
        Assert.Contains("Goal:", summary!.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Context:", summary.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Progress:", summary.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.True(summary.QualityScore >= 0.32);
    }

    [Fact]
    public void InMemoryConversationStore_MaintainsBranchAwareSummaries()
    {
        var store = new InMemoryConversationStore(new MemoryPolicyService(), new ConversationSummarizer(), persistencePath: null);
        var state = store.GetOrCreate("summary-branch");
        for (var i = 1; i <= 8; i++)
        {
            var turnId = $"turn-{i}";
            store.AddMessage(state, new ChatMessageDto("user", $"Main branch task {i}: improve reliability and diagnostics", DateTimeOffset.UtcNow.AddMinutes(i), turnId, BranchId: "main"));
            store.AddMessage(state, new ChatMessageDto("assistant", $"Main branch progress {i}: completed migration and tests", DateTimeOffset.UtcNow.AddMinutes(i).AddSeconds(5), turnId, BranchId: "main"));
        }

        Assert.True(store.CreateBranch(state, "turn-6", "branch-x", out var branchId));
        store.AddMessage(state, new ChatMessageDto("user", "Branch task: pivot architecture to event-driven design", DateTimeOffset.UtcNow.AddMinutes(100), "turn-b1", BranchId: branchId));
        store.AddMessage(state, new ChatMessageDto("assistant", "Branch progress: added broker abstraction and retry envelope", DateTimeOffset.UtcNow.AddMinutes(100).AddSeconds(5), "turn-b1", BranchId: branchId));

        Assert.True(state.BranchSummaries.ContainsKey("main"));
        Assert.True(state.BranchSummaries.ContainsKey(branchId));
        Assert.NotEqual(state.BranchSummaries["main"].Summary, state.BranchSummaries[branchId].Summary);
    }

    [Fact]
    public void ChatStreamResumeHelper_SplitsRemainingResponse_ByCursorAndChunkSize()
    {
        var payload = string.Concat(Enumerable.Repeat("abcdefghij", 4)); // 40 chars
        var chunks = ChatStreamResumeHelper.SplitRemainingResponse(payload, 4, chunkSize: 24).ToList();

        Assert.Equal(2, chunks.Count);
        Assert.Equal(24, chunks[0].Length);
        Assert.Equal(12, chunks[1].Length);
    }

    [Fact]
    public void ChatStreamResumeHelper_BuildsReplayResponse_ForExistingAssistantTurn()
    {
        var store = new InMemoryConversationStore();
        var state = store.GetOrCreate("stream-replay");
        var timestamp = DateTimeOffset.UtcNow;
        store.AddMessage(state, new ChatMessageDto("user", "ping", timestamp, "turn-1", BranchId: "main"));
        store.AddMessage(state, new ChatMessageDto("assistant", "pong", timestamp.AddSeconds(1), "turn-1", BranchId: "main"));

        var ok = ChatStreamResumeHelper.TryBuildReplayResponse(
            store,
            state.Id,
            new ChatStreamResumeRequestDto(CursorOffset: 0, TurnId: "turn-1"),
            out var replay);

        Assert.True(ok);
        Assert.Equal(state.Id, replay.ConversationId);
        Assert.Equal("pong", replay.Response);
        Assert.Equal("turn-1", replay.TurnId);
    }

    [Fact]
    public void TurnLifecycleStateMachine_RejectsIllegalTransition()
    {
        var machine = new TurnLifecycleStateMachine();
        var context = new ChatTurnContext
        {
            TurnId = "illegal-transition",
            Request = new ChatRequestDto("hello", null, 10, null),
            Conversation = new ConversationState("conv"),
            History = Array.Empty<ChatMessageDto>()
        };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            machine.Transition(context, TurnLifecycleState.Execute));

        Assert.Contains("Illegal lifecycle transition", ex.Message);
    }

    [Fact]
    public void TurnLifecycleStateMachine_TracksNominalTrace()
    {
        var machine = new TurnLifecycleStateMachine();
        var context = new ChatTurnContext
        {
            TurnId = "trace-transition",
            Request = new ChatRequestDto("hello", null, 10, null),
            Conversation = new ConversationState("conv"),
            History = Array.Empty<ChatMessageDto>()
        };

        machine.Transition(context, TurnLifecycleState.Understand);
        machine.Transition(context, TurnLifecycleState.Execute);
        machine.Transition(context, TurnLifecycleState.Verify);
        machine.Transition(context, TurnLifecycleState.Finalize);
        machine.Transition(context, TurnLifecycleState.PostAudit);

        var expected = new[]
        {
            TurnLifecycleState.New,
            TurnLifecycleState.Understand,
            TurnLifecycleState.Execute,
            TurnLifecycleState.Verify,
            TurnLifecycleState.Finalize,
            TurnLifecycleState.PostAudit
        };

        Assert.Equal(expected, context.LifecycleTrace);
    }

    [Fact]
    public async Task ChatOrchestrator_TracksLifecycleTrace_ForRegularTurn()
    {
        var store = new InMemoryConversationStore();
        var planner = new Mock<IChatTurnPlanner>();
        var executor = new Mock<IChatTurnExecutor>();
        var critic = new Mock<IChatTurnCritic>();
        var finalizer = new Mock<IChatTurnFinalizer>();
        ChatTurnContext? captured = null;

        executor.Setup(x => x.ExecuteAsync(It.IsAny<ChatTurnContext>(), It.IsAny<CancellationToken>()))
            .Returns<ChatTurnContext, CancellationToken>((ctx, _) =>
            {
                captured = ctx;
                ctx.ExecutionOutput = "regular-output";
                return Task.CompletedTask;
            });

        critic.Setup(x => x.CritiqueAsync(It.IsAny<ChatTurnContext>(), It.IsAny<CancellationToken>()))
            .Returns<ChatTurnContext, CancellationToken>((ctx, _) =>
            {
                ctx.IsCritiqueApproved = true;
                ctx.Confidence = 0.88;
                return Task.CompletedTask;
            });

        finalizer.Setup(x => x.FinalizeAsync(It.IsAny<ChatTurnContext>(), It.IsAny<CancellationToken>()))
            .Callback<ChatTurnContext, CancellationToken>((ctx, _) =>
            {
                ctx.FinalResponse = ctx.ExecutionOutput;
            })
            .Returns(Task.CompletedTask);

        var orchestrator = CreateRuntimeBackedOrchestrator(
            store,
            planner.Object,
            executor.Object,
            critic.Object,
            finalizer.Object,
            new InputRiskScanner(),
            new OutputExfiltrationGuard(),
            new TurnLifecycleStateMachine());

        var response = await orchestrator.CompleteTurnAsync(new ChatRequestDto("hello", null, 10, null), CancellationToken.None);

        Assert.Equal("regular-output", response.Response);
        Assert.NotNull(captured);
        var expected = new[]
        {
            TurnLifecycleState.New,
            TurnLifecycleState.Understand,
            TurnLifecycleState.Execute,
            TurnLifecycleState.Verify,
            TurnLifecycleState.Finalize,
            TurnLifecycleState.PostAudit
        };
        Assert.Equal(expected, captured!.LifecycleTrace);
    }

    [Fact]
    public async Task TurnOrchestrationEngine_DoesNotRecover_WhenCriticIsSkipped()
    {
        var store = new InMemoryConversationStore();
        var planner = new Mock<IChatTurnPlanner>();
        var executor = new Mock<IChatTurnExecutor>();
        var critic = new Mock<IChatTurnCritic>(MockBehavior.Strict);
        var finalizer = new Mock<IChatTurnFinalizer>();
        var stagePolicy = new Mock<ITurnStagePolicy>();
        var auditScheduler = new Mock<IPostTurnAuditScheduler>();

        planner.Setup(x => x.PlanAsync(It.IsAny<ChatTurnContext>(), It.IsAny<CancellationToken>()))
            .Returns<ChatTurnContext, CancellationToken>((ctx, _) =>
            {
                ctx.Intent = new IntentAnalysis(IntentType.Unknown, "test-model");
                return Task.CompletedTask;
            });
        executor.Setup(x => x.ExecuteAsync(It.IsAny<ChatTurnContext>(), It.IsAny<CancellationToken>()))
            .Returns<ChatTurnContext, CancellationToken>((ctx, _) =>
            {
                ctx.ExecutionOutput = "nominal-output";
                return Task.CompletedTask;
            });
        finalizer.Setup(x => x.FinalizeAsync(It.IsAny<ChatTurnContext>(), It.IsAny<CancellationToken>()))
            .Returns<ChatTurnContext, CancellationToken>((ctx, _) =>
            {
                ctx.FinalResponse = ctx.CorrectedContent ?? ctx.ExecutionOutput;
                return Task.CompletedTask;
            });
        stagePolicy.Setup(x => x.RequiresSynchronousCritic(It.IsAny<ChatTurnContext>())).Returns(false);
        stagePolicy.Setup(x => x.AllowsAsyncAudit(It.IsAny<ChatTurnContext>())).Returns(false);
        auditScheduler.Setup(x => x.TrySchedule(It.IsAny<ChatTurnContext>(), It.IsAny<ChatResponseDto>())).Returns(false);

        var engine = new TurnOrchestrationEngine(
            store,
            planner.Object,
            executor.Object,
            critic.Object,
            finalizer.Object,
            new InputRiskScanner(),
            new OutputExfiltrationGuard(),
            new TurnLifecycleStateMachine(),
            new TurnExecutionStateMachine(),
            new TurnCheckpointManager(),
            stagePolicy.Object,
            auditScheduler.Object,
            null,
            NullLogger<TurnOrchestrationEngine>.Instance);

        var response = await engine.StartTurnAsync(new ChatRequestDto("hello", null, 10, null), CancellationToken.None);

        Assert.Equal("nominal-output", response.Response);
        Assert.DoesNotContain("turn_pipeline_recovered", response.UncertaintyFlags ?? Array.Empty<string>());
        Assert.Equal(
            new[]
            {
                TurnLifecycleState.New.ToString(),
                TurnLifecycleState.Understand.ToString(),
                TurnLifecycleState.Execute.ToString(),
                TurnLifecycleState.Verify.ToString(),
                TurnLifecycleState.Finalize.ToString(),
                TurnLifecycleState.PostAudit.ToString()
            },
            response.LifecycleTrace);
    }

    [Fact]
    public async Task TurnOrchestrationEngine_EmitsChatRouteTelemetry_OnCompletedTurn()
    {
        var store = new InMemoryConversationStore();
        var planner = new Mock<IChatTurnPlanner>();
        var executor = new Mock<IChatTurnExecutor>();
        var critic = new Mock<IChatTurnCritic>(MockBehavior.Strict);
        var finalizer = new Mock<IChatTurnFinalizer>();
        var stagePolicy = new Mock<ITurnStagePolicy>();
        var auditScheduler = new Mock<IPostTurnAuditScheduler>();
        var routeTelemetry = new RouteTelemetryService();

        planner.Setup(x => x.PlanAsync(It.IsAny<ChatTurnContext>(), It.IsAny<CancellationToken>()))
            .Returns<ChatTurnContext, CancellationToken>((ctx, _) =>
            {
                ctx.Intent = new IntentAnalysis(IntentType.Research, "test-model");
                ctx.IntentConfidence = 0.78;
                ctx.IntentSource = "model_first";
                ctx.ExecutionMode = TurnExecutionMode.Balanced;
                ctx.BudgetProfile = TurnBudgetProfile.Research;
                ctx.IntentSignals.Add("test:research");
                return Task.CompletedTask;
            });
        executor.Setup(x => x.ExecuteAsync(It.IsAny<ChatTurnContext>(), It.IsAny<CancellationToken>()))
            .Returns<ChatTurnContext, CancellationToken>((ctx, _) =>
            {
                ctx.ExecutionOutput = "research output";
                ctx.FinalResponse = "research output";
                return Task.CompletedTask;
            });
        finalizer.Setup(x => x.FinalizeAsync(It.IsAny<ChatTurnContext>(), It.IsAny<CancellationToken>()))
            .Returns<ChatTurnContext, CancellationToken>((ctx, _) =>
            {
                ctx.FinalResponse = ctx.FinalResponse.Length > 0 ? ctx.FinalResponse : ctx.ExecutionOutput;
                return Task.CompletedTask;
            });
        stagePolicy.Setup(x => x.RequiresSynchronousCritic(It.IsAny<ChatTurnContext>())).Returns(false);
        stagePolicy.Setup(x => x.AllowsAsyncAudit(It.IsAny<ChatTurnContext>())).Returns(false);
        auditScheduler.Setup(x => x.TrySchedule(It.IsAny<ChatTurnContext>(), It.IsAny<ChatResponseDto>())).Returns(false);

        var engine = new TurnOrchestrationEngine(
            store,
            planner.Object,
            executor.Object,
            critic.Object,
            finalizer.Object,
            new InputRiskScanner(),
            new OutputExfiltrationGuard(),
            new TurnLifecycleStateMachine(),
            new TurnExecutionStateMachine(),
            new TurnCheckpointManager(),
            stagePolicy.Object,
            auditScheduler.Object,
            null,
            NullLogger<TurnOrchestrationEngine>.Instance,
            telemetryRecorder: new TurnRouteTelemetryRecorder(routeTelemetry));

        var response = await engine.StartTurnAsync(new ChatRequestDto("research software architecture", null, 10, null), CancellationToken.None);
        var snapshot = routeTelemetry.GetSnapshot();
        var recent = Assert.Single(snapshot.Recent);

        Assert.Equal("research output", response.Response);
        Assert.Equal(RouteTelemetryChannels.Chat, recent.Channel);
        Assert.Equal(RouteTelemetryOperationKinds.ChatTurn, recent.OperationKind);
        Assert.Equal("research", recent.RouteKey);
        Assert.Equal("research", recent.BudgetProfile);
        Assert.Equal("balanced", recent.ExecutionMode);
        Assert.Equal("model_first", recent.IntentSource);
        Assert.Equal(RouteTelemetryOutcomes.Completed, recent.Outcome);
    }

    [Fact]
    public async Task ChatOrchestrator_Recovers_WhenExecutorThrows()
    {
        var store = new InMemoryConversationStore();
        var planner = new Mock<IChatTurnPlanner>();
        var executor = new Mock<IChatTurnExecutor>();
        var critic = new Mock<IChatTurnCritic>();
        var finalizer = new Mock<IChatTurnFinalizer>();

        executor.Setup(x => x.ExecuteAsync(It.IsAny<ChatTurnContext>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("executor boom"));

        var orchestrator = CreateRuntimeBackedOrchestrator(
            store,
            planner.Object,
            executor.Object,
            critic.Object,
            finalizer.Object,
            new InputRiskScanner(),
            new OutputExfiltrationGuard(),
            new TurnLifecycleStateMachine());

        var response = await orchestrator.CompleteTurnAsync(new ChatRequestDto("recover this", null, 10, null), CancellationToken.None);

        Assert.Contains("recovery mode", response.Response, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("turn_pipeline_recovered", response.UncertaintyFlags ?? Array.Empty<string>());
        Assert.Contains("recovered_from_execute", response.UncertaintyFlags ?? Array.Empty<string>());
        Assert.True(response.Confidence <= 0.25);
    }

    [Fact]
    public void UserProfileService_NormalizesAndBuildsHint()
    {
            var state = new ConversationState("profile")
            {
                PreferredLanguage = "english",
                DetailLevel = "long",
                Formality = "informal",
            DomainFamiliarity = "beginner",
            PreferredStructure = "steps",
                Warmth = "high",
                Enthusiasm = "energetic",
                Directness = "gentle",
                DefaultAnswerShape = "list",
                SearchLocalityHint = "  New   York City  "
            };
        var service = new UserProfileService();

        var profile = service.Resolve(state);
        var hint = service.BuildSystemHint(profile);
        var route = service.ResolveStyleRoute(profile);

        Assert.Equal("en", profile.Language);
        Assert.Equal("deep", profile.DetailLevel);
        Assert.Equal("casual", profile.Formality);
        Assert.Equal("novice", profile.DomainFamiliarity);
        Assert.Equal("step_by_step", profile.PreferredStructure);
        Assert.Equal("warm", profile.Warmth);
        Assert.Equal("high", profile.Enthusiasm);
        Assert.Equal("soft", profile.Directness);
        Assert.Equal("bullets", profile.DefaultAnswerShape);
        Assert.Equal("New York City", profile.SearchLocalityHint);
        Assert.Equal("conversational", route.Mode);
        Assert.Equal("conversational_warm", route.TonePreset);
        Assert.Contains("formality=casual", hint);
        Assert.Contains("domain=novice", hint);
        Assert.Contains("warmth=warm", hint);
        Assert.Contains("enthusiasm=high", hint);
        Assert.Contains("directness=soft", hint);
        Assert.Contains("answer_shape=bullets", hint);
        Assert.Contains("mode=conversational", hint);
        Assert.Contains("tone_preset=conversational_warm", hint);
    }

    [Fact]
    public async Task ChatTurnExecutor_UsesUserProfileHintInSystemInstruction()
    {
        var ai = new Mock<AILink>("http://localhost:11434", "qwen");
        string? capturedSystemInstruction = null;
        ai.Setup(a => a.AskAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<string>()))
            .Callback<string, CancellationToken, string?, string?, int, string?>((_, _, _, _, _, instruction) =>
            {
                capturedSystemInstruction = instruction;
            })
            .ReturnsAsync("executor-result");

        var model = new Mock<IModelOrchestrator>();
        var research = new Mock<IResearchService>();
        var orchestrator = new Mock<IHelperOrchestrator>();
        orchestrator.SetupGet(x => x.Forge).Returns(Mock.Of<IProjectForgeOrchestrator>());
        orchestrator.Setup(x => x.GenerateProjectAsync(
                It.IsAny<GenerationRequest>(),
                It.IsAny<bool>(),
                It.IsAny<Action<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((GenerationRequest request, bool _, Action<string>? __, CancellationToken ___) =>
                new GenerationResult(
                    true,
                    new List<GeneratedFile>(),
                    request.OutputPath,
                    new List<BuildError>(),
                    TimeSpan.Zero));
        var resilienceTelemetry = new ChatResilienceTelemetryService();
        var resilience = new ChatResiliencePolicy(NullLogger<ChatResiliencePolicy>.Instance, resilienceTelemetry);
        var executor = new ChatTurnExecutor(
            ai.Object,
            model.Object,
            research.Object,
            new ShortHorizonResearchCache(),
            resilience,
            orchestrator.Object,
            new ApiRuntimeConfig("root", "projects", "library", "logs", "templates", "test-key"),
            new UserProfileService());

        var conversation = new ConversationState("profile-conv")
        {
            PreferredLanguage = "ru",
            DetailLevel = "deep",
            Formality = "formal",
            DomainFamiliarity = "expert",
            PreferredStructure = "checklist",
            Warmth = "warm",
            Enthusiasm = "low",
            Directness = "direct",
            DefaultAnswerShape = "paragraph"
        };
        var context = new ChatTurnContext
        {
            TurnId = "executor-profile",
            Request = new ChatRequestDto("Explain architecture", conversation.Id, 10, null),
            Conversation = conversation,
            History = new[]
            {
                new ChatMessageDto("user", "Explain architecture", DateTimeOffset.UtcNow)
            },
            Intent = new IntentAnalysis(IntentType.Unknown, "test-model")
        };

        await executor.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal("executor-result", context.ExecutionOutput);
        Assert.NotNull(capturedSystemInstruction);
        Assert.Contains("language=ru", capturedSystemInstruction, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("formality=formal", capturedSystemInstruction, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("domain=expert", capturedSystemInstruction, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("structure=checklist", capturedSystemInstruction, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("warmth=warm", capturedSystemInstruction, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("enthusiasm=low", capturedSystemInstruction, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("directness=direct", capturedSystemInstruction, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("answer_shape=paragraph", capturedSystemInstruction, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("mode=professional", capturedSystemInstruction, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("tone_preset=professional_direct", capturedSystemInstruction, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("professional", context.ResolvedStyleMode);
        Assert.Equal("professional_direct", context.ResolvedTonePreset);
    }

    [Fact]
    public async Task ChatTurnExecutor_BypassesAdmission_ForExplicitGoldenTemplatePrompt()
    {
        var previousGenerationFlag = Environment.GetEnvironmentVariable("HELPER_CHAT_ENABLE_PROJECT_GENERATION");
        Environment.SetEnvironmentVariable("HELPER_CHAT_ENABLE_PROJECT_GENERATION", "true");

        try
        {
            var ai = new Mock<AILink>("http://localhost:11434", "qwen");
            ai.Setup(a => a.AskAsync(
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<int>(),
                    It.IsAny<string>()))
                .ReturnsAsync("unused");

            var model = new Mock<IModelOrchestrator>();
            var research = new Mock<IResearchService>();
            var orchestrator = new Mock<IHelperOrchestrator>();
            orchestrator.SetupGet(x => x.Forge).Returns(Mock.Of<IProjectForgeOrchestrator>());
            orchestrator.Setup(x => x.GenerateProjectAsync(
                    It.IsAny<GenerationRequest>(),
                    It.IsAny<bool>(),
                    It.IsAny<Action<string>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((GenerationRequest request, bool _, Action<string>? __, CancellationToken ___) =>
                    new GenerationResult(
                        true,
                        new List<GeneratedFile>(),
                        request.OutputPath,
                        new List<BuildError>(),
                        TimeSpan.Zero));

            var resilienceTelemetry = new ChatResilienceTelemetryService();
            var resilience = new ChatResiliencePolicy(NullLogger<ChatResiliencePolicy>.Instance, resilienceTelemetry);
            var executor = new ChatTurnExecutor(
                ai.Object,
                model.Object,
                research.Object,
                new ShortHorizonResearchCache(),
                resilience,
                orchestrator.Object,
                new ApiRuntimeConfig("root", "projects", "library", "logs", "templates", "test-key"),
                new UserProfileService());

            var context = new ChatTurnContext
            {
                TurnId = "executor-golden-admission-bypass",
                Request = new ChatRequestDto("шахматы wpf desktop", "conv-executor-golden", 10, null),
                Conversation = new ConversationState("conv-executor-golden"),
                History = Array.Empty<ChatMessageDto>(),
                Intent = new IntentAnalysis(IntentType.Generate, "test-model"),
                IntentConfidence = 0.2
            };

            await executor.ExecuteAsync(context, CancellationToken.None);

            Assert.True(
                context.ExecutionOutput.Contains("Project successfully generated at:", StringComparison.OrdinalIgnoreCase) ||
                context.ExecutionOutput.Contains("Проект успешно сгенерирован по пути:", StringComparison.OrdinalIgnoreCase));
            Assert.Contains("generation_admission_bypassed_for_golden_template", context.UncertaintyFlags);
            orchestrator.Verify(x => x.GenerateProjectAsync(
                It.IsAny<GenerationRequest>(),
                It.IsAny<bool>(),
                It.IsAny<Action<string>>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }
        finally
        {
            Environment.SetEnvironmentVariable("HELPER_CHAT_ENABLE_PROJECT_GENERATION", previousGenerationFlag);
        }
    }

    [Fact]
    public async Task ChatTurnExecutor_UsesDeterministicMemoryCapture_ForRememberPrompt()
    {
        var ai = new Mock<AILink>("http://localhost:11434", "qwen");
        var modelGateway = new Mock<IModelGateway>(MockBehavior.Strict);
        var model = new Mock<IModelOrchestrator>();
        var research = new Mock<IResearchService>();
        var orchestrator = new Mock<IHelperOrchestrator>();
        orchestrator.SetupGet(x => x.Forge).Returns(Mock.Of<IProjectForgeOrchestrator>());

        var resilienceTelemetry = new ChatResilienceTelemetryService();
        var resilience = new ChatResiliencePolicy(NullLogger<ChatResiliencePolicy>.Instance, resilienceTelemetry);
        var executor = new ChatTurnExecutor(
            ai.Object,
            model.Object,
            research.Object,
            new ShortHorizonResearchCache(),
            resilience,
            orchestrator.Object,
            new ApiRuntimeConfig("root", "projects", "library", "logs", "templates", "test-key"),
            new UserProfileService(),
            modelGateway: modelGateway.Object);

        var context = new ChatTurnContext
        {
            TurnId = "deterministic-memory-sync",
            Request = new ChatRequestDto("[1] remember: answer concise", "conv-memory-sync", 10, null),
            Conversation = new ConversationState("conv-memory-sync"),
            History = new[] { new ChatMessageDto("user", "[1] remember: answer concise", DateTimeOffset.UtcNow) },
            Intent = new IntentAnalysis(IntentType.Unknown, "test-model")
        };

        await executor.ExecuteAsync(context, CancellationToken.None);

        Assert.Contains("preference", context.ExecutionOutput, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("answer concise", context.ExecutionOutput, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("memory_captured", context.GroundingStatus);
        Assert.Contains("deterministic_memory_capture", context.UncertaintyFlags);
        modelGateway.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ChatTurnExecutor_StreamsDeterministicMemoryCapture_ForRememberPrompt()
    {
        var ai = new Mock<AILink>("http://localhost:11434", "qwen");
        var modelGateway = new Mock<IModelGateway>(MockBehavior.Strict);
        var model = new Mock<IModelOrchestrator>();
        var research = new Mock<IResearchService>();
        var orchestrator = new Mock<IHelperOrchestrator>();
        orchestrator.SetupGet(x => x.Forge).Returns(Mock.Of<IProjectForgeOrchestrator>());

        var resilienceTelemetry = new ChatResilienceTelemetryService();
        var resilience = new ChatResiliencePolicy(NullLogger<ChatResiliencePolicy>.Instance, resilienceTelemetry);
        var executor = new ChatTurnExecutor(
            ai.Object,
            model.Object,
            research.Object,
            new ShortHorizonResearchCache(),
            resilience,
            orchestrator.Object,
            new ApiRuntimeConfig("root", "projects", "library", "logs", "templates", "test-key"),
            new UserProfileService(),
            modelGateway: modelGateway.Object);

        var context = new ChatTurnContext
        {
            TurnId = "deterministic-memory-stream",
            Request = new ChatRequestDto("[1] remember: answer concise", "conv-memory-stream", 10, null),
            Conversation = new ConversationState("conv-memory-stream"),
            History = new[] { new ChatMessageDto("user", "[1] remember: answer concise", DateTimeOffset.UtcNow) },
            Intent = new IntentAnalysis(IntentType.Unknown, "test-model")
        };

        var chunks = new List<string>();
        await foreach (var chunk in executor.ExecuteStreamAsync(context, CancellationToken.None))
        {
            if (!string.IsNullOrWhiteSpace(chunk.Content))
            {
                chunks.Add(chunk.Content);
            }
        }

        Assert.Single(chunks);
        Assert.Equal(context.ExecutionOutput, chunks[0]);
        Assert.Equal("memory_captured", context.GroundingStatus);
        modelGateway.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ChatTurnExecutor_EnforcesTokenBudget_DuringStreaming()
    {
        var ai = new Mock<AILink>("http://localhost:11434", "qwen");
        ai.Setup(a => a.StreamAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<int>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns((string _, string? _, string? _, int _, string? _, CancellationToken _) => EmitStreamingTokens());

        var model = new Mock<IModelOrchestrator>();
        var research = new Mock<IResearchService>();
        var orchestrator = new Mock<IHelperOrchestrator>();
        orchestrator.SetupGet(x => x.Forge).Returns(Mock.Of<IProjectForgeOrchestrator>());
        orchestrator.Setup(x => x.GenerateProjectAsync(
                It.IsAny<GenerationRequest>(),
                It.IsAny<bool>(),
                It.IsAny<Action<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((GenerationRequest request, bool _, Action<string>? __, CancellationToken ___) =>
                new GenerationResult(
                    true,
                    new List<GeneratedFile>(),
                    request.OutputPath,
                    new List<BuildError>(),
                    TimeSpan.Zero));
        var resilienceTelemetry = new ChatResilienceTelemetryService();
        var resilience = new ChatResiliencePolicy(NullLogger<ChatResiliencePolicy>.Instance, resilienceTelemetry);
        var executor = new ChatTurnExecutor(
            ai.Object,
            model.Object,
            research.Object,
            new ShortHorizonResearchCache(),
            resilience,
            orchestrator.Object,
            new ApiRuntimeConfig("root", "projects", "library", "logs", "templates", "test-key"),
            new UserProfileService());

        var context = new ChatTurnContext
        {
            TurnId = "token-budget-streaming",
            Request = new ChatRequestDto("Explain architecture", "conv-token-budget", 10, null),
            Conversation = new ConversationState("conv-token-budget"),
            History = new[] { new ChatMessageDto("user", "Explain architecture", DateTimeOffset.UtcNow) },
            Intent = new IntentAnalysis(IntentType.Unknown, "test-model"),
            TokenBudget = 2
        };

        var outputChunks = new List<string>();
        await foreach (var chunk in executor.ExecuteStreamAsync(context, CancellationToken.None))
        {
            if (!string.IsNullOrEmpty(chunk.Content))
            {
                outputChunks.Add(chunk.Content);
            }
        }

        Assert.True(context.BudgetExceeded);
        Assert.Contains("token_budget_exceeded", context.UncertaintyFlags);
        Assert.Contains("Output truncated by latency budget", context.ExecutionOutput, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(outputChunks);
    }

    [Fact]
    public void ConversationMetricsService_ComputesStyleRates_AndRaisesHumanLikeAlerts()
    {
        var metrics = new ConversationMetricsService();

        metrics.RecordTurn(new ConversationTurnMetric(
            FirstTokenLatencyMs: 100,
            FullResponseLatencyMs: 500,
            ToolCallsCount: 0,
            IsFactualPrompt: false,
            HasCitations: false,
            Confidence: 0.9,
            IsSuccessful: true,
            Style: new ConversationStyleTurnMetric(
                LeadPhraseFingerprint: "understanding:",
                MixedLanguageDetected: true,
                GenericClarificationDetected: false,
                GenericNextStepDetected: true,
                MemoryAckTemplateDetected: false,
                SourceFingerprint: "example.org/a|example.org/b")));

        metrics.RecordTurn(new ConversationTurnMetric(
            FirstTokenLatencyMs: 110,
            FullResponseLatencyMs: 520,
            ToolCallsCount: 0,
            IsFactualPrompt: false,
            HasCitations: false,
            Confidence: 0.88,
            IsSuccessful: true,
            Style: new ConversationStyleTurnMetric(
                LeadPhraseFingerprint: "understanding:",
                MixedLanguageDetected: false,
                GenericClarificationDetected: true,
                GenericNextStepDetected: false,
                MemoryAckTemplateDetected: true,
                SourceFingerprint: "example.org/a|example.org/b")));

        metrics.RecordTurn(new ConversationTurnMetric(
            FirstTokenLatencyMs: 115,
            FullResponseLatencyMs: 540,
            ToolCallsCount: 0,
            IsFactualPrompt: false,
            HasCitations: false,
            Confidence: 0.87,
            IsSuccessful: true,
            Style: new ConversationStyleTurnMetric(
                LeadPhraseFingerprint: "understanding:",
                MixedLanguageDetected: false,
                GenericClarificationDetected: false,
                GenericNextStepDetected: true,
                MemoryAckTemplateDetected: true,
                SourceFingerprint: "example.org/a|example.org/b")));

        var snapshot = metrics.GetSnapshot();

        Assert.Equal(3, snapshot.Style.Turns);
        Assert.True(snapshot.Style.RepeatedPhraseRate > 0.60);
        Assert.True(snapshot.Style.MixedLanguageTurnRate > 0.30);
        Assert.True(snapshot.Style.GenericClarificationRate > 0.30);
        Assert.True(snapshot.Style.GenericNextStepRate > 0.60);
        Assert.True(snapshot.Style.MemoryAckTemplateRate > 0.60);
        Assert.True(snapshot.Style.SourceReuseDominance > 0.90);
        Assert.Contains(snapshot.Alerts, alert => alert.Contains("Repeated lead-phrase rate", StringComparison.Ordinal));
        Assert.Contains(snapshot.Alerts, alert => alert.Contains("Mixed-language turn rate", StringComparison.Ordinal));
        Assert.Contains(snapshot.Alerts, alert => alert.Contains("Source reuse dominance", StringComparison.Ordinal));
    }

    private static async IAsyncEnumerable<string> EmitStreamingTokens()
    {
        yield return "abcdefghij";
        await Task.Yield();
        yield return "klmnopqrst";
    }
}
