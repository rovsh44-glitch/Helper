using Helper.Runtime.Core;
using Helper.Runtime.Infrastructure;
using Helper.Runtime.Knowledge.Retrieval;
using Moq;

namespace Helper.Runtime.Tests;

public sealed class HybridLocalRetrievalTests
{
    [Fact]
    public async Task ContextAssemblyService_HybridRetrieval_CanRecoverSparseRelevantChunk()
    {
        ContextAssemblyService.ResetCollectionProfilesForTesting();

        var vectorStore = new Mock<IVectorStore>();
        var structuredStore = new Mock<IStructuredVectorStore>();
        var ai = new Mock<AILink>("http://localhost:11434", "qwen");

        ai.Setup(x => x.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new float[768]);

        structuredStore.Setup(x => x.ListCollectionsAsync("knowledge_", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { "knowledge_medicine_v2" });
        structuredStore.Setup(x => x.GetCollectionPointCountAsync("knowledge_medicine_v2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(12000);

        vectorStore.Setup(x => x.SearchAsync(It.IsAny<float[]>(), "knowledge_medicine_v2", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<KnowledgeChunk>
            {
                CreateChunk(
                    "vector-generic",
                    "Общий физиологический материал о сердечном выбросе и регуляции артериального давления.",
                    "doc-generic",
                    "General Physiology.pdf",
                    0.96,
                    domain: "medicine")
            });

        vectorStore.Setup(x => x.ScrollMetadataAsync("knowledge_medicine_v2", It.IsAny<int>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<KnowledgeChunk>
            {
                CreateChunk(
                    "sparse-relevant",
                    "Профилактика мигрени обычно включает контроль триггеров, нормализацию сна и обсуждение профилактической терапии при частых приступах.",
                    "doc-migraine",
                    "Clinical Migraine Handbook.pdf",
                    0d,
                    domain: "medicine"),
                CreateChunk(
                    "sparse-noise",
                    "Нейтральный обзор общих жалоб в неврологии.",
                    "doc-noise",
                    "Neurology Notes.pdf",
                    0d,
                    domain: "medicine")
            });

        var service = new ContextAssemblyService(vectorStore.Object, structuredStore.Object, ai.Object, new RerankingService());

        var results = await service.AssembleAsync(
            "Как обычно строят профилактику мигрени?",
            domain: null,
            limit: 2,
            pipelineVersion: "v2",
            expandContext: false,
            ct: CancellationToken.None,
            options: new RetrievalRequestOptions(Purpose: RetrievalPurpose.ReasoningSupport, PreferTraceableChunks: true));

        Assert.NotEmpty(results);
        Assert.Equal("doc-migraine", results[0].Metadata["document_id"]);
        Assert.Equal("true", results[0].Metadata["hybrid_rrf_active"]);
        Assert.Contains(results[0].Metadata["retrieval_channel"], new[] { "sparse", "hybrid" });
    }

    [Fact]
    public async Task LocalBaselineAnswerService_GenerateDetailedAsync_UsesLocalLibraryEvidence()
    {
        var retrieval = new Mock<IRetrievalContextAssembler>();
        retrieval
            .Setup(service => service.AssembleAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<RetrievalRequestOptions?>()))
            .ReturnsAsync(new[]
            {
                CreateChunk(
                    "chunk-1",
                    "Красный свет в спортивном восстановлении изучается, но сила доказательств сильно зависит от протокола и дизайна исследования.",
                    "doc-red-light",
                    "Sports Recovery Review.pdf",
                    0.80,
                    domain: "medicine"),
                CreateChunk(
                    "chunk-2",
                    "Для уверенных выводов нужны более крупные и лучше контролируемые исследования.",
                    "doc-red-light",
                    "Sports Recovery Review.pdf",
                    0.76,
                    domain: "medicine")
            });

        var ai = new CapturingAiLink("По локальной библиотеке [1] видно, что данные о красном свете для восстановления после тренировок пока неоднородны, а [1] также подчёркивает зависимость результата от протокола. Это даёт полезный локальный каркас, но не подтверждает сильный практический эффект без более качественных исследований. Моё мнение: относиться к теме стоит осторожно, как к потенциально интересной, но ещё не надёжно закрытой.");
        var service = new LocalBaselineAnswerService(ai, retrieval.Object);

        var result = await ((ILocalBaselineAnswerDiagnostics)service).GenerateDetailedAsync(
            "Объясни, насколько убедительны данные о пользе терапии красным светом для восстановления после тренировок.",
            CancellationToken.None);

        Assert.Contains("локальной библиотеки", ai.LastPrompt!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Sports Recovery Review.pdf", ai.LastPrompt!, StringComparison.Ordinal);
        Assert.Contains("local_retrieval.mode=hybrid_rrf", result.Trace);
        Assert.Single(result.Sources);
        Assert.DoesNotContain(@"C:\LIB\", result.Sources[0], StringComparison.OrdinalIgnoreCase);
        Assert.Single(result.EvidenceItems);
        Assert.Equal("local_library_chunk", result.EvidenceItems[0].EvidenceKind);
        Assert.Equal("local_library", result.EvidenceItems[0].SourceLayer);
        Assert.Equal("pdf", result.EvidenceItems[0].SourceFormat);
        Assert.Equal("Sports Recovery Review.pdf", result.EvidenceItems[0].DisplayTitle);
        Assert.Contains("page:1", result.EvidenceItems[0].Locator, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Моё мнение:", result.Answer, StringComparison.Ordinal);
    }

    private static KnowledgeChunk CreateChunk(string id, string content, string documentId, string title, double vectorScore, string domain)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["document_id"] = documentId,
            ["title"] = title,
            ["source_path"] = $@"C:\LIB\{title}",
            ["vector_score"] = vectorScore.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["chunk_role"] = "standalone",
            ["page_start"] = "1",
            ["section_path"] = "chapter",
            ["domain"] = domain,
            ["collection"] = $"knowledge_{domain}_v2"
        };

        return new KnowledgeChunk(
            id,
            content,
            Array.Empty<float>(),
            metadata,
            $"knowledge_{domain}_v2");
    }

    private sealed class CapturingAiLink : AILink
    {
        private readonly string _response;

        public CapturingAiLink(string response)
            : base("http://localhost:11434", "test-model")
        {
            _response = response;
        }

        public string? LastPrompt { get; private set; }

        public override Task<string> AskAsync(string prompt, CancellationToken ct, string? overrideModel = null, string? base64Image = null, int keepAliveSeconds = 300, string? systemInstruction = null)
        {
            LastPrompt = prompt;
            return Task.FromResult(_response);
        }

        public override IAsyncEnumerable<string> StreamAsync(string prompt, string? overrideModel = null, string? base64Image = null, int keepAliveSeconds = 300, string? systemInstruction = null, CancellationToken ct = default)
            => Empty(ct);

        private static async IAsyncEnumerable<string> Empty([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            await Task.Yield();
            ct.ThrowIfCancellationRequested();
            yield break;
        }
    }
}

