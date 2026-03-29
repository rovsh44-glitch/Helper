using Helper.Runtime.Core;
using Helper.Runtime.Infrastructure;
using Helper.Runtime.WebResearch;
using System.Runtime.CompilerServices;

namespace Helper.Runtime.Tests;

public sealed class SimpleResearcherTests
{
    [Fact]
    public async Task ResearchAsync_UsesHonestFallback_WhenSearchIsUnavailable()
    {
        var researcher = new SimpleResearcher(
            new BlankAiLink(),
            new NoopCodeExecutor(),
            new EmptyWebSearcher(),
            new NoopVectorStore());

        var topic = "Проанализируй статью и предоставь своё мнение: https://www.kaggle.com/competitions/kaggle-measuring-agi";
        var result = await researcher.ResearchAsync(topic, ct: CancellationToken.None);

        Assert.Contains("could not reliably read enough of the referenced document", result.FullReport, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("document body was not retrieved strongly enough", result.FullReport, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("My view:", result.FullReport, StringComparison.Ordinal);
        Assert.Contains("https://kaggle.com/competitions/kaggle-measuring-agi", result.Sources);
        Assert.DoesNotContain("learn.microsoft.com", string.Join("\n", result.Sources), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("redundancy across critical components", result.FullReport, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Research request:", result.FullReport, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ResearchAsync_BuildsSourceSpecificFallback_WhenEvidenceIsAvailable()
    {
        var researcher = new SimpleResearcher(
            new BlankAiLink(),
            new NoopCodeExecutor(),
            new StubWebSearcher(
                new WebSearchResult(
                    "https://learn.microsoft.com/retries",
                    "Retry guidance",
                    "Retries should focus on transient faults and use bounded exponential backoff.",
                    false),
                new WebSearchResult(
                    "https://aws.amazon.com/timeouts",
                    "Timeout guidance",
                    "Timeouts should be coordinated with retries so the client does not amplify overload.",
                    false)),
            new NoopVectorStore());

        var result = await researcher.ResearchAsync("Compare retry guidance across cloud reliability docs.", ct: CancellationToken.None);

        Assert.NotNull(result.EvidenceItems);
        Assert.Equal(2, result.EvidenceItems!.Count);
        Assert.Contains("Retry guidance [1]", result.FullReport, StringComparison.Ordinal);
        Assert.Contains("while Timeout guidance [2]", result.FullReport, StringComparison.Ordinal);
        Assert.DoesNotContain("Overview", result.FullReport, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Key Takeaways", result.FullReport, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[1] Retry guidance", result.RawEvidence, StringComparison.Ordinal);
        Assert.NotNull(result.SearchTrace);
        Assert.Contains(result.SearchTrace!, line => line.Contains("web_search.iteration_count=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ResearchAsync_UsesLocalBaselineAnswer_WhenWebEvidenceIsMissing_ForNonDocumentQuery()
    {
        var researcher = new SimpleResearcher(
            new ReturningAiLink("Профилактику мигрени обычно строят ступенчато: сначала уменьшают триггеры и нормализуют сон, затем добавляют дневник симптомов и обсуждают профилактические препараты, если приступы частые. Без внешней проверки я не могу подтвердить, что именно изменилось в самых последних рекомендациях, но базовая логика профилактики остаётся такой. My view: для текущих уточнений нужны свежие рекомендации, но общий каркас профилактики описан корректно."),
            new NoopCodeExecutor(),
            new EmptyWebSearcher(),
            new NoopVectorStore());

        var result = await researcher.ResearchAsync(
            "Объясни, как обычно строят профилактику мигрени, а затем проверь, что изменилось или уточнилось в последних клинических рекомендациях.",
            ct: CancellationToken.None);

        Assert.Contains("Профилактику мигрени обычно строят", result.FullReport, StringComparison.Ordinal);
        Assert.Contains("не могу подтвердить, что именно изменилось в самых последних рекомендациях", result.FullReport, StringComparison.Ordinal);
        Assert.Contains("My view:", result.FullReport, StringComparison.Ordinal);
        Assert.DoesNotContain("No verifiable sources were retrieved", result.FullReport, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("current runtime", result.FullReport, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ResearchAsync_RejectsFabricatedPlaceholderBaseline_WhenWebEvidenceIsMissing()
    {
        var researcher = new SimpleResearcher(
            new ReturningAiLink("## Local Findings\nAerobics and anatheldral training differ.\n\n## Web Findings\n[Link 1](https://example.com)\n\n## Sources\n- Local Library Resources\n\n## Analysis\nBoth are useful.\n\n## Conclusion\nCombine them.\n\n## Opinion\nMy view: combine them."),
            new NoopCodeExecutor(),
            new EmptyWebSearcher(),
            new NoopVectorStore());

        var result = await researcher.ResearchAsync(
            "Объясни простыми словами, чем отличаются аэробные и анаэробные нагрузки и когда нужны оба типа тренировок.",
            ct: CancellationToken.None);

        Assert.DoesNotContain("https://example.com", result.FullReport, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Local Library Resources", result.FullReport, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("аэробные", result.FullReport, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Моё мнение:", result.FullReport, StringComparison.Ordinal);
        Assert.DoesNotContain("current runtime", result.FullReport, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ResearchAsync_UsesRussianLocalBaselinePrompt_ForRussianTopics()
    {
        var ai = new CapturingAiLink();
        var researcher = new SimpleResearcher(
            ai,
            new NoopCodeExecutor(),
            new EmptyWebSearcher(),
            new NoopVectorStore());

        await researcher.ResearchAsync(
            "Объясни, насколько убедительны данные о пользе терапии красным светом для восстановления после тренировок.",
            ct: CancellationToken.None);

        Assert.NotNull(ai.LastPrompt);
        Assert.Contains(ai.Prompts, prompt => prompt.Contains("Отвечай по-русски.", StringComparison.Ordinal));
        Assert.Contains(ai.Prompts, prompt => prompt.Contains("Не выдумывай URL", StringComparison.Ordinal));
        Assert.Contains(ai.Prompts, prompt => prompt.Contains("Моё мнение:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ResearchAsync_CarriesDetailedLocalBaselineSources_AndTrace_WhenWebEvidenceIsMissing()
    {
        var researcher = new SimpleResearcher(
            new BlankAiLink(),
            new NoopCodeExecutor(),
            new NoopVectorStore(),
            new StubSearchSessionCoordinator(new WebSearchSession(
                new WebSearchRequest("Объясни простыми словами, чем отличаются аэробные и анаэробные нагрузки."),
                new WebSearchPlan("Объясни простыми словами, чем отличаются аэробные и анаэробные нагрузки.", 5, 1, "research", "standard", true),
                new WebSearchResultBundle(
                    "Объясни простыми словами, чем отличаются аэробные и анаэробные нагрузки.",
                    Array.Empty<WebSearchDocument>(),
                    Array.Empty<string>(),
                    UsedDeterministicFallback: false,
                    Outcome: "no_results"),
                DateTimeOffset.UtcNow.AddSeconds(-1),
                DateTimeOffset.UtcNow)),
            new DetailedLocalBaselineStub());

        var result = await researcher.ResearchAsync(
            "Объясни простыми словами, чем отличаются аэробные и анаэробные нагрузки.",
            ct: CancellationToken.None);

        Assert.Contains(@"C:\LIB\Training Basics.pdf", result.Sources);
        Assert.NotNull(result.SearchTrace);
        Assert.Contains(result.SearchTrace!, line => line.Contains("local_retrieval.mode=hybrid_rrf", StringComparison.Ordinal));
        Assert.NotNull(result.EvidenceItems);
        Assert.Single(result.EvidenceItems!);
        Assert.Equal("local_library_chunk", result.EvidenceItems[0].EvidenceKind);
    }

    [Fact]
    public async Task ResearchAsync_RepairsRejectedLocalBaseline_WhenFirstDraftIsLowQuality()
    {
        var researcher = new SimpleResearcher(
            new QueuedAiLink(
                "## Local Findings\nAerobics and anatheldral training differ.\n\n## Sources\n[Link 1](https://example.com)\n\n## Opinion\nMy view: combine them.",
                "По устойчивому базовому знанию аэробные нагрузки в основном связаны с более длительной работой умеренной интенсивности, а анаэробные — с короткими интенсивными усилиями. Оба типа обычному человеку полезны вместе: первый поддерживает выносливость и здоровье сердца, второй помогает сохранять силу и мышечную массу. Степень неопределённости здесь низкая для общего каркаса, но точные схемы тренировок уже нужно подбирать под возраст, здоровье и цель. Моё мнение: для повседневной практики разумнее сочетать оба типа нагрузок, а не противопоставлять их."),
            new NoopCodeExecutor(),
            new EmptyWebSearcher(),
            new NoopVectorStore());

        var result = await researcher.ResearchAsync(
            "Объясни простыми словами, чем отличаются аэробные и анаэробные нагрузки и когда нужны оба типа тренировок.",
            ct: CancellationToken.None);

        Assert.Contains("аэробные нагрузки", result.FullReport, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Степень неопределённости", result.FullReport, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Моё мнение:", result.FullReport, StringComparison.Ordinal);
        Assert.DoesNotContain("https://example.com", result.FullReport, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("current runtime", result.FullReport, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ResearchAsync_UsesDeterministicLocalBaselineFallback_WhenModelDoesNotProduceUsableAnswer()
    {
        var researcher = new SimpleResearcher(
            new BlankAiLink(),
            new NoopCodeExecutor(),
            new EmptyWebSearcher(),
            new NoopVectorStore());

        var result = await researcher.ResearchAsync(
            "Объясни, насколько убедительны данные о пользе терапии красным светом для восстановления после тренировок.",
            ct: CancellationToken.None);

        Assert.Contains("терапии красным светом", result.FullReport, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Степень неопределённости", result.FullReport, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Моё мнение:", result.FullReport, StringComparison.Ordinal);
        Assert.DoesNotContain("current runtime", result.FullReport, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("No verifiable sources were retrieved", result.FullReport, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ResearchAsync_UsesFetchedPageEvidence_WhenCoordinatorProvidesFullPages()
    {
        var researcher = new SimpleResearcher(
            new BlankAiLink(),
            new NoopCodeExecutor(),
            new NoopVectorStore(),
            new StubSearchSessionCoordinator(new WebSearchSession(
                new WebSearchRequest("Compare retry guidance"),
                new WebSearchPlan("Compare retry guidance", 5, 1, "research", "standard", true),
                new WebSearchResultBundle(
                    "Compare retry guidance",
                    new[]
                    {
                        new WebSearchDocument(
                            "https://example.org/retries",
                            "Retry guidance",
                            "Search snippet",
                            ExtractedPage: new ExtractedWebPage(
                                "https://example.org/retries?ref=search",
                                "https://example.org/retries",
                                "https://example.org/retries",
                                "Retry guidance",
                                "2026-03-20",
                                "This full page evidence explains bounded exponential backoff and retry budgets in production systems.",
                                new[]
                                {
                                    new ExtractedWebPassage(1, "This full page evidence explains bounded exponential backoff and retry budgets in production systems.")
                                },
                                "text/html"))
                    },
                    new[] { "https://example.org/retries" },
                    UsedDeterministicFallback: false,
                    Outcome: "live_results",
                    PageTrace: new[] { "web_page_fetch.extracted target=https://example.org/retries canonical=https://example.org/retries passages=1" }),
                DateTimeOffset.UtcNow.AddSeconds(-1),
                DateTimeOffset.UtcNow)));

        var result = await researcher.ResearchAsync("Compare retry guidance", ct: CancellationToken.None);

        Assert.NotNull(result.EvidenceItems);
        Assert.Single(result.EvidenceItems!);
        Assert.Contains("full page evidence explains bounded exponential backoff", result.EvidenceItems[0].Snippet, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("fetched_page", result.EvidenceItems[0].EvidenceKind);
        Assert.Equal("2026-03-20", result.EvidenceItems[0].PublishedAt);
        Assert.NotNull(result.EvidenceItems[0].Passages);
        Assert.Single(result.EvidenceItems[0].Passages!);
        Assert.Equal("1:p1", result.EvidenceItems[0].Passages![0].CitationLabel);
        Assert.Contains(result.SearchTrace!, line => line.Contains("web_page_fetch.extracted_count=1", StringComparison.Ordinal));
        Assert.Contains(result.SearchTrace!, line => line.Contains("web_page_fetch.extracted target=https://example.org/retries", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ResearchAsync_BuildsPromptWithUntrustedEvidenceBoundary_AndSanitizedContent()
    {
        var ai = new CapturingAiLink();
        var researcher = new SimpleResearcher(
            ai,
            new NoopCodeExecutor(),
            new NoopVectorStore(),
            new StubSearchSessionCoordinator(new WebSearchSession(
                new WebSearchRequest("deployment sequencing"),
                new WebSearchPlan("deployment sequencing", 5, 1, "research", "standard", true),
                new WebSearchResultBundle(
                    "deployment sequencing",
                    new[]
                    {
                        new WebSearchDocument(
                            "https://example.org/deploy",
                            "Deployment article",
                            "Search snippet",
                            ExtractedPage: new ExtractedWebPage(
                                "https://example.org/deploy",
                                "https://example.org/deploy",
                                "https://example.org/deploy",
                                "Deployment article",
                                "2026-03-21",
                                "[instruction-like text removed from untrusted web content] This page still explains rollout sequencing in a useful way.",
                                new[]
                                {
                                    new ExtractedWebPassage(
                                        1,
                                        "[instruction-like text removed from untrusted web content]",
                                        "untrusted_web_content",
                                        WasSanitized: true,
                                        SafetyFlags: new[] { "instruction_override", "system_prompt_reference" }),
                                    new ExtractedWebPassage(
                                        2,
                                        "This page still explains rollout sequencing in a useful way.",
                                        "untrusted_web_content",
                                        WasSanitized: false,
                                        SafetyFlags: Array.Empty<string>())
                                },
                                "text/html",
                                TrustLevel: "untrusted_web_content",
                                WasSanitized: true,
                                InjectionSignalsDetected: true,
                                SafetyFlags: new[] { "instruction_override", "system_prompt_reference" }))
                    },
                    new[] { "https://example.org/deploy" },
                    UsedDeterministicFallback: false,
                    Outcome: "live_results",
                    PageTrace: new[] { "web_evidence_boundary.injection_detected=yes" }),
                DateTimeOffset.UtcNow.AddSeconds(-1),
                DateTimeOffset.UtcNow)));

        await researcher.ResearchAsync("deployment sequencing", ct: CancellationToken.None);

        Assert.NotNull(ai.LastPrompt);
        Assert.Contains("Evidence boundary:", ai.LastPrompt!, StringComparison.Ordinal);
        Assert.Contains("BEGIN_UNTRUSTED_WEB_EVIDENCE", ai.LastPrompt!, StringComparison.Ordinal);
        Assert.Contains("Trust: untrusted_web_content | kind=fetched_page | sanitized=yes | date=2026-03-21 | flags=instruction_override,system_prompt_reference", ai.LastPrompt!, StringComparison.Ordinal);
        Assert.DoesNotContain("Ignore previous instructions", ai.LastPrompt!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[instruction-like text removed from untrusted web content]", ai.LastPrompt!, StringComparison.Ordinal);
        Assert.Contains("[p1] [instruction-like text removed from untrusted web content]", ai.LastPrompt!, StringComparison.Ordinal);
        Assert.Contains("[p2] This page still explains rollout sequencing in a useful way.", ai.LastPrompt!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResearchAsync_ReplacesChromeLikeAiOutput_WithDeterministicDocumentAnalysis()
    {
        var researcher = new SimpleResearcher(
            new ReturningAiLink("GitHub Advanced Security Enterprise platform GitHub Advanced Security Enterprise platform"),
            new NoopCodeExecutor(),
            new NoopVectorStore(),
            new StubSearchSessionCoordinator(new WebSearchSession(
                new WebSearchRequest("Проанализируй и предоставь своё мнение: https://example.org/paper.pdf"),
                new WebSearchPlan("Проанализируй и предоставь своё мнение: https://example.org/paper.pdf", 5, 1, "research", "standard", true),
                new WebSearchResultBundle(
                    "Проанализируй и предоставь своё мнение: https://example.org/paper.pdf",
                    new[]
                    {
                        new WebSearchDocument(
                            "https://example.org/paper.pdf",
                            "Attention Residuals",
                            "Attention residuals replace fixed residual accumulation in transformers.",
                            ExtractedPage: new ExtractedWebPage(
                                "https://example.org/paper.pdf",
                                "https://example.org/paper.pdf",
                                "https://example.org/paper.pdf",
                                "Attention Residuals",
                                "2026",
                                "Attention residuals replace fixed residual accumulation in transformers and report scaling improvements.",
                                new[]
                                {
                                    new ExtractedWebPassage(1, "Attention residuals replace fixed residual accumulation in transformers and report scaling improvements.")
                                },
                                "application/pdf"))
                    },
                    new[] { "https://example.org/paper.pdf" },
                    UsedDeterministicFallback: false,
                    Outcome: "direct_page_fetch"),
                DateTimeOffset.UtcNow.AddSeconds(-1),
                DateTimeOffset.UtcNow)));

        var result = await researcher.ResearchAsync(
            "Проанализируй и предоставь своё мнение: https://example.org/paper.pdf",
            ct: CancellationToken.None);

        Assert.DoesNotContain("GitHub Advanced Security", result.FullReport, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("My view:", result.FullReport, StringComparison.Ordinal);
        Assert.Contains("Main limitation:", result.FullReport, StringComparison.Ordinal);
        Assert.Contains("[1]", result.FullReport, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResearchAsync_BuildsDocumentAnalysisFallback_WithOpinionMarkers_WhenModelIsEmpty()
    {
        var researcher = new SimpleResearcher(
            new BlankAiLink(),
            new NoopCodeExecutor(),
            new NoopVectorStore(),
            new StubSearchSessionCoordinator(new WebSearchSession(
                new WebSearchRequest("Analyze this paper and give your opinion: https://example.org/paper.pdf"),
                new WebSearchPlan("Analyze this paper and give your opinion: https://example.org/paper.pdf", 5, 1, "research", "standard", true),
                new WebSearchResultBundle(
                    "Analyze this paper and give your opinion: https://example.org/paper.pdf",
                    new[]
                    {
                        new WebSearchDocument(
                            "https://example.org/paper.pdf",
                            "Attention Residuals",
                            "Attention residuals replace fixed residual accumulation in transformers.",
                            ExtractedPage: new ExtractedWebPage(
                                "https://example.org/paper.pdf",
                                "https://example.org/paper.pdf",
                                "https://example.org/paper.pdf",
                                "Attention Residuals",
                                "2026",
                                "Attention residuals replace fixed residual accumulation in transformers and report scaling improvements.",
                                new[]
                                {
                                    new ExtractedWebPassage(1, "Attention residuals replace fixed residual accumulation in transformers and report scaling improvements.")
                                },
                                "application/pdf"))
                    },
                    new[] { "https://example.org/paper.pdf" },
                    UsedDeterministicFallback: false,
                    Outcome: "direct_page_fetch"),
                DateTimeOffset.UtcNow.AddSeconds(-1),
                DateTimeOffset.UtcNow)));

        var result = await researcher.ResearchAsync(
            "Analyze this paper and give your opinion: https://example.org/paper.pdf",
            ct: CancellationToken.None);

        Assert.Contains("My view:", result.FullReport, StringComparison.Ordinal);
        Assert.Contains("Main limitation:", result.FullReport, StringComparison.Ordinal);
        Assert.Contains("Attention Residuals", result.FullReport, StringComparison.Ordinal);
    }

    private sealed class BlankAiLink : AILink
    {
        public BlankAiLink()
            : base("http://localhost:11434", "test-model")
        {
        }

        public override Task<string> AskAsync(string prompt, CancellationToken ct, string? overrideModel = null, string? base64Image = null, int keepAliveSeconds = 300, string? systemInstruction = null)
            => Task.FromResult(string.Empty);

        public override IAsyncEnumerable<string> StreamAsync(
            string prompt,
            string? overrideModel = null,
            string? base64Image = null,
            int keepAliveSeconds = 300,
            string? systemInstruction = null,
            CancellationToken ct = default)
            => EmptyStream(ct);

        private static async IAsyncEnumerable<string> EmptyStream([EnumeratorCancellation] CancellationToken ct)
        {
            await Task.Yield();
            ct.ThrowIfCancellationRequested();
            yield break;
        }
    }

    private sealed class CapturingAiLink : AILink
    {
        public CapturingAiLink()
            : base("http://localhost:11434", "test-model")
        {
        }

        public string? LastPrompt { get; private set; }
        public List<string> Prompts { get; } = new();

        public override Task<string> AskAsync(string prompt, CancellationToken ct, string? overrideModel = null, string? base64Image = null, int keepAliveSeconds = 300, string? systemInstruction = null)
        {
            LastPrompt = prompt;
            Prompts.Add(prompt);
            return Task.FromResult(string.Empty);
        }

        public override IAsyncEnumerable<string> StreamAsync(
            string prompt,
            string? overrideModel = null,
            string? base64Image = null,
            int keepAliveSeconds = 300,
            string? systemInstruction = null,
            CancellationToken ct = default)
            => EmptyCaptureStream(ct);

        private static async IAsyncEnumerable<string> EmptyCaptureStream([EnumeratorCancellation] CancellationToken ct)
        {
            await Task.Yield();
            ct.ThrowIfCancellationRequested();
            yield break;
        }
    }

    private sealed class ReturningAiLink : AILink
    {
        private readonly string _response;

        public ReturningAiLink(string response)
            : base("http://localhost:11434", "test-model")
        {
            _response = response;
        }

        public override Task<string> AskAsync(string prompt, CancellationToken ct, string? overrideModel = null, string? base64Image = null, int keepAliveSeconds = 300, string? systemInstruction = null)
            => Task.FromResult(_response);

        public override IAsyncEnumerable<string> StreamAsync(
            string prompt,
            string? overrideModel = null,
            string? base64Image = null,
            int keepAliveSeconds = 300,
            string? systemInstruction = null,
            CancellationToken ct = default)
            => EmptyResponseStream(ct);

        private static async IAsyncEnumerable<string> EmptyResponseStream([EnumeratorCancellation] CancellationToken ct)
        {
            await Task.Yield();
            ct.ThrowIfCancellationRequested();
            yield break;
        }
    }

    private sealed class QueuedAiLink : AILink
    {
        private readonly Queue<string> _responses;

        public QueuedAiLink(params string[] responses)
            : base("http://localhost:11434", "test-model")
        {
            _responses = new Queue<string>(responses);
        }

        public override Task<string> AskAsync(string prompt, CancellationToken ct, string? overrideModel = null, string? base64Image = null, int keepAliveSeconds = 300, string? systemInstruction = null)
            => Task.FromResult(_responses.Count > 0 ? _responses.Dequeue() : string.Empty);

        public override IAsyncEnumerable<string> StreamAsync(
            string prompt,
            string? overrideModel = null,
            string? base64Image = null,
            int keepAliveSeconds = 300,
            string? systemInstruction = null,
            CancellationToken ct = default)
            => EmptyQueuedStream(ct);

        private static async IAsyncEnumerable<string> EmptyQueuedStream([EnumeratorCancellation] CancellationToken ct)
        {
            await Task.Yield();
            ct.ThrowIfCancellationRequested();
            yield break;
        }
    }

    private sealed class NoopCodeExecutor : ICodeExecutor
    {
        public Task<ExecutionResult> ExecuteAsync(string code, string language = "python", CancellationToken ct = default)
            => Task.FromResult(new ExecutionResult(true, string.Empty, string.Empty, new List<string>()));
    }

    private sealed class EmptyWebSearcher : IWebSearcher
    {
        public Task<List<WebSearchResult>> SearchAsync(string query, CancellationToken ct = default)
            => Task.FromResult(new List<WebSearchResult>());
    }

    private sealed class StubWebSearcher : IWebSearcher
    {
        private readonly List<WebSearchResult> _results;

        public StubWebSearcher(params WebSearchResult[] results)
        {
            _results = results.ToList();
        }

        public Task<List<WebSearchResult>> SearchAsync(string query, CancellationToken ct = default)
            => Task.FromResult(_results.ToList());
    }

    private sealed class NoopVectorStore : IVectorStore
    {
        public Task UpsertAsync(KnowledgeChunk chunk, CancellationToken ct = default) => Task.CompletedTask;
        public Task<List<KnowledgeChunk>> SearchAsync(float[] queryEmbedding, string collection = HelperKnowledgeCollections.CanonicalDefault, int limit = 5, CancellationToken ct = default) => Task.FromResult(new List<KnowledgeChunk>());
        public Task<List<KnowledgeChunk>> SearchMetadataAsync(string key, string value, string collection, CancellationToken ct = default) => Task.FromResult(new List<KnowledgeChunk>());
        public Task<List<KnowledgeChunk>> ScrollMetadataAsync(string collection, int limit = 100, string? offset = null, CancellationToken ct = default) => Task.FromResult(new List<KnowledgeChunk>());
        public Task EnsureCollectionExistsAsync(string name, int dimensions, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeletePointsAsync(List<string> ids, string collection, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteByMetadataAsync(string key, string value, string collection, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteCollectionAsync(string collection, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class StubSearchSessionCoordinator : IWebSearchSessionCoordinator
    {
        private readonly WebSearchSession _session;

        public StubSearchSessionCoordinator(WebSearchSession session)
        {
            _session = session;
        }

        public Task<WebSearchSession> ExecuteAsync(WebSearchRequest request, CancellationToken ct = default)
            => Task.FromResult(_session);
    }

    private sealed class DetailedLocalBaselineStub : ILocalBaselineAnswerService, ILocalBaselineAnswerDiagnostics
    {
        public Task<string> GenerateAsync(string topic, CancellationToken ct = default)
            => Task.FromResult("fallback");

        public Task<LocalBaselineAnswerResult> GenerateDetailedAsync(string topic, CancellationToken ct = default)
        {
            return Task.FromResult(new LocalBaselineAnswerResult(
                "Локальная библиотека показывает, что аэробные нагрузки связаны с более длительной умеренной работой, а анаэробные — с короткими интенсивными усилиями [1]. Моё мнение: для базового объяснения этого локального материала достаточно.",
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

