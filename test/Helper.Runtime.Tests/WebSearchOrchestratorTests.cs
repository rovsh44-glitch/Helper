using Helper.Api.Backend.Application;
using Helper.Api.Conversation;
using Helper.Api.Hosting;
using Helper.Runtime.Core;
using Helper.Runtime.Infrastructure;
using Helper.Runtime.WebResearch;
using Moq;

namespace Helper.Runtime.Tests;

public sealed class WebSearchOrchestratorTests
{
    [Fact]
    public async Task ExecuteAsync_UsesCacheAndSkipsSecondResearchCall()
    {
        var research = new Mock<IResearchService>();
        research
            .Setup(service => service.ResearchAsync(
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<Action<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string topic, int _, Action<string>? __, CancellationToken ___) => new ResearchResult(
                topic,
                "summary",
                new List<string> { "https://example.org/report" },
                new List<string>(),
                "Research report",
                DateTime.UtcNow,
                EvidenceItems: new[]
                {
                    new ResearchEvidenceItem(1, "https://example.org/report", "Report", "Evidence snippet")
                }));

        var orchestrator = new WebSearchOrchestrator(
            research.Object,
            new ShortHorizonResearchCache(),
            new SourceNormalizationService());

        var first = CreateResearchContext("latest observability guidance");
        var second = CreateResearchContext("latest observability guidance");

        await orchestrator.ExecuteAsync(first, CancellationToken.None);
        await orchestrator.ExecuteAsync(second, CancellationToken.None);

        Assert.Equal("Research report", first.ExecutionOutput);
        Assert.Equal("Research report", second.ExecutionOutput);
        Assert.Contains("web_search:live_fetch", first.IntentSignals);
        Assert.Contains("web_search:cache_hit", second.IntentSignals);
        Assert.Contains("web_cache.state=fresh", second.RetrievalTrace);
        Assert.Single(second.Sources);
        Assert.Single(second.ResearchEvidenceItems);
        research.Verify(service => service.ResearchAsync(
            It.IsAny<string>(),
            It.IsAny<int>(),
            It.IsAny<Action<string>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_NormalizesDuplicateSources_AndCarriesAlerts()
    {
        var research = new Mock<IResearchService>();
        research
            .Setup(service => service.ResearchAsync(
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<Action<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResearchResult(
                "topic",
                "summary",
                new List<string>
                {
                    "https://example.org/report",
                    "https://example.org/report/"
                },
                new List<string>(),
                "Research report",
                DateTime.UtcNow,
                SearchTrace: new[]
                {
                    "web_search.outcome=iterative_live_results",
                    "web_search.iteration_count=2"
                }));

        var orchestrator = new WebSearchOrchestrator(
            research.Object,
            new ShortHorizonResearchCache(),
            new SourceNormalizationService());

        var context = CreateResearchContext("latest guidance");
        await orchestrator.ExecuteAsync(context, CancellationToken.None);

        Assert.Single(context.Sources);
        Assert.Contains("source_contradiction:example.org/report", context.UncertaintyFlags);
        Assert.Contains("web_search.iteration_count=2", context.RetrievalTrace);
    }

    [Fact]
    public async Task ExecuteAsync_RefreshesAgingWebRequiredCacheBeforeUse()
    {
        var research = new Mock<IResearchService>();
        research
            .Setup(service => service.ResearchAsync(
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<Action<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResearchResult(
                "topic",
                "summary",
                new List<string> { "https://example.org/live" },
                new List<string>(),
                "Live refreshed report",
                DateTime.UtcNow));

        var cache = new ShortHorizonResearchCache();
        cache.Seed(
            "What is the current price of BTC today?",
            new ResearchResult(
                "topic",
                "summary",
                new List<string> { "https://example.org/cached" },
                new List<string>(),
                "Cached aging report",
                DateTime.UtcNow.AddMinutes(-12)),
            DateTimeOffset.UtcNow.AddMinutes(-12),
            categoryHint: "finance");

        var orchestrator = new WebSearchOrchestrator(
            research.Object,
            cache,
            new SourceNormalizationService());

        var context = CreateResearchContext("What is the current price of BTC today?", liveWebRequirement: "web_required", liveWebReason: "finance");
        await orchestrator.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal("Live refreshed report", context.ExecutionOutput);
        Assert.DoesNotContain("Cached aging report", context.ExecutionOutput, StringComparison.Ordinal);
        Assert.Contains("web_search:live_fetch", context.IntentSignals);
        Assert.Contains("web_cache.write_state=fresh", context.RetrievalTrace);
        research.Verify(service => service.ResearchAsync(
            It.IsAny<string>(),
            It.IsAny<int>(),
            It.IsAny<Action<string>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_DisclosesStaleCacheWhenRefreshFails()
    {
        var research = new Mock<IResearchService>();
        research
            .Setup(service => service.ResearchAsync(
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<Action<string>>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("search backend unavailable"));

        var cache = new ShortHorizonResearchCache();
        cache.Seed(
            "What is the current price of BTC today?",
            new ResearchResult(
                "topic",
                "summary",
                new List<string> { "https://example.org/cached" },
                new List<string>(),
                "Cached stale report",
                DateTime.UtcNow.AddHours(-2)),
            DateTimeOffset.UtcNow.AddHours(-2),
            categoryHint: "finance");

        var orchestrator = new WebSearchOrchestrator(
            research.Object,
            cache,
            new SourceNormalizationService());

        var context = CreateResearchContext("What is the current price of BTC today?", liveWebRequirement: "web_required", liveWebReason: "finance");
        context.ResolvedTurnLanguage = "en";

        await orchestrator.ExecuteAsync(context, CancellationToken.None);

        Assert.Contains("Cached stale report", context.ExecutionOutput, StringComparison.Ordinal);
        Assert.Contains("Freshness note:", context.ExecutionOutput, StringComparison.Ordinal);
        Assert.Contains("rechecked online", context.ExecutionOutput, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("web_cache.state=stale", context.RetrievalTrace);
        Assert.Contains("web_cache.refresh_failed=yes", context.RetrievalTrace);
        Assert.Contains("web_cache_refresh_failed_fallback", context.UncertaintyFlags);
        Assert.Contains("web_search:cache_hit", context.IntentSignals);
    }

    [Fact]
    public async Task ExecuteAsync_ReusesConversationSearchSession_ForFollowUpResearch()
    {
        var observedQueries = new List<string>();
        var research = new Mock<IResearchService>();
        research
            .Setup(service => service.ResearchAsync(
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<Action<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string topic, int _, Action<string>? __, CancellationToken ___) =>
            {
                observedQueries.Add(topic);
                return new ResearchResult(
                    topic,
                    "summary",
                    new List<string> { "https://reuters.com/world/climate-pact" },
                    new List<string>(),
                    "Follow-up report",
                    DateTime.UtcNow,
                    EvidenceItems: new[]
                    {
                        new ResearchEvidenceItem(
                            1,
                            "https://reuters.com/world/climate-pact",
                            "Leaders sign climate pact - Reuters",
                            "Evidence snippet",
                            EvidenceKind: "fetched_page",
                            Passages: new[]
                            {
                                new EvidencePassage(
                                    "p1",
                                    1,
                                    1,
                                    "1:p1",
                                    "https://reuters.com/world/climate-pact",
                                    "Leaders sign climate pact - Reuters",
                                    "2026-03-21",
                                    "Evidence snippet",
                                    "fetched_page")
                            })
                    },
                    SearchTrace: new[] { "web_search.outcome=live_results" });
            });

        var conversation = new ConversationState("conv-web-search");
        var orchestrator = new WebSearchOrchestrator(
            research.Object,
            new ShortHorizonResearchCache(),
            new SourceNormalizationService());

        var first = CreateResearchContext("latest climate pact announcement", conversation);
        var second = CreateResearchContext("what about the Reuters coverage?", conversation);

        await orchestrator.ExecuteAsync(first, CancellationToken.None);
        await orchestrator.ExecuteAsync(second, CancellationToken.None);

        Assert.Equal(2, observedQueries.Count);
        Assert.Equal("latest climate pact announcement", observedQueries[0]);
        Assert.Contains("latest climate pact announcement", observedQueries[1], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Reuters", observedQueries[1], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("coverage", observedQueries[1], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("web_search:session_reuse", second.IntentSignals);
        Assert.Contains(second.RetrievalTrace, line => line.Contains("search_session.reuse=yes", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(second.RetrievalTrace, line => line.Contains("citation_lineage.reused=1", StringComparison.OrdinalIgnoreCase));
        Assert.True(conversation.SearchSessions.TryGetValue("main", out var session));
        Assert.NotNull(session);
        Assert.Equal("latest climate pact announcement", session.RootQuery);
        Assert.Equal(second.TurnId, session.LastTurnId);
        Assert.True(session.ContinuationDepth >= 1);
        Assert.NotEmpty(session.EffectiveEvidenceMemory);
        Assert.Contains(second.RetrievalTrace, line => line.Contains("evidence_memory.selected=", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ExecuteAsync_RewritesLocalRecommendationQuery_UsingExplicitProfileLocalityHint()
    {
        var observedQueries = new List<string>();
        var research = new Mock<IResearchService>();
        research
            .Setup(service => service.ResearchAsync(
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<Action<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string topic, int _, Action<string>? __, CancellationToken ___) =>
            {
                observedQueries.Add(topic);
                return new ResearchResult(
                    topic,
                    "summary",
                    new List<string> { "https://example.org/local-guide" },
                    new List<string>(),
                    "Local guide",
                    DateTime.UtcNow);
            });

        var conversation = new ConversationState("conv-locality")
        {
            SearchLocalityHint = "Tashkent"
        };
        var orchestrator = new WebSearchOrchestrator(
            research.Object,
            new ShortHorizonResearchCache(),
            new SourceNormalizationService());

        var context = CreateResearchContext("best restaurant near me", conversation);

        await orchestrator.ExecuteAsync(context, CancellationToken.None);

        Assert.Single(observedQueries);
        Assert.Equal("best restaurant in Tashkent", observedQueries[0]);
        Assert.Contains(context.RetrievalTrace, line => line.Contains("web_query.locality applied=yes", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_PreservesVoiceNormalizedRootQuery_ForTextFollowUpResearch()
    {
        var observedQueries = new List<string>();
        var research = new Mock<IResearchService>();
        research
            .Setup(service => service.ResearchAsync(
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<Action<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string topic, int _, Action<string>? __, CancellationToken ___) =>
            {
                observedQueries.Add(topic);
                return new ResearchResult(
                    topic,
                    "summary",
                    new List<string> { "https://reuters.com/world/climate-pact" },
                    new List<string>(),
                    "Voice follow-up report",
                    DateTime.UtcNow,
                    EvidenceItems: new[]
                    {
                        new ResearchEvidenceItem(1, "https://reuters.com/world/climate-pact", "Reuters", "Evidence snippet")
                    });
            });

        var conversation = new ConversationState("conv-voice-followup");
        var orchestrator = new WebSearchOrchestrator(
            research.Object,
            new ShortHorizonResearchCache(),
            new SourceNormalizationService());

        var voice = CreateResearchContext(
            "Hey Helper, can you look up latest climate pact announcement please",
            conversation,
            inputMode: "voice");
        var text = CreateResearchContext(
            "what about the Reuters coverage?",
            conversation);

        await orchestrator.ExecuteAsync(voice, CancellationToken.None);
        await orchestrator.ExecuteAsync(text, CancellationToken.None);

        Assert.Equal(2, observedQueries.Count);
        Assert.Equal("latest climate pact announcement", observedQueries[0]);
        Assert.Contains("latest climate pact announcement", observedQueries[1], StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Hey Helper", observedQueries[1], StringComparison.OrdinalIgnoreCase);
        Assert.Contains(voice.RetrievalTrace, line => line.Contains("search_session.input_mode=voice", StringComparison.OrdinalIgnoreCase));
        Assert.True(conversation.SearchSessions.TryGetValue("main", out var session));
        Assert.NotNull(session);
        Assert.Equal("latest climate pact announcement", session.RootQuery);
        Assert.Equal("text", session.LastInputMode);
    }

    [Fact]
    public async Task ExecuteAsync_UsesLocalBaselineService_ForBenchmarkLocalOnlyTurn()
    {
        var research = new Mock<IResearchService>(MockBehavior.Strict);
        var localBaseline = new Mock<ILocalBaselineAnswerService>();
        localBaseline
            .Setup(service => service.GenerateAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("Данные по теме можно объяснить на уровне устойчивого базового каркаса без live web-поиска. Уровень неопределённости здесь ограничен пределами локального знания. Моё мнение: для базового объяснения этого достаточно.");

        var orchestrator = new WebSearchOrchestrator(
            research.Object,
            new ShortHorizonResearchCache(),
            new SourceNormalizationService(),
            localBaselineAnswerService: localBaseline.Object);

        var context = CreateResearchContext(
            "Объясни простыми словами, чем отличаются аэробные и анаэробные нагрузки.",
            liveWebRequirement: "no_web_needed");
        context.IsLocalFirstBenchmarkTurn = true;
        context.LocalFirstBenchmarkMode = LocalFirstBenchmarkMode.LocalOnly;

        await orchestrator.ExecuteAsync(context, CancellationToken.None);

        Assert.Contains("research.local_baseline", context.ToolCalls);
        Assert.DoesNotContain("research.search", context.ToolCalls);
        Assert.Contains("benchmark.local_first.mode=local_only", context.RetrievalTrace);
        Assert.Contains("web_search.skipped reason=benchmark_local_only", context.RetrievalTrace);
        Assert.Contains("Данные по теме можно объяснить", context.ExecutionOutput, StringComparison.Ordinal);
        research.VerifyNoOtherCalls();
        localBaseline.Verify(service => service.GenerateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_UsesDetailedLocalBaselineDiagnostics_ForBenchmarkLocalOnlyTurn()
    {
        var research = new Mock<IResearchService>(MockBehavior.Strict);
        var localBaseline = new DetailedLocalBaselineService();

        var orchestrator = new WebSearchOrchestrator(
            research.Object,
            new ShortHorizonResearchCache(),
            new SourceNormalizationService(),
            localBaselineAnswerService: localBaseline);

        var context = CreateResearchContext(
            "Объясни простыми словами, чем отличаются аэробные и анаэробные нагрузки.",
            liveWebRequirement: "no_web_needed");
        context.IsLocalFirstBenchmarkTurn = true;
        context.LocalFirstBenchmarkMode = LocalFirstBenchmarkMode.LocalOnly;

        await orchestrator.ExecuteAsync(context, CancellationToken.None);

        Assert.Contains(@"C:\LIB\Training Basics.pdf", context.Sources);
        Assert.Contains("local_retrieval.mode=hybrid_rrf", context.RetrievalTrace);
        Assert.Single(context.ResearchEvidenceItems);
        Assert.Equal("local_library_chunk", context.ResearchEvidenceItems[0].EvidenceKind);
        Assert.True(context.Conversation.SearchSessions.TryGetValue("main", out var session));
        Assert.NotNull(session);
        Assert.NotEmpty(session.EffectiveEvidenceMemory);
        research.VerifyNoOtherCalls();
    }

    private static ChatTurnContext CreateResearchContext(
        string message,
        ConversationState? conversation = null,
        string liveWebRequirement = "web_helpful",
        string? liveWebReason = null,
        string? inputMode = null)
    {
        return new ChatTurnContext
        {
            TurnId = Guid.NewGuid().ToString("N"),
            Request = new ChatRequestDto(message, "conv-web-search", 10, null, InputMode: inputMode),
            Conversation = conversation ?? new ConversationState("conv-web-search"),
            History = Array.Empty<ChatMessageDto>(),
            Intent = new IntentAnalysis(IntentType.Research, "test-model"),
            ResolvedLiveWebRequirement = liveWebRequirement,
            ResolvedLiveWebReason = liveWebReason
        };
    }

    private sealed class DetailedLocalBaselineService : ILocalBaselineAnswerService, ILocalBaselineAnswerDiagnostics
    {
        public Task<string> GenerateAsync(string topic, CancellationToken ct = default)
            => Task.FromResult("fallback");

        public Task<LocalBaselineAnswerResult> GenerateDetailedAsync(string topic, CancellationToken ct = default)
        {
            return Task.FromResult(new LocalBaselineAnswerResult(
                "Локальная библиотека показывает, что аэробные нагрузки связаны с более длительной умеренной работой, а анаэробные — с короткими интенсивными усилиями [1]. Моё мнение: этого local-first ответа достаточно для базового объяснения.",
                new[] { @"C:\LIB\Training Basics.pdf" },
                new[] { "local_retrieval.mode=hybrid_rrf", "local_retrieval.chunk_count=1" },
                new[]
                {
                    new ResearchEvidenceItem(
                        1,
                        @"C:\LIB\Training Basics.pdf",
                        "Training Basics.pdf",
                        "Аэробные нагрузки связаны с более длительной умеренной работой, а анаэробные — с короткими интенсивными усилиями.",
                        TrustLevel: "local_library",
                        EvidenceKind: "local_library_chunk")
                }));
        }
    }
}

