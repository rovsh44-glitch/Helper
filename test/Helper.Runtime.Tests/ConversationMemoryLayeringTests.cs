using Helper.Api.Conversation;
using Helper.Api.Hosting;
using Helper.Runtime.Core;
using Moq;

namespace Helper.Runtime.Tests;

public class ConversationMemoryLayeringTests
{
    [Fact]
    public async Task ConversationContextAssembler_InjectsStructuredLayers_WithoutLosingRecentHistory()
    {
        var reflection = new Mock<IReflectionService>(MockBehavior.Strict);
        reflection.Setup(x => x.SearchLessonsAsync("How should we verify the migration plan?", 2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EngineeringLesson>
            {
                new("Migration regression", "backend", "Run schema diff before rollout.", "Verify invariants before cutover.", DateTime.UtcNow)
            });

        var retrieval = new Mock<IRetrievalContextAssembler>(MockBehavior.Strict);
        retrieval.Setup(x => x.AssembleAsync(
                "How should we verify the migration plan?",
                null,
                3,
                "v2",
                true,
                It.IsAny<CancellationToken>(),
                It.Is<RetrievalRequestOptions?>(options => options != null &&
                                                        options.Purpose == RetrievalPurpose.FactualLookup &&
                                                        options.PreferTraceableChunks)))
            .ReturnsAsync(new List<KnowledgeChunk>
            {
                new("chunk-1", "OpenAPI contract changes must be diffed before release.", new[] { 0.1f }, new Dictionary<string, string> { ["collection"] = "docs" }, "docs")
            });

        var assembler = new ConversationContextAssembler(reflection.Object, retrieval.Object, new ReasoningAwareRetrievalPolicy());
        var context = new ChatTurnContext
        {
            TurnId = "turn-1",
            Request = new ChatRequestDto("How should we verify the migration plan?", "conv-1", 16, null),
            Conversation = new ConversationState("conv-1"),
            History = new[]
            {
                new ChatMessageDto("system", "Conversation summary (main): We are migrating the API gateway.", DateTimeOffset.UtcNow),
                new ChatMessageDto("system", "User preferences: concise, technical.", DateTimeOffset.UtcNow),
                new ChatMessageDto("user", "How should we verify the migration plan?", DateTimeOffset.UtcNow),
                new ChatMessageDto("assistant", "Let's build a checklist.", DateTimeOffset.UtcNow)
            },
            Intent = new IntentAnalysis(IntentType.Research, string.Empty),
            ExecutionMode = TurnExecutionMode.Balanced,
            IsFactualPrompt = true
        };

        var assembly = await assembler.AssembleAsync(context, CancellationToken.None);

        Assert.Contains("recent_history", assembly.UsedLayers);
        Assert.Contains("branch_summary", assembly.UsedLayers);
        Assert.Contains("conversation_profile", assembly.UsedLayers);
        Assert.Contains("procedural_lessons", assembly.UsedLayers);
        Assert.Contains("structured_retrieval", assembly.UsedLayers);
        Assert.Contains("Run schema diff before rollout.", assembly.Prompt);
        Assert.Contains("OpenAPI contract changes must be diffed before release.", assembly.Prompt);
        Assert.Contains("How should we verify the migration plan?", assembly.Prompt);
        Assert.Equal("factual_lookup", context.SelectedRetrievalPurpose);
        Assert.Contains(context.RetrievalTrace, trace => trace.Contains("purpose:FactualLookup", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ConversationContextAssembler_UsesReasoningSupportRetrievalMode_ForVerifiableDeepPrompt()
    {
        var reflection = new Mock<IReflectionService>(MockBehavior.Strict);
        reflection.Setup(x => x.SearchLessonsAsync("Return only JSON with status and count.", 3, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EngineeringLesson>());

        var retrieval = new Mock<IRetrievalContextAssembler>(MockBehavior.Strict);
        retrieval.Setup(x => x.AssembleAsync(
                "Return only JSON with status and count.",
                null,
                3,
                "v2",
                false,
                It.IsAny<CancellationToken>(),
                It.Is<RetrievalRequestOptions?>(options => options != null &&
                                                        options.Purpose == RetrievalPurpose.ReasoningSupport &&
                                                        options.PreferTraceableChunks &&
                                                        options.DisallowedDomains != null &&
                                                        options.DisallowedDomains.Contains("historical_encyclopedias"))))
            .ReturnsAsync(new List<KnowledgeChunk>
            {
                new(
                    "chunk-json",
                    "The result object should expose a status field and a numeric count field.",
                    new[] { 0.1f },
                    new Dictionary<string, string>
                    {
                        ["collection"] = "knowledge_computer_science_v2",
                        ["domain"] = "computer_science",
                        ["document_id"] = "doc-json",
                        ["section_path"] = "chapter.1",
                        ["page_start"] = "8"
                    },
                    "knowledge_computer_science_v2")
            });

        var assembler = new ConversationContextAssembler(reflection.Object, retrieval.Object, new ReasoningAwareRetrievalPolicy());
        var context = new ChatTurnContext
        {
            TurnId = "turn-reasoning-retrieval",
            Request = new ChatRequestDto("Return only JSON with status and count.", "conv-json", 12, null),
            Conversation = new ConversationState("conv-json"),
            History = new[]
            {
                new ChatMessageDto("user", "Return only JSON with status and count.", DateTimeOffset.UtcNow)
            },
            Intent = new IntentAnalysis(IntentType.Unknown, string.Empty),
            ExecutionMode = TurnExecutionMode.Deep
        };

        var assembly = await assembler.AssembleAsync(context, CancellationToken.None);

        Assert.Contains("structured_retrieval", assembly.UsedLayers);
        Assert.Equal("reasoning_support", context.SelectedRetrievalPurpose);
        Assert.Contains(context.RetrievalTrace, trace => trace.Contains("expand_context:false", StringComparison.Ordinal));
        Assert.Contains("numeric count field", assembly.Prompt);
    }

    [Fact]
    public async Task ConversationContextAssembler_UsesReasoningSupportRetrievalMode_ForSchemaConstrainedPromptRegression()
    {
        var reflection = new Mock<IReflectionService>(MockBehavior.Strict);
        reflection.Setup(x => x.SearchLessonsAsync("Return YAML with fields name and retries.", 3, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EngineeringLesson>());

        var retrieval = new Mock<IRetrievalContextAssembler>(MockBehavior.Strict);
        retrieval.Setup(x => x.AssembleAsync(
                "Return YAML with fields name and retries.",
                null,
                3,
                "v2",
                false,
                It.IsAny<CancellationToken>(),
                It.Is<RetrievalRequestOptions?>(options => options != null &&
                                                        options.Purpose == RetrievalPurpose.ReasoningSupport &&
                                                        options.PreferTraceableChunks &&
                                                        options.DisallowedDomains != null &&
                                                        options.DisallowedDomains.Contains("analysis_strategy"))))
            .ReturnsAsync(new List<KnowledgeChunk>
            {
                new(
                    "chunk-yaml",
                    "Use a retries field with an integer value and keep the name field as a plain string.",
                    new[] { 0.2f },
                    new Dictionary<string, string>
                    {
                        ["collection"] = "knowledge_programming",
                        ["domain"] = "computer_science"
                    },
                    "knowledge_programming")
            });

        var assembler = new ConversationContextAssembler(reflection.Object, retrieval.Object, new ReasoningAwareRetrievalPolicy());
        var context = new ChatTurnContext
        {
            TurnId = "turn-reasoning-retrieval-yaml",
            Request = new ChatRequestDto("Return YAML with fields name and retries.", "conv-yaml", 12, null),
            Conversation = new ConversationState("conv-yaml"),
            History = new[]
            {
                new ChatMessageDto("user", "Return YAML with fields name and retries.", DateTimeOffset.UtcNow)
            },
            Intent = new IntentAnalysis(IntentType.Unknown, string.Empty),
            ExecutionMode = TurnExecutionMode.Deep
        };

        var assembly = await assembler.AssembleAsync(context, CancellationToken.None);

        Assert.Contains("structured_retrieval", assembly.UsedLayers);
        Assert.Equal("reasoning_support", context.SelectedRetrievalPurpose);
        Assert.Contains(context.RetrievalTrace, trace => trace.Contains("expand_context:false", StringComparison.Ordinal));
        Assert.Contains("integer value", assembly.Prompt);
    }

    [Fact]
    public async Task ConversationContextAssembler_DeepensRetrieval_WhenInitialTopicalFitIsLow()
    {
        var reflection = new Mock<IReflectionService>(MockBehavior.Strict);
        reflection.Setup(x => x.SearchLessonsAsync("Explain Denavit-Hartenberg parameters for robot arm kinematics.", 2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EngineeringLesson>());

        var retrieval = new Mock<IRetrievalContextAssembler>(MockBehavior.Strict);
        retrieval.Setup(x => x.AssembleAsync(
                "Explain Denavit-Hartenberg parameters for robot arm kinematics.",
                null,
                3,
                "v2",
                true,
                It.IsAny<CancellationToken>(),
                It.Is<RetrievalRequestOptions?>(options => options != null &&
                                                        options.Purpose == RetrievalPurpose.FactualLookup &&
                                                        options.PreferTraceableChunks)))
            .ReturnsAsync(new List<KnowledgeChunk>
            {
                CreateRetrievedChunk(
                    "generic-encyclopedia",
                    "A generic encyclopedia note about systems and parameters.",
                    "encyclopedias",
                    "low",
                    0.210,
                    genericDomain: true)
            });
        retrieval.Setup(x => x.AssembleAsync(
                "Explain Denavit-Hartenberg parameters for robot arm kinematics.",
                null,
                6,
                "v2",
                true,
                It.IsAny<CancellationToken>(),
                It.Is<RetrievalRequestOptions?>(options => options != null &&
                                                        options.Purpose == RetrievalPurpose.FactualLookup &&
                                                        options.PreferTraceableChunks)))
            .ReturnsAsync(new List<KnowledgeChunk>
            {
                CreateRetrievedChunk(
                    "robotics-specific",
                    "Denavit-Hartenberg parameters encode the relative transforms between robot arm links.",
                    "robotics",
                    "high",
                    0.860),
                CreateRetrievedChunk(
                    "robotics-context",
                    "Robot kinematics uses Denavit-Hartenberg notation to map link geometry to joint frames.",
                    "robotics",
                    "high",
                    0.810)
            });

        var assembler = new ConversationContextAssembler(reflection.Object, retrieval.Object, new ReasoningAwareRetrievalPolicy());
        var context = new ChatTurnContext
        {
            TurnId = "turn-topical-fit-deepen",
            Request = new ChatRequestDto("Explain Denavit-Hartenberg parameters for robot arm kinematics.", "conv-robotics", 16, null),
            Conversation = new ConversationState("conv-robotics"),
            History = new[]
            {
                new ChatMessageDto("user", "Explain Denavit-Hartenberg parameters for robot arm kinematics.", DateTimeOffset.UtcNow)
            },
            Intent = new IntentAnalysis(IntentType.Research, string.Empty),
            ExecutionMode = TurnExecutionMode.Balanced,
            IsFactualPrompt = true
        };

        var assembly = await assembler.AssembleAsync(context, CancellationToken.None);

        Assert.Contains("Denavit-Hartenberg parameters encode", assembly.Prompt);
        Assert.DoesNotContain("generic encyclopedia note", assembly.Prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(context.RetrievalTrace, trace => trace.Contains("deep_retrieval:accepted", StringComparison.Ordinal));
        Assert.Contains(context.RetrievalTrace, trace => trace.Contains("topical_fit_domains:encyclopedias:low", StringComparison.Ordinal));
        Assert.Contains(context.RetrievalTrace, trace => trace.Contains("selected_topical_fit_label:high", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ConversationContextAssembler_DeepensRetrieval_WhenSourceDominanceIsHigh()
    {
        var reflection = new Mock<IReflectionService>(MockBehavior.Strict);
        reflection.Setup(x => x.SearchLessonsAsync("How should I compare Denavit-Hartenberg reference sources?", 2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EngineeringLesson>());

        var retrieval = new Mock<IRetrievalContextAssembler>(MockBehavior.Strict);
        retrieval.Setup(x => x.AssembleAsync(
                "How should I compare Denavit-Hartenberg reference sources?",
                null,
                3,
                "v2",
                true,
                It.IsAny<CancellationToken>(),
                It.Is<RetrievalRequestOptions?>(options => options != null &&
                                                        options.Purpose == RetrievalPurpose.FactualLookup &&
                                                        options.PreferTraceableChunks)))
            .ReturnsAsync(new List<KnowledgeChunk>
            {
                CreateSourceDominatedChunk("dom-1", "Source one explains frame parameters.", "https://docs.example.org/robotics/dh", "knowledge_robotics_v2"),
                CreateSourceDominatedChunk("dom-2", "Source one repeats alpha, a, d, theta.", "https://docs.example.org/robotics/dh", "knowledge_robotics_v2"),
                CreateSourceDominatedChunk("dom-3", "Source one repeats the same reference set.", "https://docs.example.org/robotics/dh", "knowledge_robotics_v2")
            });
        retrieval.Setup(x => x.AssembleAsync(
                "How should I compare Denavit-Hartenberg reference sources?",
                null,
                6,
                "v2",
                true,
                It.IsAny<CancellationToken>(),
                It.Is<RetrievalRequestOptions?>(options => options != null &&
                                                        options.Purpose == RetrievalPurpose.FactualLookup &&
                                                        options.PreferTraceableChunks)))
            .ReturnsAsync(new List<KnowledgeChunk>
            {
                CreateSourceDominatedChunk("dom-1", "Source one explains frame parameters.", "https://docs.example.org/robotics/dh", "knowledge_robotics_v2", guardApplied: true),
                CreateSourceDominatedChunk("alt-1", "Alternative robotics handbook compares notation variants.", "https://alt.example.org/robotics/notation", "knowledge_robotics_alt_v2", guardApplied: true),
                CreateSourceDominatedChunk("alt-2", "A second source focuses on kinematics assumptions and edge cases.", "https://research.example.org/robotics/kinematics", "knowledge_robotics_research_v2", guardApplied: true)
            });

        var assembler = new ConversationContextAssembler(reflection.Object, retrieval.Object, new ReasoningAwareRetrievalPolicy());
        var context = new ChatTurnContext
        {
            TurnId = "turn-source-diversity-deepen",
            Request = new ChatRequestDto("How should I compare Denavit-Hartenberg reference sources?", "conv-diversity", 16, null),
            Conversation = new ConversationState("conv-diversity"),
            History = new[]
            {
                new ChatMessageDto("user", "How should I compare Denavit-Hartenberg reference sources?", DateTimeOffset.UtcNow)
            },
            Intent = new IntentAnalysis(IntentType.Research, string.Empty),
            ExecutionMode = TurnExecutionMode.Balanced,
            IsFactualPrompt = true
        };

        var assembly = await assembler.AssembleAsync(context, CancellationToken.None);

        Assert.Contains("Alternative robotics handbook compares notation variants.", assembly.Prompt);
        Assert.Contains(context.RetrievalTrace, trace => trace.Contains("deep_retrieval:accepted", StringComparison.Ordinal));
        Assert.Contains(context.RetrievalTrace, trace => trace.Contains("source_diversity_guard:needed", StringComparison.Ordinal));
        Assert.Contains(context.RetrievalTrace, trace => trace.Contains("selected_source_diversity_guard:applied", StringComparison.Ordinal));
        Assert.Contains(context.RetrievalTrace, trace => trace.Contains("selected_source_diversity_distinct_sources:3", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ConversationContextAssembler_DiscardsRetrieval_WhenTopicalFitRemainsLow_AfterDeepRetry()
    {
        var reflection = new Mock<IReflectionService>(MockBehavior.Strict);
        reflection.Setup(x => x.SearchLessonsAsync("Explain aerobic vs anaerobic exercise for ordinary training.", 2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EngineeringLesson>());

        var retrieval = new Mock<IRetrievalContextAssembler>(MockBehavior.Strict);
        retrieval.Setup(x => x.AssembleAsync(
                "Explain aerobic vs anaerobic exercise for ordinary training.",
                null,
                3,
                "v2",
                true,
                It.IsAny<CancellationToken>(),
                It.IsAny<RetrievalRequestOptions?>()))
            .ReturnsAsync(new List<KnowledgeChunk>
            {
                CreateRetrievedChunk(
                    "physics-mismatch-1",
                    "Flight load equations describe aerodynamic stability rather than exercise training.",
                    "physics",
                    "low",
                    0.052)
            });
        retrieval.Setup(x => x.AssembleAsync(
                "Explain aerobic vs anaerobic exercise for ordinary training.",
                null,
                6,
                "v2",
                true,
                It.IsAny<CancellationToken>(),
                It.IsAny<RetrievalRequestOptions?>()))
            .ReturnsAsync(new List<KnowledgeChunk>
            {
                CreateRetrievedChunk(
                    "physics-mismatch-2",
                    "Theoretical mechanics discusses gas loads and resonance, not fitness programming.",
                    "physics",
                    "low",
                    0.071)
            });

        var assembler = new ConversationContextAssembler(reflection.Object, retrieval.Object, new ReasoningAwareRetrievalPolicy());
        var context = new ChatTurnContext
        {
            TurnId = "turn-topical-fit-discard",
            Request = new ChatRequestDto("Explain aerobic vs anaerobic exercise for ordinary training.", "conv-training", 16, null),
            Conversation = new ConversationState("conv-training"),
            History = new[]
            {
                new ChatMessageDto("user", "Explain aerobic vs anaerobic exercise for ordinary training.", DateTimeOffset.UtcNow)
            },
            Intent = new IntentAnalysis(IntentType.Unknown, string.Empty),
            ExecutionMode = TurnExecutionMode.Balanced
        };

        var assembly = await assembler.AssembleAsync(context, CancellationToken.None);

        Assert.DoesNotContain("aerodynamic stability", assembly.Prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Theoretical mechanics", assembly.Prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("structured_retrieval", assembly.UsedLayers);
        Assert.Contains(context.RetrievalTrace, trace => trace.Contains("retrieval_quality:discarded_low_fit", StringComparison.Ordinal));
    }

    [Fact]
    public void InMemoryConversationStore_GetRecentMessages_Uses_ProjectBoundaryFiltered_Legacy_Memory_Summaries()
    {
        var store = new InMemoryConversationStore(
            new MemoryPolicyService(),
            new ConversationSummarizer(),
            persistenceEngine: null,
            writeBehindQueue: null);
        var state = store.GetOrCreate("conv-boundary-prompt");
        state.ProjectContext = new ProjectContextState("project-a", "Project A", "Stay within project A.", MemoryEnabled: true, Array.Empty<string>(), DateTimeOffset.UtcNow);
        state.MemoryItems.Add(new ConversationMemoryItem(
            "pref-a",
            "long_term",
            "Prefer project A release notes.",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddDays(7),
            null,
            IsPersonal: false,
            Scope: MemoryScope.Project,
            Retention: "long_term_ttl",
            WhyRemembered: "explicit_remember_directive",
            Priority: 10,
            SourceProjectId: "project-a",
            UserEditable: true));
        state.MemoryItems.Add(new ConversationMemoryItem(
            "pref-b",
            "long_term",
            "Prefer project B migration notes.",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddDays(7),
            null,
            IsPersonal: false,
            Scope: MemoryScope.Project,
            Retention: "long_term_ttl",
            WhyRemembered: "explicit_remember_directive",
            Priority: 9,
            SourceProjectId: "project-b",
            UserEditable: true));
        state.MemoryItems.Add(new ConversationMemoryItem(
            "task-a",
            "task",
            "Review project A rollout gate.",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddDays(1),
            null,
            IsPersonal: false,
            Scope: MemoryScope.Project,
            Retention: "project_ttl",
            WhyRemembered: "project_task_signal",
            Priority: 8,
            SourceProjectId: "project-a",
            UserEditable: true));
        state.MemoryItems.Add(new ConversationMemoryItem(
            "task-b",
            "task",
            "Review project B rollout gate.",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddDays(1),
            null,
            IsPersonal: false,
            Scope: MemoryScope.Project,
            Retention: "project_ttl",
            WhyRemembered: "project_task_signal",
            Priority: 7,
            SourceProjectId: "project-b",
            UserEditable: true));
        store.AddMessage(state, new ChatMessageDto("user", "What should we do next?", DateTimeOffset.UtcNow, "turn-1", BranchId: "main"));

        var messages = store.GetRecentMessages(state, "main", 20);
        var prompt = string.Join("\n", messages.Select(message => message.Content));

        Assert.Contains("Prefer project A release notes.", prompt);
        Assert.Contains("Review project A rollout gate.", prompt);
        Assert.DoesNotContain("project B migration notes", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("project B rollout gate", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MemoryInspectionService_And_CaptureLogic_Respect_ProjectMemoryBoundary_Mode()
    {
        var state = new ConversationState("conv-boundary-memory")
        {
            ProjectContext = new ProjectContextState("project-a", "Project A", "Stay within project A.", MemoryEnabled: true, Array.Empty<string>(), DateTimeOffset.UtcNow)
        };
        state.MemoryItems.Add(new ConversationMemoryItem(
            "visible-project",
            "long_term",
            "Visible project fact.",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddDays(3),
            null,
            IsPersonal: false,
            Scope: MemoryScope.Project,
            Retention: "long_term_ttl",
            WhyRemembered: "explicit_remember_directive",
            Priority: 10,
            SourceProjectId: "project-a",
            UserEditable: true));
        state.MemoryItems.Add(new ConversationMemoryItem(
            "hidden-project",
            "long_term",
            "Hidden project fact.",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddDays(3),
            null,
            IsPersonal: false,
            Scope: MemoryScope.Project,
            Retention: "long_term_ttl",
            WhyRemembered: "explicit_remember_directive",
            Priority: 9,
            SourceProjectId: "project-b",
            UserEditable: true));

        var inspection = new MemoryInspectionService();
        var scopedSnapshot = inspection.BuildSnapshot(state, DateTimeOffset.UtcNow);

        Assert.Single(scopedSnapshot);
        Assert.Contains(scopedSnapshot, item => item.Content == "Visible project fact.");

        state.ProjectContext = state.ProjectContext with { MemoryEnabled = false };
        var memoryPolicy = new MemoryPolicyService();
        memoryPolicy.CaptureFromUserMessage(
            state,
            new ChatMessageDto("user", "Need to fix the rollout checklist.", DateTimeOffset.UtcNow, "turn-boundary"),
            DateTimeOffset.UtcNow);

        var unscopedTask = state.MemoryItems
            .Single(item => item.Type.Equals("task", StringComparison.OrdinalIgnoreCase) &&
                            item.Content.Contains("Need to fix", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(MemoryScope.Task, unscopedTask.Scope);
        Assert.Null(unscopedTask.SourceProjectId);

        var conversationWideSnapshot = inspection.BuildSnapshot(state, DateTimeOffset.UtcNow);
        Assert.Equal(4, conversationWideSnapshot.Count);
        Assert.Contains(conversationWideSnapshot, item => item.Content == "Hidden project fact.");
        Assert.Contains(conversationWideSnapshot, item => item.Content.Contains("Need to fix", StringComparison.OrdinalIgnoreCase));
    }

    private static KnowledgeChunk CreateRetrievedChunk(
        string id,
        string content,
        string domain,
        string topicalFitLabel,
        double topicalFitScore,
        bool genericDomain = false)
    {
        var collection = $"knowledge_{domain}_v2";
        return new KnowledgeChunk(
            id,
            content,
            Array.Empty<float>(),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["collection"] = collection,
                ["domain"] = domain,
                ["topical_fit_label"] = topicalFitLabel,
                ["topical_fit_score"] = topicalFitScore.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture),
                ["topical_fit_generic_domain"] = genericDomain ? "true" : "false",
                ["document_id"] = id,
                ["section_path"] = "chapter.1",
                ["page_start"] = "2"
            },
            collection);
    }

    private static KnowledgeChunk CreateSourceDominatedChunk(
        string id,
        string content,
        string sourcePath,
        string collection,
        bool guardApplied = false)
    {
        return new KnowledgeChunk(
            id,
            content,
            Array.Empty<float>(),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["collection"] = collection,
                ["domain"] = "robotics",
                ["source_path"] = sourcePath,
                ["document_id"] = id,
                ["section_path"] = "chapter.1",
                ["page_start"] = "4",
                ["source_diversity_guard_applied"] = guardApplied ? "true" : "false"
            },
            collection);
    }
}

