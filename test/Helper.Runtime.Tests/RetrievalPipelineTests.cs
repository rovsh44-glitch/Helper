using Helper.Runtime.Core;
using Helper.Runtime.Infrastructure;
using Helper.Runtime.Knowledge.Retrieval;
using Moq;

namespace Helper.Runtime.Tests;

public class RetrievalPipelineTests
{
    public RetrievalPipelineTests()
    {
        ContextAssemblyService.ResetCollectionProfilesForTesting();
    }

    [Fact]
    public void RerankingService_BalancesDocuments_WithoutLosingTheMostRelevantBook()
    {
        var service = new RerankingService();
        var candidates = new[]
        {
            CreateChunk("a1", "Общее введение в механику сплошной среды.", "doc-a", "Continuum_Mechanics.pdf", 0.97),
            CreateChunk("a2", "Еще один фрагмент про механику среды.", "doc-a", "Continuum_Mechanics.pdf", 0.96),
            CreateChunk("a3", "Третий фрагмент про механику среды.", "doc-a", "Continuum_Mechanics.pdf", 0.95),
            CreateChunk("b1", "Принцип наименьшего действия, уравнения Лагранжа и обобщенные координаты.", "doc-b", "Теоретическая_физика_1_Механика_Ландау_Лифшиц.pdf", 0.90),
            CreateChunk("c1", "Гидродинамика и течение жидкости.", "doc-c", "Hydrodynamics.pdf", 0.89)
        };

        var results = service.Rerank("Принцип наименьшего действия и уравнения Лагранжа", candidates, limit: 3);

        Assert.Equal(3, results.Count);
        Assert.Equal("doc-b", results[0].Metadata["document_id"]);
        Assert.True(results.Select(static chunk => chunk.Metadata["document_id"]).Distinct(StringComparer.OrdinalIgnoreCase).Count() >= 2);
    }

    [Fact]
    public async Task ContextAssemblyService_UsesAdaptiveCandidateWindow_AndCanSkipContextExpansion()
    {
        var vectorStore = new Mock<IVectorStore>();
        var structuredStore = new Mock<IStructuredVectorStore>();
        var ai = new Mock<AILink>("http://localhost:11434", "qwen");

        ai.Setup(x => x.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new float[768]);

        structuredStore.Setup(x => x.ListCollectionsAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { "knowledge_physics_v2" });
        structuredStore.Setup(x => x.GetCollectionPointCountAsync("knowledge_physics_v2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(16584);

        var returnedChunks = new List<KnowledgeChunk>
        {
            CreateChunk("k1", "Кинетическое уравнение Больцмана и интеграл столкновений.", "doc-k", "Теоретическая_физика_10_Физическая_кинетика_Ландау_Лифшиц.pdf", 0.82),
            CreateChunk("k2", "Физическая кинетика и функция распределения.", "doc-k", "Теоретическая_физика_10_Физическая_кинетика_Ландау_Лифшиц.pdf", 0.81),
            CreateChunk("q1", "Квантовая механика и волновая функция.", "doc-q", "Теоретическая_физика_3_Квантовая_механика_Ландау_Лифшиц.pdf", 0.84)
        };

        var observedLimit = 0;
        vectorStore.Setup(x => x.SearchAsync(It.IsAny<float[]>(), "knowledge_physics_v2", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(returnedChunks)
            .Callback<float[], string, int, CancellationToken>((_, _, limit, _) => observedLimit = limit);

        var service = new ContextAssemblyService(vectorStore.Object, structuredStore.Object, ai.Object, new RerankingService());

        var results = await service.AssembleAsync(
            "Кинетическое уравнение Больцмана, столкновения и функция распределения",
            domain: "physics",
            limit: 5,
            pipelineVersion: "v2",
            expandContext: false,
            ct: CancellationToken.None);

        Assert.True(observedLimit >= 64, $"Expected adaptive candidate window >= 64, got {observedLimit}.");
        Assert.NotEmpty(results);
        structuredStore.Verify(x => x.GetChunksByChunkIdsAsync(It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()), Times.Never);
        structuredStore.Verify(x => x.GetDocumentLocalGroupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void RerankingService_PrefersCandidatesWithChunkDescriptors_WhenVectorScoresAreClose()
    {
        var service = new RerankingService();
        var candidates = new[]
        {
            CreateChunk("generic-1", "Общий обзор неврологических состояний и симптомов.", "doc-generic", "General Neurology.pdf", 0.84, domain: "medicine"),
            CreateChunk(
                "targeted-1",
                "Раздел с практическими рекомендациями по контролю триггеров и профилактической терапии.",
                "doc-targeted",
                "Migraine Prevention Handbook.pdf",
                0.81,
                domain: "medicine",
                extraMetadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["chunk_title"] = "Профилактика мигрени - клинические рекомендации",
                    ["chunk_summary"] = "Контроль триггеров, сон и профилактическая терапия при частых приступах.",
                    ["semantic_terms"] = "мигрень, профилактика, триггеры, терапия"
                })
        };

        var results = service.Rerank("Как обычно строят профилактику мигрени?", candidates, limit: 2);

        Assert.Equal("doc-targeted", results[0].Metadata["document_id"]);
    }

    [Fact]
    public void RerankingService_PrefersCandidatesFromStronglyRoutedCollections()
    {
        var service = new RerankingService();
        var candidates = new[]
        {
            CreateChunk("chem-1", "Нейтральный фрагмент без явной анатомической лексики.", "doc-chem", "Общая химия", 0.88, routingScore: 0.2, domain: "chemistry"),
            CreateChunk("anat-1", "Фрагмент про отверстия основания черепа и прохождение нервов.", "doc-anat", "Gray's Anatomy", 0.73, routingScore: 6.4, domain: "anatomy")
        };

        var results = service.Rerank("Какие отверстия есть в основании черепа и что через них проходит?", candidates, limit: 2);

        Assert.Equal(2, results.Count);
        Assert.Equal("doc-anat", results[0].Metadata["document_id"]);
        Assert.Equal("anatomy", results[0].Metadata["domain"]);
    }

    [Fact]
    public void RerankingService_ReasoningSupport_PrefersTraceableChunks_AndPenalizesGenericReferenceNoise()
    {
        var service = new RerankingService();
        var candidates = new[]
        {
            CreateChunk(
                "archive-1",
                "Краткая энциклопедическая заметка о статусе и количестве.",
                "doc-archive",
                "Большая энциклопедия",
                0.9,
                routingScore: 0.4,
                domain: "historical_encyclopedias",
                collection: "knowledge_historical_encyclopedias_v2"),
            CreateChunk(
                "cs-1",
                "Structured JSON output should contain a status field and a numeric count field.",
                "doc-cs",
                "Reliable Structured Outputs.pdf",
                0.78,
                routingScore: 2.9,
                domain: "computer_science",
                collection: "knowledge_computer_science_v2")
        };

        var results = service.Rerank(
            "Return only JSON with status and count.",
            candidates,
            limit: 2,
            options: new RetrievalRequestOptions(
                Purpose: RetrievalPurpose.ReasoningSupport,
                DisallowedDomains: new[] { "historical_encyclopedias" },
                PreferTraceableChunks: true));

        Assert.Equal(2, results.Count);
        Assert.Equal("computer_science", results[0].Metadata["domain"]);
    }

    [Fact]
    public void RerankingService_PenalizesHistoricalArchiveForComputerScienceArchitectureQuery()
    {
        var service = new RerankingService();
        var candidates = new[]
        {
            CreateChunk(
                "he-arch",
                "Нейтральный энциклопедический фрагмент про архитектуру и систему.",
                "doc-he",
                "Том 24-1. Собаки-Струна. (1976).pdf",
                0.94,
                routingScore: 2.2,
                domain: "historical_encyclopedias",
                sourcePath: @"D:\HELPER_DATA\library\docs\encyclopedias\Большая Советская Энциклопедия, 3-е изд, 30 томов, (1969-1981)\Том 24-1. Собаки-Струна. (1976).pdf",
                collection: "knowledge_historical_encyclopedias_v2"),
            CreateChunk(
                "cs-arch",
                "Архитектура приложений, границы модулей и управление зависимостями в больших проектах.",
                "doc-cs",
                "Clean Architecture.pdf",
                0.79,
                routingScore: 3.8,
                domain: "computer_science",
                collection: "knowledge_computer_science_v2")
        };

        var results = service.Rerank("Как управлять зависимостями и архитектурой больших приложений?", candidates, limit: 2);

        Assert.Equal(2, results.Count);
        Assert.Equal("computer_science", results[0].Metadata["domain"]);
    }

    [Fact]
    public void RerankingService_PrefersMedicineOverVirologyForClinicalPneumoniaQuestion()
    {
        var service = new RerankingService();
        var candidates = new[]
        {
            CreateChunk(
                "viral-pneumonia",
                "Вирусы, вирионы и вирусная репликация.",
                "doc-vir",
                "Fields Virology.pdf",
                0.91,
                routingScore: 3.0,
                domain: "virology",
                collection: "knowledge_virology_v2"),
            CreateChunk(
                "clinical-pneumonia",
                "Клинические различия между вирусной и бактериальной пневмонией включают симптомы, диагностику и лечение.",
                "doc-med",
                "The Merck Manual Home Health Handbook",
                0.79,
                routingScore: 3.4,
                domain: "medicine",
                collection: "knowledge_medicine_v2")
        };

        var results = service.Rerank("Чем отличаются вирусная и бактериальная пневмония?", candidates, limit: 2);

        Assert.Equal(2, results.Count);
        Assert.Equal("medicine", results[0].Metadata["domain"]);
    }

    [Fact]
    public void RerankingService_PenalizesChemistrySinkForArtCultureStyleQuestion()
    {
        var service = new RerankingService();
        var candidates = new[]
        {
            CreateChunk(
                "chem-style",
                "Общая химия и свойства растворов.",
                "doc-chem",
                "Общая химия",
                0.9,
                routingScore: 0.5,
                domain: "chemistry",
                collection: "knowledge_chemistry_v2"),
            CreateChunk(
                "art-style",
                "Нехудожественный текст становится ясным и сильным благодаря структуре, стилю и редактированию.",
                "doc-art",
                "On Writing Well.pdf",
                0.78,
                routingScore: 3.6,
                domain: "art_culture",
                collection: "knowledge_art_culture_v2")
        };

        var results = service.Rerank("Что делает нехудожественный текст ясным и сильным по стилю?", candidates, limit: 2);

        Assert.Equal(2, results.Count);
        Assert.Equal("art_culture", results[0].Metadata["domain"]);
    }

    [Fact]
    public void RerankingService_PrefersEconomicsOverComputerScienceForComparativeAdvantageQuestion()
    {
        var service = new RerankingService();
        var candidates = new[]
        {
            CreateChunk(
                "cs-trade",
                "Архитектура систем и зависимостей в больших приложениях.",
                "doc-cs",
                "Clean Architecture.pdf",
                0.9,
                routingScore: 0.6,
                domain: "computer_science",
                collection: "knowledge_computer_science_v2"),
            CreateChunk(
                "econ-trade",
                "Сравнительное преимущество объясняет выгоды торговли между странами.",
                "doc-econ",
                "Principles of Economics.pdf",
                0.78,
                routingScore: 3.4,
                domain: "economics",
                collection: "knowledge_economics_v2")
        };

        var results = service.Rerank("Как работает сравнительное преимущество в торговле?", candidates, limit: 2);

        Assert.Equal(2, results.Count);
        Assert.Equal("economics", results[0].Metadata["domain"]);
    }

    [Fact]
    public void RerankingService_PrefersMythologyOverPhysicsForHeroJourneyQuestion()
    {
        var service = new RerankingService();
        var candidates = new[]
        {
            CreateChunk(
                "physics-hero",
                "Квантовые состояния и эволюция системы.",
                "doc-phys",
                "Теоретическая_физика_3_Квантовая_механика_Ландау_Лифшиц.pdf",
                0.9,
                routingScore: 0.5,
                domain: "physics",
                collection: "knowledge_physics_v2"),
            CreateChunk(
                "myth-hero",
                "Путешествие героя и архетипы в мифологии описывают путь испытаний и возвращения.",
                "doc-myth",
                "Mythology.epub",
                0.76,
                routingScore: 3.5,
                domain: "mythology_religion",
                collection: "knowledge_mythology_religion_v2")
        };

        var results = service.Rerank("Что означает путешествие героя в мифе?", candidates, limit: 2);

        Assert.Equal(2, results.Count);
        Assert.Equal("mythology_religion", results[0].Metadata["domain"]);
    }

    [Fact]
    public void RerankingService_PrefersSciFiOverComputerScienceForSeldonCrisisQuestion()
    {
        var service = new RerankingService();
        var candidates = new[]
        {
            CreateChunk(
                "cs-seldon",
                "Production systems and failure handling in distributed services.",
                "doc-cs",
                "ML Systems Design.pdf",
                0.88,
                routingScore: 0.7,
                domain: "computer_science",
                collection: "knowledge_computer_science_v2"),
            CreateChunk(
                "scifi-seldon",
                "Кризис Селдона в цикле Основание связан с планом психоистории и будущим Галактической Империи.",
                "doc-scifi",
                "Foundation and Empire.epub",
                0.75,
                routingScore: 3.8,
                domain: "sci_fi_concepts",
                collection: "knowledge_sci_fi_concepts_v2")
        };

        var results = service.Rerank("Что означает кризис Селдона?", candidates, limit: 2);

        Assert.Equal(2, results.Count);
        Assert.Equal("sci_fi_concepts", results[0].Metadata["domain"]);
    }

    [Fact]
    public void RerankingService_PrefersBiologyOverPhysicsForCellMembraneQuestion()
    {
        var service = new RerankingService();
        var candidates = new[]
        {
            CreateChunk(
                "phys-membrane",
                "Квантовая механика и физические состояния системы.",
                "doc-phys",
                "Теоретическая_физика_3_Квантовая_механика_Ландау_Лифшиц.pdf",
                0.89,
                routingScore: 0.6,
                domain: "physics",
                collection: "knowledge_physics_v2"),
            CreateChunk(
                "bio-membrane",
                "Клеточная мембрана обеспечивает транспорт веществ и поддерживает градиенты концентраций.",
                "doc-bio",
                "Campbell Biology.pdf",
                0.76,
                routingScore: 3.6,
                domain: "biology",
                collection: "knowledge_biology_v2")
        };

        var results = service.Rerank("Как устроена клеточная мембрана и как идет транспорт веществ?", candidates, limit: 2);

        Assert.Equal(2, results.Count);
        Assert.Equal("biology", results[0].Metadata["domain"]);
    }

    [Fact]
    public void RerankingService_PrefersAnalysisStrategyForAntifragilityQuestion()
    {
        var service = new RerankingService();
        var candidates = new[]
        {
            CreateChunk(
                "phys-antifragile",
                "Физические системы, поля и состояния материи.",
                "doc-phys",
                "Теоретическая_физика_4_Квантовая_электродинамика_Ландау_Лифшиц.pdf",
                0.9,
                routingScore: 0.4,
                domain: "physics",
                collection: "knowledge_physics_v2"),
            CreateChunk(
                "analysis-antifragile",
                "Антихрупкость описывает системы, которые выигрывают от стресса и неопределенности.",
                "doc-analysis",
                "Антихрупкость.pdf",
                0.78,
                routingScore: 3.6,
                domain: "analysis_strategy",
                collection: "knowledge_analysis_strategy_v2")
        };

        var results = service.Rerank("Что означает антихрупкость системы?", candidates, limit: 2);

        Assert.Equal(2, results.Count);
        Assert.Equal("analysis_strategy", results[0].Metadata["domain"]);
    }

    [Fact]
    public void RerankingService_PrefersAnatomyOverHistoricalArchiveForTibiaFibulaQuestion()
    {
        var service = new RerankingService();
        var candidates = new[]
        {
            CreateChunk(
                "archive-bones",
                "Нейтральный энциклопедический фрагмент.",
                "doc-archive",
                "Том 07. Гоголь-Дебит. (1972).pdf",
                0.9,
                routingScore: 0.5,
                domain: "historical_encyclopedias",
                sourcePath: @"D:\HELPER_DATA\library\docs\encyclopedias\Большая Советская Энциклопедия, 3-е изд, 30 томов, (1969-1981)\Том 07. Гоголь-Дебит. (1972).pdf",
                collection: "knowledge_historical_encyclopedias_v2"),
            CreateChunk(
                "anat-bones",
                "Большеберцовая и малоберцовая кости отличаются положением, толщиной и ролью в голени.",
                "doc-anat",
                "Gray's Anatomy.pdf",
                0.76,
                routingScore: 3.5,
                domain: "anatomy",
                collection: "knowledge_anatomy_v2")
        };

        var results = service.Rerank("Чем отличаются большеберцовая и малоберцовая кости?", candidates, limit: 2);

        Assert.Equal(2, results.Count);
        Assert.Equal("anatomy", results[0].Metadata["domain"]);
    }

    [Fact]
    public void RerankingService_PrefersEconomicsOverEncyclopediasForSystemsThinkingQuestion()
    {
        var service = new RerankingService();
        var candidates = new[]
        {
            CreateChunk(
                "encyc-systems",
                "Краткая энциклопедическая заметка о системе как общем понятии.",
                "doc-encyc",
                "Прохоров А.М. СЭС 1988.pdf",
                0.88,
                routingScore: 0.4,
                domain: "encyclopedias",
                collection: "knowledge_encyclopedias_v2"),
            CreateChunk(
                "econ-systems",
                "Системное мышление в экономике показывает взаимосвязи, обратные связи и долгосрочные эффекты решений.",
                "doc-econ",
                "Thinking in Systems.pdf",
                0.75,
                routingScore: 3.3,
                domain: "economics",
                collection: "knowledge_economics_v2")
        };

        var results = service.Rerank("Что означает системное мышление в экономике?", candidates, limit: 2);

        Assert.Equal(2, results.Count);
        Assert.Equal("economics", results[0].Metadata["domain"]);
    }

    [Fact]
    public void RerankingService_PrefersSciFiOverHistoryForGalacticEmpireQuestion()
    {
        var service = new RerankingService();
        var candidates = new[]
        {
            CreateChunk(
                "history-empire",
                "История мировых империй и их политического устройства.",
                "doc-history",
                "Всемирная история. Энциклопедия. Т.12. - 2007.pdf",
                0.87,
                routingScore: 0.6,
                domain: "history",
                collection: "knowledge_history_v2"),
            CreateChunk(
                "scifi-empire",
                "Идея Галактической Империи у Азимова описывает масштаб цивилизации и политический упадок будущего.",
                "doc-scifi",
                "Foundation.epub",
                0.75,
                routingScore: 3.5,
                domain: "sci_fi_concepts",
                collection: "knowledge_sci_fi_concepts_v2")
        };

        var results = service.Rerank("Как работает идея Галактической Империи у Азимова?", candidates, limit: 2);

        Assert.Equal(2, results.Count);
        Assert.Equal("sci_fi_concepts", results[0].Metadata["domain"]);
    }

    [Fact]
    public void RerankingService_PrefersPhysicsOverAnalysisStrategyForLeastActionQuestion()
    {
        var service = new RerankingService();
        var candidates = new[]
        {
            CreateChunk(
                "analysis-principle",
                "Принципы структурирования аргументации и принятия решений.",
                "doc-analysis",
                "Принцип пирамиды Минто.pdf",
                0.88,
                routingScore: 0.5,
                domain: "analysis_strategy",
                collection: "knowledge_analysis_strategy_v2"),
            CreateChunk(
                "physics-action",
                "Принцип наименьшего действия лежит в основе лагранжевой формулировки механики.",
                "doc-physics",
                "Теоретическая_физика_1_Механика_Ландау_Лифшиц.pdf",
                0.77,
                routingScore: 3.7,
                domain: "physics",
                collection: "knowledge_physics_v2")
        };

        var results = service.Rerank("Что такое принцип наименьшего действия?", candidates, limit: 2);

        Assert.Equal(2, results.Count);
        Assert.Equal("physics", results[0].Metadata["domain"]);
    }

    [Fact]
    public async Task ContextAssemblyService_GlobalRouting_CanIncludeHistoricalArchiveCollections_WhenQueryStronglyMatches()
    {
        var vectorStore = new Mock<IVectorStore>();
        var structuredStore = new Mock<IStructuredVectorStore>();
        var ai = new Mock<AILink>("http://localhost:11434", "qwen");

        ai.Setup(x => x.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new float[768]);

        structuredStore.Setup(x => x.ListCollectionsAsync("knowledge_", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { "knowledge_history_v2", "knowledge_historical_encyclopedias_v2" });
        structuredStore.Setup(x => x.GetCollectionPointCountAsync("knowledge_history_v2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(10000);
        structuredStore.Setup(x => x.GetCollectionPointCountAsync("knowledge_historical_encyclopedias_v2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(150000);

        vectorStore.Setup(x => x.ScrollMetadataAsync("knowledge_history_v2", It.IsAny<int>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<KnowledgeChunk>
            {
                CreateChunk("h1", "История древнего мира и Римской империи.", "doc-h1", "Всемирная история. Энциклопедия. Т.1. - 2006.pdf", 0.6, domain: "history")
            });
        vectorStore.Setup(x => x.ScrollMetadataAsync("knowledge_historical_encyclopedias_v2", It.IsAny<int>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<KnowledgeChunk>
            {
                CreateChunk("he1", "Экслибрис как книжный знак и его история.", "doc-he1", "Том 30. Экслибрис-Яя. (1978).pdf", 0.6, domain: "historical_encyclopedias")
            });

        var searchedCollections = new List<string>();
        vectorStore.Setup(x => x.SearchAsync(It.IsAny<float[]>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((float[] _, string collection, int _, CancellationToken _) =>
            {
                searchedCollections.Add(collection);
                return collection == "knowledge_historical_encyclopedias_v2"
                    ? new List<KnowledgeChunk> { CreateChunk("he-hit", "Экслибрис как книжный знак.", "doc-he-hit", "Том 30. Экслибрис-Яя. (1978).pdf", 0.81, domain: "historical_encyclopedias") }
                    : new List<KnowledgeChunk>();
            });

        var service = new ContextAssemblyService(vectorStore.Object, structuredStore.Object, ai.Object, new RerankingService());

        var results = await service.AssembleAsync(
            "Что такое экслибрис?",
            domain: null,
            limit: 3,
            pipelineVersion: "v2",
            expandContext: false,
            ct: CancellationToken.None);

        Assert.Contains("knowledge_historical_encyclopedias_v2", searchedCollections);
        Assert.NotEmpty(results);
        Assert.Equal("historical_encyclopedias", results[0].Metadata["domain"]);
    }

    [Fact]
    public async Task ContextAssemblyService_GlobalRouting_DoesNotPreferHistoricalArchiveForGenericComputerScienceQueries()
    {
        var vectorStore = new Mock<IVectorStore>();
        var structuredStore = new Mock<IStructuredVectorStore>();
        var ai = new Mock<AILink>("http://localhost:11434", "qwen");

        ai.Setup(x => x.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new float[768]);

        structuredStore.Setup(x => x.ListCollectionsAsync("knowledge_", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { "knowledge_computer_science_v2", "knowledge_historical_encyclopedias_v2" });
        structuredStore.Setup(x => x.GetCollectionPointCountAsync("knowledge_computer_science_v2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(25000);
        structuredStore.Setup(x => x.GetCollectionPointCountAsync("knowledge_historical_encyclopedias_v2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(150000);

        vectorStore.Setup(x => x.ScrollMetadataAsync("knowledge_computer_science_v2", It.IsAny<int>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<KnowledgeChunk>
            {
                CreateChunk("cs-profile", "Архитектура приложений, dependency injection и управление зависимостями.", "doc-cs-profile", "Clean Architecture.pdf", 0.6, domain: "computer_science")
            });
        vectorStore.Setup(x => x.ScrollMetadataAsync("knowledge_historical_encyclopedias_v2", It.IsAny<int>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<KnowledgeChunk>
            {
                CreateChunk("he-profile", "Архитектура приложений и управление зависимостями встречаются в широком энциклопедическом корпусе.", "doc-he-profile", "Том 24-1. Собаки-Струна. (1976).pdf", 0.6, domain: "historical_encyclopedias")
            });

        var searchedCollections = new List<string>();
        vectorStore.Setup(x => x.SearchAsync(It.IsAny<float[]>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((float[] _, string collection, int _, CancellationToken _) =>
            {
                searchedCollections.Add(collection);
                return collection switch
                {
                    "knowledge_computer_science_v2" => new List<KnowledgeChunk>
                    {
                        CreateChunk("cs-hit", "Управление зависимостями и архитектурой больших приложений требует явных границ и dependency injection.", "doc-cs-hit", "Clean Architecture.pdf", 0.79, domain: "computer_science")
                    },
                    "knowledge_historical_encyclopedias_v2" => new List<KnowledgeChunk>
                    {
                        CreateChunk("he-hit", "Архитектура и система как общие энциклопедические понятия.", "doc-he-hit", "Том 24-1. Собаки-Струна. (1976).pdf", 0.92, domain: "historical_encyclopedias")
                    },
                    _ => new List<KnowledgeChunk>()
                };
            });

        var service = new ContextAssemblyService(vectorStore.Object, structuredStore.Object, ai.Object, new RerankingService());

        var results = await service.AssembleAsync(
            "Как управлять зависимостями и архитектурой больших приложений?",
            domain: null,
            limit: 3,
            pipelineVersion: "v2",
            expandContext: false,
            ct: CancellationToken.None);

        Assert.DoesNotContain("knowledge_historical_encyclopedias_v2", searchedCollections);
        Assert.NotEmpty(results);
        Assert.Equal("computer_science", results[0].Metadata["domain"]);
    }

    [Fact]
    public async Task ContextAssemblyService_GlobalRouting_DoesNotOverfavorAnalysisStrategyForBroadHistoryQuestions()
    {
        var vectorStore = new Mock<IVectorStore>();
        var structuredStore = new Mock<IStructuredVectorStore>();
        var ai = new Mock<AILink>("http://localhost:11434", "qwen");

        ai.Setup(x => x.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new float[768]);

        structuredStore.Setup(x => x.ListCollectionsAsync("knowledge_", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { "knowledge_history_v2", "knowledge_analysis_strategy_v2" });
        structuredStore.Setup(x => x.GetCollectionPointCountAsync("knowledge_history_v2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(12000);
        structuredStore.Setup(x => x.GetCollectionPointCountAsync("knowledge_analysis_strategy_v2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(7000);

        vectorStore.Setup(x => x.ScrollMetadataAsync("knowledge_history_v2", It.IsAny<int>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<KnowledgeChunk>
            {
                CreateChunk("hist-profile", "Первая мировая война, причины конфликта и дипломатический кризис 1914 года.", "doc-hist-profile", "Всемирная история. Энциклопедия. Т.13. - 2007.pdf", 0.6, domain: "history")
            });
        vectorStore.Setup(x => x.ScrollMetadataAsync("knowledge_analysis_strategy_v2", It.IsAny<int>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<KnowledgeChunk>
            {
                CreateChunk("analysis-profile", "Стратегия, непрямые действия и теория войны у Клаузевица.", "doc-analysis-profile", "Карл фон Клаузевиц - О ВОЙНЕ.pdf", 0.6, domain: "analysis_strategy")
            });

        vectorStore.Setup(x => x.SearchAsync(It.IsAny<float[]>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((float[] _, string collection, int _, CancellationToken _) =>
            {
                return collection switch
                {
                    "knowledge_history_v2" => new List<KnowledgeChunk>
                    {
                        CreateChunk("hist-hit", "Причины Первой мировой войны: система союзов, национализм и июльский кризис 1914 года.", "doc-hist-hit", "Всемирная история. Энциклопедия. Т.13. - 2007.pdf", 0.81, domain: "history")
                    },
                    "knowledge_analysis_strategy_v2" => new List<KnowledgeChunk>
                    {
                        CreateChunk("analysis-hit", "Война и стратегия как продолжение политики и теория вооруженного конфликта.", "doc-analysis-hit", "Карл фон Клаузевиц - О ВОЙНЕ.pdf", 0.9, domain: "analysis_strategy")
                    },
                    _ => new List<KnowledgeChunk>()
                };
            });

        var service = new ContextAssemblyService(vectorStore.Object, structuredStore.Object, ai.Object, new RerankingService());

        var results = await service.AssembleAsync(
            "Какие причины привели к Первой мировой войне?",
            domain: null,
            limit: 3,
            pipelineVersion: "v2",
            expandContext: false,
            ct: CancellationToken.None);

        Assert.NotEmpty(results);
        Assert.Equal("history", results[0].Metadata["domain"]);
    }

    [Fact]
    public async Task ContextAssemblyService_GlobalRouting_DoesNotUseHistoricalArchiveMarkers_AsComputerScienceSignal()
    {
        var vectorStore = new Mock<IVectorStore>();
        var structuredStore = new Mock<IStructuredVectorStore>();
        var ai = new Mock<AILink>("http://localhost:11434", "qwen");

        ai.Setup(x => x.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new float[768]);

        structuredStore.Setup(x => x.ListCollectionsAsync("knowledge_", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { "knowledge_computer_science_v2", "knowledge_historical_encyclopedias_v2" });
        structuredStore.Setup(x => x.GetCollectionPointCountAsync("knowledge_computer_science_v2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(25000);
        structuredStore.Setup(x => x.GetCollectionPointCountAsync("knowledge_historical_encyclopedias_v2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(150000);

        vectorStore.Setup(x => x.ScrollMetadataAsync("knowledge_computer_science_v2", It.IsAny<int>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<KnowledgeChunk>
            {
                CreateChunk("cs-profile", "Поддерживаемый код, архитектура приложений и границы модулей.", "doc-cs-profile", "Clean Architecture.pdf", 0.6, domain: "computer_science")
            });
        vectorStore.Setup(x => x.ScrollMetadataAsync("knowledge_historical_encyclopedias_v2", It.IsAny<int>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<KnowledgeChunk>
            {
                CreateChunk(
                    "he-profile",
                    "Нейтральная энциклопедическая статья без программной тематики.",
                    "doc-he-profile",
                    "Том 07. Гоголь-Дебит. (1972).pdf",
                    0.6,
                    domain: "historical_encyclopedias",
                    sourcePath: @"D:\HELPER_DATA\library\docs\encyclopedias\Большая Советская Энциклопедия, 3-е изд, 30 томов, (1969-1981)\Том 07. Гоголь-Дебит. (1972).pdf")
            });

        vectorStore.Setup(x => x.SearchAsync(It.IsAny<float[]>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((float[] _, string collection, int _, CancellationToken _) =>
            {
                return collection switch
                {
                    "knowledge_computer_science_v2" => new List<KnowledgeChunk>
                    {
                        CreateChunk("cs-hit", "Поддерживаемый код в больших проектах требует явных принципов проектирования.", "doc-cs-hit", "Code Complete.pdf", 0.79, domain: "computer_science")
                    },
                    "knowledge_historical_encyclopedias_v2" => new List<KnowledgeChunk>
                    {
                        CreateChunk(
                            "he-hit",
                            "Нейтральный энциклопедический фрагмент.",
                            "doc-he-hit",
                            "Том 07. Гоголь-Дебит. (1972).pdf",
                            0.93,
                            domain: "historical_encyclopedias",
                            sourcePath: @"D:\HELPER_DATA\library\docs\encyclopedias\Большая Советская Энциклопедия, 3-е изд, 30 томов, (1969-1981)\Том 07. Гоголь-Дебит. (1972).pdf")
                    },
                    _ => new List<KnowledgeChunk>()
                };
            });

        var service = new ContextAssemblyService(vectorStore.Object, structuredStore.Object, ai.Object, new RerankingService());

        var results = await service.AssembleAsync(
            "Какие принципы делают код поддерживаемым в больших проектах?",
            domain: null,
            limit: 3,
            pipelineVersion: "v2",
            expandContext: false,
            ct: CancellationToken.None);

        Assert.NotEmpty(results);
        Assert.Equal("computer_science", results[0].Metadata["domain"]);
    }

    [Fact]
    public async Task ContextAssemblyService_GlobalRouting_PrefersArtCultureForEuropeanPaintingPeriodQuery()
    {
        var vectorStore = new Mock<IVectorStore>();
        var structuredStore = new Mock<IStructuredVectorStore>();
        var ai = new Mock<AILink>("http://localhost:11434", "qwen");

        ai.Setup(x => x.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new float[768]);

        structuredStore.Setup(x => x.ListCollectionsAsync("knowledge_", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { "knowledge_art_culture_v2", "knowledge_history_v2" });
        structuredStore.Setup(x => x.GetCollectionPointCountAsync("knowledge_art_culture_v2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(4500);
        structuredStore.Setup(x => x.GetCollectionPointCountAsync("knowledge_history_v2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(10000);

        vectorStore.Setup(x => x.ScrollMetadataAsync("knowledge_art_culture_v2", It.IsAny<int>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<KnowledgeChunk>
            {
                CreateChunk("art-profile", "История европейской живописи: готика, Ренессанс, барокко и модернизм.", "doc-art-profile", "The Story of Art.pdf", 0.6, domain: "art_culture")
            });
        vectorStore.Setup(x => x.ScrollMetadataAsync("knowledge_history_v2", It.IsAny<int>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<KnowledgeChunk>
            {
                CreateChunk("hist-profile", "Политическая история Европы и мировые войны.", "doc-hist-profile", "Всемирная история. Энциклопедия. Т.12. - 2007.pdf", 0.6, domain: "history")
            });

        vectorStore.Setup(x => x.SearchAsync(It.IsAny<float[]>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((float[] _, string collection, int _, CancellationToken _) =>
            {
                return collection switch
                {
                    "knowledge_art_culture_v2" => new List<KnowledgeChunk>
                    {
                        CreateChunk("art-hit", "Европейская живопись проходит через Ренессанс, барокко, классицизм и модернизм.", "doc-art-hit", "The Story of Art.pdf", 0.79, domain: "art_culture")
                    },
                    "knowledge_history_v2" => new List<KnowledgeChunk>
                    {
                        CreateChunk("hist-hit", "История Европы по векам и политическим периодам.", "doc-hist-hit", "Всемирная история. Энциклопедия. Т.12. - 2007.pdf", 0.83, domain: "history")
                    },
                    _ => new List<KnowledgeChunk>()
                };
            });

        var service = new ContextAssemblyService(vectorStore.Object, structuredStore.Object, ai.Object, new RerankingService());

        var results = await service.AssembleAsync(
            "Какие основные эпохи выделяют в истории европейской живописи?",
            domain: null,
            limit: 3,
            pipelineVersion: "v2",
            expandContext: false,
            ct: CancellationToken.None);

        Assert.NotEmpty(results);
        Assert.Equal("art_culture", results[0].Metadata["domain"]);
    }

    [Fact]
    public async Task ContextAssemblyService_GlobalRouting_PrefersMedicineForClinicalHeartFailureQuery()
    {
        var vectorStore = new Mock<IVectorStore>();
        var structuredStore = new Mock<IStructuredVectorStore>();
        var ai = new Mock<AILink>("http://localhost:11434", "qwen");

        ai.Setup(x => x.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new float[768]);

        structuredStore.Setup(x => x.ListCollectionsAsync("knowledge_", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { "knowledge_medicine_v2", "knowledge_computer_science_v2", "knowledge_chemistry_v2" });
        structuredStore.Setup(x => x.GetCollectionPointCountAsync("knowledge_medicine_v2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(12000);
        structuredStore.Setup(x => x.GetCollectionPointCountAsync("knowledge_computer_science_v2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(25000);
        structuredStore.Setup(x => x.GetCollectionPointCountAsync("knowledge_chemistry_v2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(10000);

        vectorStore.Setup(x => x.ScrollMetadataAsync("knowledge_medicine_v2", It.IsAny<int>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<KnowledgeChunk>
            {
                CreateChunk("med-profile", "Клинические признаки сердечной недостаточности, диагностика и лечение.", "doc-med-profile", "The Merck Manual Home Health Handbook", 0.6, domain: "medicine")
            });
        vectorStore.Setup(x => x.ScrollMetadataAsync("knowledge_computer_science_v2", It.IsAny<int>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<KnowledgeChunk>
            {
                CreateChunk("cs-profile", "Production systems and monitoring services.", "doc-cs-profile", "ML Systems Design.pdf", 0.6, domain: "computer_science")
            });
        vectorStore.Setup(x => x.ScrollMetadataAsync("knowledge_chemistry_v2", It.IsAny<int>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<KnowledgeChunk>
            {
                CreateChunk("chem-profile", "Общая химия и свойства растворов.", "doc-chem-profile", "Общая химия", 0.6, domain: "chemistry")
            });

        vectorStore.Setup(x => x.SearchAsync(It.IsAny<float[]>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((float[] _, string collection, int _, CancellationToken _) =>
            {
                return collection switch
                {
                    "knowledge_medicine_v2" => new List<KnowledgeChunk>
                    {
                        CreateChunk("med-hit", "Клинические признаки сердечной недостаточности включают одышку, отеки и утомляемость.", "doc-med-hit", "The Merck Manual Home Health Handbook", 0.75, domain: "medicine")
                    },
                    "knowledge_computer_science_v2" => new List<KnowledgeChunk>
                    {
                        CreateChunk("cs-hit", "System monitoring and failure handling in distributed services.", "doc-cs-hit", "ML Systems Design.pdf", 0.84, domain: "computer_science")
                    },
                    "knowledge_chemistry_v2" => new List<KnowledgeChunk>
                    {
                        CreateChunk("chem-hit", "Химические свойства растворов и кислот.", "doc-chem-hit", "Общая химия", 0.82, domain: "chemistry")
                    },
                    _ => new List<KnowledgeChunk>()
                };
            });

        var service = new ContextAssemblyService(vectorStore.Object, structuredStore.Object, ai.Object, new RerankingService());

        var results = await service.AssembleAsync(
            "Какие клинические признаки характерны для сердечной недостаточности?",
            domain: null,
            limit: 3,
            pipelineVersion: "v2",
            expandContext: false,
            ct: CancellationToken.None);

        Assert.NotEmpty(results);
        Assert.Equal("medicine", results[0].Metadata["domain"]);
    }

    [Fact]
    public void RerankingService_PrefersAnatomyOverComputerScienceForIntestinalWallQuestion()
    {
        var service = new RerankingService();
        var candidates = new[]
        {
            CreateChunk(
                "cs-intestinal-wall",
                "Слои архитектуры программ и границы модулей в больших приложениях.",
                "doc-cs",
                "Clean Architecture.pdf",
                0.9,
                routingScore: 0.5,
                domain: "computer_science",
                collection: "knowledge_computer_science_v2"),
            CreateChunk(
                "anat-intestinal-wall",
                "Стенку тонкой кишки образуют слизистая оболочка, подслизистая основа, мышечная и серозная оболочки.",
                "doc-anat",
                "Gray's Anatomy.pdf",
                0.77,
                routingScore: 3.6,
                domain: "anatomy",
                collection: "knowledge_anatomy_v2")
        };

        var results = service.Rerank("Какие слои образуют стенку тонкой кишки?", candidates, limit: 2);

        Assert.Equal(2, results.Count);
        Assert.Equal("anatomy", results[0].Metadata["domain"]);
    }

    [Fact]
    public void RerankingService_PrefersNeuroOverPhysicsForActionPotentialQuestion()
    {
        var service = new RerankingService();
        var candidates = new[]
        {
            CreateChunk(
                "physics-action-potential",
                "Физический потенциал, уравнения поля и динамика системы.",
                "doc-phys",
                "Теоретическая_физика_2_Теория_поля_Ландау_Лифшиц.pdf",
                0.9,
                routingScore: 0.4,
                domain: "physics",
                collection: "knowledge_physics_v2"),
            CreateChunk(
                "neuro-action-potential",
                "Потенциал действия нейрона возникает из-за изменения проницаемости мембраны для ионов натрия и калия.",
                "doc-neuro",
                "Principles of Neural Science.pdf",
                0.77,
                routingScore: 3.6,
                domain: "neuro",
                collection: "knowledge_neuro_v2")
        };

        var results = service.Rerank("Как работает потенциал действия нейрона?", candidates, limit: 2);

        Assert.Equal(2, results.Count);
        Assert.Equal("neuro", results[0].Metadata["domain"]);
    }

    [Fact]
    public void RerankingService_PrefersNeuroOverPhysicsForBasalGangliaQuestion()
    {
        var service = new RerankingService();
        var candidates = new[]
        {
            CreateChunk(
                "physics-ganglia",
                "Физические взаимодействия частиц и симметрии в динамических системах.",
                "doc-phys",
                "Теоретическая_физика_3_Квантовая_механика_Ландау_Лифшиц.pdf",
                0.89,
                routingScore: 0.4,
                domain: "physics",
                collection: "knowledge_physics_v2"),
            CreateChunk(
                "neuro-ganglia",
                "Базальные ганглии участвуют в выборе и инициации движений через петли коры, таламуса и стриатума.",
                "doc-neuro",
                "Principles of Neural Science.pdf",
                0.76,
                routingScore: 3.5,
                domain: "neuro",
                collection: "knowledge_neuro_v2")
        };

        var results = service.Rerank("Как базальные ганглии участвуют в движении?", candidates, limit: 2);

        Assert.Equal(2, results.Count);
        Assert.Equal("neuro", results[0].Metadata["domain"]);
    }

    [Fact]
    public void RerankingService_PrefersEnglishLiteratureOverEncyclopediasForShakespeareQuestion()
    {
        var service = new RerankingService();
        var candidates = new[]
        {
            CreateChunk(
                "encyc-shakespeare",
                "Краткая энциклопедическая справка о драматурге и эпохе.",
                "doc-encyc",
                "Прохоров А.М. СЭС 1988.pdf",
                0.89,
                routingScore: 0.5,
                domain: "encyclopedias",
                collection: "knowledge_encyclopedias_v2"),
            CreateChunk(
                "englit-shakespeare",
                "Комедии и трагедии Шекспира различаются по композиции, конфликту и развязке.",
                "doc-englit",
                "The Complete Tragedies and Comedies of William Shakespeare.pdf",
                0.76,
                routingScore: 3.4,
                domain: "english_lang_lit",
                collection: "knowledge_english_lang_lit_v2")
        };

        var results = service.Rerank("Что отличает комедии и трагедии Шекспира?", candidates, limit: 2);

        Assert.Equal(2, results.Count);
        Assert.Equal("english_lang_lit", results[0].Metadata["domain"]);
    }

    [Fact]
    public void RerankingService_PrefersHistoricalEncyclopediasOverHistoryForAngolaContextQuestion()
    {
        var service = new RerankingService();
        var candidates = new[]
        {
            CreateChunk(
                "history-angola",
                "История Анголы в колониальный и постколониальный периоды.",
                "doc-history",
                "Всемирная история. Энциклопедия. Т.2. - 2006.pdf",
                0.88,
                routingScore: 0.5,
                domain: "history",
                collection: "knowledge_history_v2"),
            CreateChunk(
                "archive-angola",
                "Ангола в историко-энциклопедическом контексте: география, колонизация и ключевые вехи.",
                "doc-archive",
                "Большая Советская Энциклопедия.pdf",
                0.76,
                routingScore: 3.4,
                domain: "historical_encyclopedias",
                collection: "knowledge_historical_encyclopedias_v2")
        };

        var results = service.Rerank("Что известно об Анголе в историко-энциклопедическом контексте?", candidates, limit: 2);

        Assert.Equal(2, results.Count);
        Assert.Equal("historical_encyclopedias", results[0].Metadata["domain"]);
    }

    [Fact]
    public void RerankingService_PrefersEncyclopediasOverPhysicsForDopamineDefinitionQuestion()
    {
        var service = new RerankingService();
        var candidates = new[]
        {
            CreateChunk(
                "physics-dopamine",
                "Физические свойства молекул и энергетические переходы в системе.",
                "doc-phys",
                "Теоретическая_физика_5_Статистическая_физика_Ландау_Лифшиц.pdf",
                0.88,
                routingScore: 0.4,
                domain: "physics",
                collection: "knowledge_physics_v2"),
            CreateChunk(
                "encyc-dopamine",
                "Дофамин: краткое энциклопедическое определение нейромедиатора и его функций.",
                "doc-encyc",
                "A Dictionary of Psychology.epub",
                0.76,
                routingScore: 3.3,
                domain: "encyclopedias",
                collection: "knowledge_encyclopedias_v2")
        };

        var results = service.Rerank("Что такое дофамин в кратком энциклопедическом определении?", candidates, limit: 2);

        Assert.Equal(2, results.Count);
        Assert.Equal("encyclopedias", results[0].Metadata["domain"]);
    }

    [Fact]
    public void RerankingService_PrefersLinguisticsOverMathForAnalyticVsSyntheticLanguagesQuestion()
    {
        var service = new RerankingService();
        var candidates = new[]
        {
            CreateChunk(
                "math-analytic-synthetic",
                "Аналитические методы и синтетические конструкции в математике.",
                "doc-math",
                "Mathematical Analysis.pdf",
                0.89,
                routingScore: 0.5,
                domain: "math",
                collection: "knowledge_math_v2"),
            CreateChunk(
                "ling-analytic-synthetic",
                "Аналитические языки выражают грамматические отношения отдельными словами, а синтетические - формами слов.",
                "doc-ling",
                "The World Atlas of Language Structures.pdf",
                0.77,
                routingScore: 3.5,
                domain: "linguistics",
                collection: "knowledge_linguistics_v2")
        };

        var results = service.Rerank("Чем аналитические языки отличаются от синтетических?", candidates, limit: 2);

        Assert.Equal(2, results.Count);
        Assert.Equal("linguistics", results[0].Metadata["domain"]);
    }

    [Fact]
    public void RerankingService_PrefersMathOverComputerScienceForEigenvaluesQuestion()
    {
        var service = new RerankingService();
        var candidates = new[]
        {
            CreateChunk(
                "cs-eigen",
                "Собственные значения могут использоваться в алгоритмах машинного обучения и анализа данных.",
                "doc-cs",
                "Hands-On Machine Learning.pdf",
                0.89,
                routingScore: 0.5,
                domain: "computer_science",
                collection: "knowledge_computer_science_v2"),
            CreateChunk(
                "math-eigen",
                "Собственные значения матрицы определяются из характеристического многочлена линейного оператора.",
                "doc-math",
                "Linear Algebra.pdf",
                0.77,
                routingScore: 3.4,
                domain: "math",
                collection: "knowledge_math_v2")
        };

        var results = service.Rerank("Что такое собственные значения матрицы?", candidates, limit: 2);

        Assert.Equal(2, results.Count);
        Assert.Equal("math", results[0].Metadata["domain"]);
    }

    [Fact]
    public void RerankingService_PrefersPsychologyOverComputerScienceForIndividuationQuestion()
    {
        var service = new RerankingService();
        var candidates = new[]
        {
            CreateChunk(
                "cs-individuation",
                "Индивидуализация рекомендаций в адаптивных цифровых системах.",
                "doc-cs",
                "Recommender Systems.pdf",
                0.89,
                routingScore: 0.5,
                domain: "computer_science",
                collection: "knowledge_computer_science_v2"),
            CreateChunk(
                "psych-individuation",
                "Индивидуация у Юнга описывает процесс становления целостной личности.",
                "doc-psych",
                "Psychological Types.epub",
                0.77,
                routingScore: 3.4,
                domain: "psychology",
                collection: "knowledge_psychology_v2")
        };

        var results = service.Rerank("Что такое индивидуация?", candidates, limit: 2);

        Assert.Equal(2, results.Count);
        Assert.Equal("psychology", results[0].Metadata["domain"]);
    }

    [Fact]
    public void RerankingService_PrefersPsychologyOverEncyclopediasForIntroversionQuestion()
    {
        var service = new RerankingService();
        var candidates = new[]
        {
            CreateChunk(
                "encyc-introversion",
                "Краткая словарная заметка о личностных качествах.",
                "doc-encyc",
                "A Dictionary of Psychology.epub",
                0.89,
                routingScore: 0.5,
                domain: "encyclopedias",
                collection: "knowledge_encyclopedias_v2"),
            CreateChunk(
                "psych-introversion",
                "Интроверсия и экстраверсия описывают разные способы направленности психической энергии у Юнга.",
                "doc-psych",
                "Psychological Types.epub",
                0.77,
                routingScore: 3.4,
                domain: "psychology",
                collection: "knowledge_psychology_v2")
        };

        var results = service.Rerank("Чем отличаются интроверсия и экстраверсия?", candidates, limit: 2);

        Assert.Equal(2, results.Count);
        Assert.Equal("psychology", results[0].Metadata["domain"]);
    }

    [Fact]
    public void RerankingService_PrefersBiologyOverNeuroForSynapseSignalQuestion()
    {
        var service = new RerankingService();
        var candidates = new[]
        {
            CreateChunk(
                "neuro-synapse",
                "Синаптическая передача в нейронных цепях и пластичность синапсов.",
                "doc-neuro",
                "Principles of Neural Science.pdf",
                0.89,
                routingScore: 0.7,
                domain: "neuro",
                collection: "knowledge_neuro_v2"),
            CreateChunk(
                "bio-synapse",
                "Сигнал через синапс передается химическими медиаторами, выделяющимися в синаптическую щель.",
                "doc-bio",
                "Campbell Biology.pdf",
                0.77,
                routingScore: 3.4,
                domain: "biology",
                collection: "knowledge_biology_v2")
        };

        var results = service.Rerank("Как передается сигнал через синапс?", candidates, limit: 2);

        Assert.Equal(2, results.Count);
        Assert.Equal("biology", results[0].Metadata["domain"]);
    }

    [Fact]
    public void RerankingService_PrefersChemistryOverEncyclopediasForOrganicAcidityQuestion()
    {
        var service = new RerankingService();
        var candidates = new[]
        {
            CreateChunk(
                "encyc-acidity",
                "Краткая энциклопедическая заметка о кислотности как общем термине.",
                "doc-encyc",
                "Прохоров А.М. СЭС 1988.pdf",
                0.9,
                routingScore: 0.6,
                domain: "encyclopedias",
                collection: "knowledge_encyclopedias_v2"),
            CreateChunk(
                "chem-acidity",
                "Кислотность и основность органических соединений определяются распределением электронной плотности и устойчивостью сопряженных форм.",
                "doc-chem",
                "Organic Chemistry.pdf",
                0.77,
                routingScore: 3.5,
                domain: "chemistry",
                collection: "knowledge_chemistry_v2")
        };

        var results = service.Rerank("Что определяет кислотность и основность органических соединений?", candidates, limit: 2);

        Assert.Equal(2, results.Count);
        Assert.Equal("chemistry", results[0].Metadata["domain"]);
    }

    [Fact]
    public void RerankingService_PrefersChemistryOverPhysicsForPhysicalChemistrySecondLawQuestion()
    {
        var service = new RerankingService();
        var candidates = new[]
        {
            CreateChunk(
                "physics-second-law",
                "Второй закон термодинамики в физике формулируется через рост энтропии изолированной системы.",
                "doc-phys",
                "Теоретическая_физика_5_Статистическая_физика_Ландау_Лифшиц.pdf",
                0.9,
                routingScore: 0.6,
                domain: "physics",
                collection: "knowledge_physics_v2"),
            CreateChunk(
                "chem-second-law",
                "В физической химии второй закон термодинамики связывает направление процессов с ростом энтропии и свободной энергией.",
                "doc-chem",
                "Physical Chemistry.pdf",
                0.77,
                routingScore: 3.4,
                domain: "chemistry",
                collection: "knowledge_chemistry_v2")
        };

        var results = service.Rerank("Как формулируется второй закон термодинамики в физической химии?", candidates, limit: 2);

        Assert.Equal(2, results.Count);
        Assert.Equal("chemistry", results[0].Metadata["domain"]);
    }

    [Fact]
    public void RerankingService_PrefersHistoryOverAnalysisStrategyForFirstWorldWarQuestion()
    {
        var service = new RerankingService();
        var candidates = new[]
        {
            CreateChunk(
                "analysis-ww1",
                "Стратегия войны, коалиции и непрямые действия в конфликтах.",
                "doc-analysis",
                "О ВОЙНЕ.pdf",
                0.9,
                routingScore: 0.7,
                domain: "analysis_strategy",
                collection: "knowledge_analysis_strategy_v2"),
            CreateChunk(
                "history-ww1",
                "Причины Первой мировой войны включают систему союзов, национализм и июльский кризис.",
                "doc-history",
                "World History Encyclopedia.pdf",
                0.77,
                routingScore: 3.4,
                domain: "history",
                collection: "knowledge_history_v2")
        };

        var results = service.Rerank("Какие причины привели к Первой мировой войне?", candidates, limit: 2);

        Assert.Equal(2, results.Count);
        Assert.Equal("history", results[0].Metadata["domain"]);
    }

    [Fact]
    public void RerankingService_PrefersHistoryOverPhilosophyForAncientEastQuestion()
    {
        var service = new RerankingService();
        var candidates = new[]
        {
            CreateChunk(
                "philosophy-east",
                "Философские представления о происхождении государства и общества.",
                "doc-philosophy",
                "History of Western Philosophy.epub",
                0.9,
                routingScore: 0.7,
                domain: "philosophy",
                collection: "knowledge_philosophy_v2"),
            CreateChunk(
                "history-east",
                "Первые государства Древнего Востока сформировались в долинах рек на основе ирригации и централизации власти.",
                "doc-history",
                "World History Encyclopedia.pdf",
                0.77,
                routingScore: 3.4,
                domain: "history",
                collection: "knowledge_history_v2")
        };

        var results = service.Rerank("Как возникли первые государства Древнего Востока?", candidates, limit: 2);

        Assert.Equal(2, results.Count);
        Assert.Equal("history", results[0].Metadata["domain"]);
    }

    [Fact]
    public void RerankingService_PrefersLinguisticsOverPhilosophyForGrammaticalNumberQuestion()
    {
        var service = new RerankingService();
        var candidates = new[]
        {
            CreateChunk(
                "philosophy-number",
                "Философские категории числа и множественности.",
                "doc-philosophy",
                "History of Western Philosophy.epub",
                0.89,
                routingScore: 0.7,
                domain: "philosophy",
                collection: "knowledge_philosophy_v2"),
            CreateChunk(
                "ling-number",
                "Грамматическое число в языках мира выражается морфологически, синтаксически или аналитически.",
                "doc-ling",
                "The World Atlas of Language Structures.pdf",
                0.77,
                routingScore: 3.5,
                domain: "linguistics",
                collection: "knowledge_linguistics_v2")
        };

        var results = service.Rerank("Как выражается грамматическое число в языках мира?", candidates, limit: 2);

        Assert.Equal(2, results.Count);
        Assert.Equal("linguistics", results[0].Metadata["domain"]);
    }

    [Fact]
    public void RerankingService_PrefersPhilosophyOverChemistryForSocratesMethodQuestion()
    {
        var service = new RerankingService();
        var candidates = new[]
        {
            CreateChunk(
                "chem-method",
                "Методы химического анализа и лабораторные процедуры.",
                "doc-chem",
                "General Chemistry.pdf",
                0.89,
                routingScore: 0.7,
                domain: "chemistry",
                collection: "knowledge_chemistry_v2"),
            CreateChunk(
                "philosophy-socrates",
                "Метод Сократа строится на диалоге, вопросах и выявлении противоречий в убеждениях собеседника.",
                "doc-philosophy",
                "The History of Western Philosophy.epub",
                0.77,
                routingScore: 3.4,
                domain: "philosophy",
                collection: "knowledge_philosophy_v2")
        };

        var results = service.Rerank("В чем состоит метод Сократа?", candidates, limit: 2);

        Assert.Equal(2, results.Count);
        Assert.Equal("philosophy", results[0].Metadata["domain"]);
    }

    [Fact]
    public void RerankingService_PrefersPhysicsOverMathForMaxwellQuestion()
    {
        var service = new RerankingService();
        var candidates = new[]
        {
            CreateChunk(
                "math-maxwell",
                "Системы уравнений и методы математического анализа.",
                "doc-math",
                "Mathematical Analysis.pdf",
                0.89,
                routingScore: 0.7,
                domain: "math",
                collection: "knowledge_math_v2"),
            CreateChunk(
                "physics-maxwell",
                "Уравнения Максвелла связывают электрическое и магнитное поля с зарядами и токами.",
                "doc-physics",
                "Теоретическая_физика_2_Теория_поля_Ландау_Лифшиц.pdf",
                0.77,
                routingScore: 3.4,
                domain: "physics",
                collection: "knowledge_physics_v2")
        };

        var results = service.Rerank("Как записываются уравнения Максвелла?", candidates, limit: 2);

        Assert.Equal(2, results.Count);
        Assert.Equal("physics", results[0].Metadata["domain"]);
    }

    [Fact]
    public void RerankingService_PrefersPhysicsOverMathForMaxwellQuestion_WhenMathAlsoMatchesEquations()
    {
        var service = new RerankingService();
        var candidates = new[]
        {
            CreateChunk(
                "math-maxwell-strong",
                "Уравнения и методы математического анализа для систем дифференциальных уравнений.",
                "doc-math",
                "Mathematical Analysis.pdf",
                0.91,
                routingScore: 1.2,
                domain: "math",
                collection: "knowledge_math_v2"),
            CreateChunk(
                "physics-maxwell-strong",
                "Уравнения Максвелла описывают динамику электрического и магнитного полей.",
                "doc-physics",
                "Теоретическая_физика_2_Теория_поля_Ландау_Лифшиц.pdf",
                0.77,
                routingScore: 3.4,
                domain: "physics",
                collection: "knowledge_physics_v2")
        };

        var results = service.Rerank("Как записываются уравнения Максвелла?", candidates, limit: 2);

        Assert.Equal(2, results.Count);
        Assert.Equal("physics", results[0].Metadata["domain"]);
    }

    [Fact]
    public void RerankingService_PrefersRoboticsOverComputerScienceForDenavitHartenbergQuestion()
    {
        var service = new RerankingService();
        var candidates = new[]
        {
            CreateChunk(
                "cs-dh",
                "Структуры данных и параметры конфигурации программных систем.",
                "doc-cs",
                "Clean Architecture.pdf",
                0.89,
                routingScore: 0.7,
                domain: "computer_science",
                collection: "knowledge_computer_science_v2"),
            CreateChunk(
                "robotics-dh",
                "Параметры Денавита-Хартенберга описывают геометрию звеньев и сочленений манипулятора.",
                "doc-robotics",
                "Introduction to Robotics.pdf",
                0.77,
                routingScore: 3.4,
                domain: "robotics",
                collection: "knowledge_robotics_v2")
        };

        var results = service.Rerank("Как записываются параметры Денавита-Хартенберга?", candidates, limit: 2);

        Assert.Equal(2, results.Count);
        Assert.Equal("robotics", results[0].Metadata["domain"]);
    }

    [Fact]
    public void RerankingService_PrefersRussianLiteratureOverPhilosophyForWarAndPeaceQuestion()
    {
        var service = new RerankingService();
        var candidates = new[]
        {
            CreateChunk(
                "philosophy-war-peace",
                "Философские размышления о войне, мире и морали.",
                "doc-philosophy",
                "History of Western Philosophy.epub",
                0.89,
                routingScore: 0.7,
                domain: "philosophy",
                collection: "knowledge_philosophy_v2"),
            CreateChunk(
                "russian-war-peace",
                "У Толстого тема войны и мира раскрывается через судьбы героев и историческую панораму общества.",
                "doc-russian",
                "War and Peace.epub",
                0.77,
                routingScore: 3.4,
                domain: "russian_lang_lit",
                collection: "knowledge_russian_lang_lit_v2")
        };

        var results = service.Rerank("Как раскрывается тема войны и мира у Толстого?", candidates, limit: 2);

        Assert.Equal(2, results.Count);
        Assert.Equal("russian_lang_lit", results[0].Metadata["domain"]);
    }

    [Fact]
    public void RerankingService_PrefersSciFiOverNeuroForFutureColonizationQuestion()
    {
        var service = new RerankingService();
        var candidates = new[]
        {
            CreateChunk(
                "neuro-future",
                "Мозг прогнозирует будущее состояние среды и формирует ожидания.",
                "doc-neuro",
                "Principles of Neural Science.pdf",
                0.89,
                routingScore: 0.7,
                domain: "neuro",
                collection: "knowledge_neuro_v2"),
            CreateChunk(
                "scifi-future",
                "Тема колонизации будущего в science fiction показывает экспансию человечества и устройство новых миров.",
                "doc-scifi",
                "Science Fiction Concepts.epub",
                0.77,
                routingScore: 3.5,
                domain: "sci_fi_concepts",
                collection: "knowledge_sci_fi_concepts_v2")
        };

        var results = service.Rerank("Как тема колонизации будущего раскрывается в science fiction?", candidates, limit: 2);

        Assert.Equal(2, results.Count);
        Assert.Equal("sci_fi_concepts", results[0].Metadata["domain"]);
    }

    [Fact]
    public void RerankingService_PrefersEntomologyOverBiologyForInsectWingQuestion()
    {
        var service = new RerankingService();
        var candidates = new[]
        {
            CreateChunk(
                "bio-wings",
                "Эволюция структур и органов у животных рассматривается в общей биологии.",
                "doc-bio",
                "Campbell Biology.pdf",
                0.89,
                routingScore: 0.7,
                domain: "biology",
                collection: "knowledge_biology_v2"),
            CreateChunk(
                "ento-wings",
                "Эволюция крыльев у насекомых связана с преобразованием покровов тела и ранним полетом.",
                "doc-ento",
                "Entomology.pdf",
                0.77,
                routingScore: 3.4,
                domain: "entomology",
                collection: "knowledge_entomology_v2")
        };

        var results = service.Rerank("Как эволюционировали крылья у насекомых?", candidates, limit: 2);

        Assert.Equal(2, results.Count);
        Assert.Equal("entomology", results[0].Metadata["domain"]);
    }

    [Fact]
    public void RerankingService_PrefersEntomologyOverHistoryForAntCastesQuestion()
    {
        var service = new RerankingService();
        var candidates = new[]
        {
            CreateChunk(
                "history-castes",
                "Исторические кастовые системы и социальные страты общества.",
                "doc-history",
                "World History Encyclopedia.pdf",
                0.89,
                routingScore: 0.7,
                domain: "history",
                collection: "knowledge_history_v2"),
            CreateChunk(
                "ento-castes",
                "Касты и коммуникация у муравьев обеспечивают разделение труда и координацию колонии.",
                "doc-ento",
                "Entomology.pdf",
                0.77,
                routingScore: 3.4,
                domain: "entomology",
                collection: "knowledge_entomology_v2")
        };

        var results = service.Rerank("Что известно о кастах и коммуникации у муравьев?", candidates, limit: 2);

        Assert.Equal(2, results.Count);
        Assert.Equal("entomology", results[0].Metadata["domain"]);
    }

    [Fact]
    public void RerankingService_PrefersHistoricalArchiveOverEncyclopediasForIbsenQuestion()
    {
        var service = new RerankingService();
        var candidates = new[]
        {
            CreateChunk(
                "encyc-ibsen",
                "Краткая словарная справка об Ибсене.",
                "doc-encyc",
                "Прохоров А.М. СЭС 1988.pdf",
                0.89,
                routingScore: 0.7,
                domain: "encyclopedias",
                collection: "knowledge_encyclopedias_v2"),
            CreateChunk(
                "archive-ibsen",
                "Ибсен в историко-энциклопедическом контексте: драматург, эпоха и культурное влияние.",
                "doc-archive",
                "Большая Советская Энциклопедия.pdf",
                0.77,
                routingScore: 3.4,
                domain: "historical_encyclopedias",
                collection: "knowledge_historical_encyclopedias_v2")
        };

        var results = service.Rerank("Кто такой Ибсен?", candidates, limit: 2);

        Assert.Equal(2, results.Count);
        Assert.Equal("historical_encyclopedias", results[0].Metadata["domain"]);
    }

    [Fact]
    public void RerankingService_PrefersAnalysisStrategyOverEconomicsForFastSlowThinkingQuestion()
    {
        var service = new RerankingService();
        var candidates = new[]
        {
            CreateChunk(
                "econ-fast-slow",
                "Экономическое поведение и когнитивные искажения на рынках.",
                "doc-econ",
                "Principles of Economics.pdf",
                0.89,
                routingScore: 0.7,
                domain: "economics",
                collection: "knowledge_economics_v2"),
            CreateChunk(
                "analysis-fast-slow",
                "Эвристика быстрого и медленного мышления описывает два режима принятия решений.",
                "doc-analysis",
                "Thinking, Fast and Slow.pdf",
                0.77,
                routingScore: 3.4,
                domain: "analysis_strategy",
                collection: "knowledge_analysis_strategy_v2")
        };

        var results = service.Rerank("Как работает эвристика быстрого и медленного мышления?", candidates, limit: 2);

        Assert.Equal(2, results.Count);
        Assert.Equal("analysis_strategy", results[0].Metadata["domain"]);
    }

    [Fact]
    public void RerankingService_PrefersMedicineOverComputerScienceForTypeTwoDiabetesQuestion()
    {
        var service = new RerankingService();
        var candidates = new[]
        {
            CreateChunk(
                "cs-type-two",
                "Системы второго типа и типы данных в программировании.",
                "doc-cs",
                "Code Complete.pdf",
                0.89,
                routingScore: 0.7,
                domain: "computer_science",
                collection: "knowledge_computer_science_v2"),
            CreateChunk(
                "med-diabetes",
                "Диабет второго типа связан с инсулинорезистентностью и нарушением регуляции уровня глюкозы.",
                "doc-med",
                "The Merck Manual Home Health Handbook",
                0.77,
                routingScore: 3.4,
                domain: "medicine",
                collection: "knowledge_medicine_v2")
        };

        var results = service.Rerank("Что такое диабет второго типа?", candidates, limit: 2);

        Assert.Equal(2, results.Count);
        Assert.Equal("medicine", results[0].Metadata["domain"]);
    }

    [Fact]
    public void RerankingService_PrefersSocialSciencesOverHistoryForPrincipledNegotiationsQuestion()
    {
        var service = new RerankingService();
        var candidates = new[]
        {
            CreateChunk(
                "history-negotiations",
                "История международных переговоров и дипломатии.",
                "doc-history",
                "World History Encyclopedia.pdf",
                0.89,
                routingScore: 0.7,
                domain: "history",
                collection: "knowledge_history_v2"),
            CreateChunk(
                "social-negotiations",
                "Принципиальные переговоры строятся на интересах сторон, критериях и отделении людей от проблемы.",
                "doc-social",
                "Getting to Yes.pdf",
                0.77,
                routingScore: 3.4,
                domain: "social_sciences",
                collection: "knowledge_social_sciences_v2")
        };

        var results = service.Rerank("Как вести принципиальные переговоры?", candidates, limit: 2);

        Assert.Equal(2, results.Count);
        Assert.Equal("social_sciences", results[0].Metadata["domain"]);
    }

    [Fact]
    public void RerankingService_PrefersRoboticsOverEncyclopediasForCyberneticsQuestion()
    {
        var service = new RerankingService();
        var candidates = new[]
        {
            CreateChunk(
                "encyc-cybernetics",
                "Краткая словарная заметка о кибернетике.",
                "doc-encyc",
                "Прохоров А.М. СЭС 1988.pdf",
                0.89,
                routingScore: 0.7,
                domain: "encyclopedias",
                collection: "knowledge_encyclopedias_v2"),
            CreateChunk(
                "robotics-cybernetics",
                "Кибернетика изучает управление, обратную связь и поведение сложных систем и машин.",
                "doc-robotics",
                "Introduction to Robotics.pdf",
                0.77,
                routingScore: 3.4,
                domain: "robotics",
                collection: "knowledge_robotics_v2")
        };

        var results = service.Rerank("Что изучает кибернетика?", candidates, limit: 2);

        Assert.Equal(2, results.Count);
        Assert.Equal("robotics", results[0].Metadata["domain"]);
    }

    [Fact]
    public void RerankingService_AnnotatesTopicalFitAndPenalizesGenericEncyclopediaForNarrowRoboticsQuestion()
    {
        var service = new RerankingService();
        var candidates = new[]
        {
            CreateChunk(
                "encyc-dh",
                "Краткая энциклопедическая заметка о параметрах и системах.",
                "doc-encyc",
                "СЭС.pdf",
                0.92,
                routingScore: 0.6,
                domain: "encyclopedias",
                collection: "knowledge_encyclopedias_v2"),
            CreateChunk(
                "robotics-dh-topical",
                "Параметры Денавита-Хартенберга описывают геометрию звеньев, оси сочленений и преобразования манипулятора.",
                "doc-robotics",
                "Параметры Денавита-Хартенберга и кинематика роботов.pdf",
                0.77,
                routingScore: 3.4,
                domain: "robotics",
                collection: "knowledge_robotics_v2")
        };

        var results = service.Rerank(
            "Как записываются параметры Денавита-Хартенберга?",
            candidates,
            limit: 2,
            options: new RetrievalRequestOptions(
                Purpose: RetrievalPurpose.FactualLookup,
                PreferTraceableChunks: true));

        Assert.Equal(2, results.Count);
        Assert.Equal("robotics", results[0].Metadata["domain"]);
        Assert.NotEqual("low", results[0].Metadata["topical_fit_label"]);
        Assert.Equal("low", results[1].Metadata["topical_fit_label"]);
        Assert.Equal("FactualLookup", results[0].Metadata["retrieval_purpose"]);
        Assert.Equal("true", results[1].Metadata["topical_fit_generic_domain"]);
    }

    [Fact]
    public void RerankingService_SourceDiversityGuard_PullsInAlternativeSource_WhenTopResultsAreOverConcentrated()
    {
        var service = new RerankingService();
        var candidates = new[]
        {
            CreateChunk(
                "robotics-a",
                "Параметры Денавита-Хартенберга задают геометрию первого звена манипулятора.",
                "doc-robotics-a",
                "Robotics Handbook A.pdf",
                0.93,
                routingScore: 3.9,
                domain: "robotics",
                sourcePath: "https://docs.example.org/robotics/denavit-hartenberg"),
            CreateChunk(
                "robotics-b",
                "Матрицы преобразований в нотации Денавита-Хартенберга связывают соседние звенья.",
                "doc-robotics-b",
                "Robotics Handbook B.pdf",
                0.91,
                routingScore: 3.7,
                domain: "robotics",
                sourcePath: "https://docs.example.org/robotics/denavit-hartenberg"),
            CreateChunk(
                "robotics-c",
                "Для каждого сустава задаются alpha, a, d и theta.",
                "doc-robotics-c",
                "Robotics Handbook C.pdf",
                0.89,
                routingScore: 3.6,
                domain: "robotics",
                sourcePath: "https://docs.example.org/robotics/denavit-hartenberg"),
            CreateChunk(
                "robotics-alt",
                "Обзор кинематики манипуляторов и систем координат для сравнения разных нотаций.",
                "doc-robotics-alt",
                "Manipulator Frames.pdf",
                0.83,
                routingScore: 3.2,
                domain: "robotics",
                sourcePath: "https://alt.example.org/robotics/reference-frames",
                collection: "knowledge_robotics_alt_v2")
        };

        var results = service.Rerank(
            "Как записываются параметры Денавита-Хартенберга?",
            candidates,
            limit: 3,
            options: new RetrievalRequestOptions(
                Purpose: RetrievalPurpose.FactualLookup,
                PreferTraceableChunks: true));

        Assert.Equal(3, results.Count);
        Assert.True(results.Select(chunk => chunk.Metadata["source_diversity_source_key"]).Distinct(StringComparer.OrdinalIgnoreCase).Count() >= 2);
        Assert.All(results, chunk => Assert.True(double.Parse(chunk.Metadata["source_diversity_dominance"], System.Globalization.CultureInfo.InvariantCulture) <= 0.67d));
        Assert.Contains(results, chunk => string.Equals(chunk.Collection, "knowledge_robotics_alt_v2", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ContextAssemblyService_GlobalRouting_CanIncludeChemistryForOrganicAcidityQuestion()
    {
        var vectorStore = new Mock<IVectorStore>();
        var structuredStore = new Mock<IStructuredVectorStore>();
        var ai = new Mock<AILink>("http://localhost:11434", "qwen");

        ai.Setup(x => x.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new float[768]);

        structuredStore.Setup(x => x.ListCollectionsAsync("knowledge_", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { "knowledge_chemistry_v2", "knowledge_encyclopedias_v2", "knowledge_art_culture_v2" });
        structuredStore.Setup(x => x.GetCollectionPointCountAsync("knowledge_chemistry_v2", It.IsAny<CancellationToken>())).ReturnsAsync(18000);
        structuredStore.Setup(x => x.GetCollectionPointCountAsync("knowledge_encyclopedias_v2", It.IsAny<CancellationToken>())).ReturnsAsync(24000);
        structuredStore.Setup(x => x.GetCollectionPointCountAsync("knowledge_art_culture_v2", It.IsAny<CancellationToken>())).ReturnsAsync(5000);

        vectorStore.Setup(x => x.ScrollMetadataAsync("knowledge_chemistry_v2", It.IsAny<int>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<KnowledgeChunk> { CreateChunk("chem-profile", "Органические соединения, кислотность и основность.", "doc-chem-profile", "Organic Chemistry.pdf", 0.6, domain: "chemistry") });
        vectorStore.Setup(x => x.ScrollMetadataAsync("knowledge_encyclopedias_v2", It.IsAny<int>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<KnowledgeChunk> { CreateChunk("encyc-profile", "Краткие словарные определения общих терминов.", "doc-encyc-profile", "СЭС.pdf", 0.6, domain: "encyclopedias") });
        vectorStore.Setup(x => x.ScrollMetadataAsync("knowledge_art_culture_v2", It.IsAny<int>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<KnowledgeChunk> { CreateChunk("art-profile", "История искусства и культуры.", "doc-art-profile", "Story of Art.pdf", 0.6, domain: "art_culture") });

        var searchedCollections = new List<string>();
        vectorStore.Setup(x => x.SearchAsync(It.IsAny<float[]>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((float[] _, string collection, int _, CancellationToken _) =>
            {
                searchedCollections.Add(collection);
                return collection switch
                {
                    "knowledge_chemistry_v2" => new List<KnowledgeChunk> { CreateChunk("chem-hit", "Кислотность и основность органических соединений зависят от электронной структуры.", "doc-chem-hit", "Organic Chemistry.pdf", 0.77, domain: "chemistry") },
                    "knowledge_encyclopedias_v2" => new List<KnowledgeChunk> { CreateChunk("encyc-hit", "Кислотность как общий термин.", "doc-encyc-hit", "СЭС.pdf", 0.88, domain: "encyclopedias") },
                    _ => new List<KnowledgeChunk>()
                };
            });

        var service = new ContextAssemblyService(vectorStore.Object, structuredStore.Object, ai.Object, new RerankingService());

        var results = await service.AssembleAsync("Что определяет кислотность и основность органических соединений?", domain: null, limit: 3, pipelineVersion: "v2", expandContext: false, ct: CancellationToken.None);

        Assert.Contains("knowledge_chemistry_v2", searchedCollections);
        Assert.NotEmpty(results);
        Assert.Equal("chemistry", results[0].Metadata["domain"]);
    }

    [Fact]
    public async Task ContextAssemblyService_GlobalRouting_CanIncludeEnglishLiteratureForDoublethinkQuestion()
    {
        var vectorStore = new Mock<IVectorStore>();
        var structuredStore = new Mock<IStructuredVectorStore>();
        var ai = new Mock<AILink>("http://localhost:11434", "qwen");

        ai.Setup(x => x.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new float[768]);

        structuredStore.Setup(x => x.ListCollectionsAsync("knowledge_", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { "knowledge_english_lang_lit_v2", "knowledge_russian_lang_lit_v2" });
        structuredStore.Setup(x => x.GetCollectionPointCountAsync("knowledge_english_lang_lit_v2", It.IsAny<CancellationToken>())).ReturnsAsync(6000);
        structuredStore.Setup(x => x.GetCollectionPointCountAsync("knowledge_russian_lang_lit_v2", It.IsAny<CancellationToken>())).ReturnsAsync(7000);

        vectorStore.Setup(x => x.ScrollMetadataAsync("knowledge_english_lang_lit_v2", It.IsAny<int>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<KnowledgeChunk> { CreateChunk("eng-profile", "Doublethink in Orwell and English literature of the twentieth century.", "doc-eng-profile", "1984.pdf", 0.6, domain: "english_lang_lit") });
        vectorStore.Setup(x => x.ScrollMetadataAsync("knowledge_russian_lang_lit_v2", It.IsAny<int>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<KnowledgeChunk> { CreateChunk("rus-profile", "Русская литература XIX и XX веков.", "doc-rus-profile", "History of Russian Literature.pdf", 0.6, domain: "russian_lang_lit") });

        var searchedCollections = new List<string>();
        vectorStore.Setup(x => x.SearchAsync(It.IsAny<float[]>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((float[] _, string collection, int _, CancellationToken _) =>
            {
                searchedCollections.Add(collection);
                return collection switch
                {
                    "knowledge_english_lang_lit_v2" => new List<KnowledgeChunk> { CreateChunk("eng-hit", "Doublethink у Оруэлла описывает способность удерживать противоречащие убеждения.", "doc-eng-hit", "1984.pdf", 0.77, domain: "english_lang_lit") },
                    "knowledge_russian_lang_lit_v2" => new List<KnowledgeChunk> { CreateChunk("rus-hit", "Русская литература XX века.", "doc-rus-hit", "History of Russian Literature.pdf", 0.88, domain: "russian_lang_lit") },
                    _ => new List<KnowledgeChunk>()
                };
            });

        var service = new ContextAssemblyService(vectorStore.Object, structuredStore.Object, ai.Object, new RerankingService());

        var results = await service.AssembleAsync("Что такое двоемыслие в английской литературе XX века?", domain: null, limit: 3, pipelineVersion: "v2", expandContext: false, ct: CancellationToken.None);

        Assert.Contains("knowledge_english_lang_lit_v2", searchedCollections);
        Assert.NotEmpty(results);
        Assert.Equal("english_lang_lit", results[0].Metadata["domain"]);
    }

    [Fact]
    public async Task ContextAssemblyService_GlobalRouting_CanIncludePhilosophyForSocratesMethodQuestion()
    {
        var vectorStore = new Mock<IVectorStore>();
        var structuredStore = new Mock<IStructuredVectorStore>();
        var ai = new Mock<AILink>("http://localhost:11434", "qwen");

        ai.Setup(x => x.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new float[768]);

        structuredStore.Setup(x => x.ListCollectionsAsync("knowledge_", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { "knowledge_philosophy_v2", "knowledge_chemistry_v2", "knowledge_computer_science_v2" });
        structuredStore.Setup(x => x.GetCollectionPointCountAsync("knowledge_philosophy_v2", It.IsAny<CancellationToken>())).ReturnsAsync(7000);
        structuredStore.Setup(x => x.GetCollectionPointCountAsync("knowledge_chemistry_v2", It.IsAny<CancellationToken>())).ReturnsAsync(18000);
        structuredStore.Setup(x => x.GetCollectionPointCountAsync("knowledge_computer_science_v2", It.IsAny<CancellationToken>())).ReturnsAsync(25000);

        vectorStore.Setup(x => x.ScrollMetadataAsync("knowledge_philosophy_v2", It.IsAny<int>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<KnowledgeChunk> { CreateChunk("phil-profile", "Сократ, диалог и сократический метод.", "doc-phil-profile", "The History of Western Philosophy.epub", 0.6, domain: "philosophy") });
        vectorStore.Setup(x => x.ScrollMetadataAsync("knowledge_chemistry_v2", It.IsAny<int>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<KnowledgeChunk> { CreateChunk("chem-profile", "Методы химического анализа.", "doc-chem-profile", "General Chemistry.pdf", 0.6, domain: "chemistry") });
        vectorStore.Setup(x => x.ScrollMetadataAsync("knowledge_computer_science_v2", It.IsAny<int>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<KnowledgeChunk> { CreateChunk("cs-profile", "Методы проектирования и анализа систем.", "doc-cs-profile", "Code Complete.pdf", 0.6, domain: "computer_science") });

        var searchedCollections = new List<string>();
        vectorStore.Setup(x => x.SearchAsync(It.IsAny<float[]>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((float[] _, string collection, int _, CancellationToken _) =>
            {
                searchedCollections.Add(collection);
                return collection switch
                {
                    "knowledge_philosophy_v2" => new List<KnowledgeChunk> { CreateChunk("phil-hit", "Метод Сократа выявляет противоречия через вопросы и диалог.", "doc-phil-hit", "The History of Western Philosophy.epub", 0.77, domain: "philosophy") },
                    "knowledge_chemistry_v2" => new List<KnowledgeChunk> { CreateChunk("chem-hit", "Методы химического анализа.", "doc-chem-hit", "General Chemistry.pdf", 0.88, domain: "chemistry") },
                    _ => new List<KnowledgeChunk>()
                };
            });

        var service = new ContextAssemblyService(vectorStore.Object, structuredStore.Object, ai.Object, new RerankingService());

        var results = await service.AssembleAsync("В чем состоит метод Сократа?", domain: null, limit: 3, pipelineVersion: "v2", expandContext: false, ct: CancellationToken.None);

        Assert.Contains("knowledge_philosophy_v2", searchedCollections);
        Assert.NotEmpty(results);
        Assert.Equal("philosophy", results[0].Metadata["domain"]);
    }

    [Fact]
    public async Task ContextAssemblyService_GlobalRouting_CanIncludeRoboticsForDenavitHartenbergQuestion()
    {
        var vectorStore = new Mock<IVectorStore>();
        var structuredStore = new Mock<IStructuredVectorStore>();
        var ai = new Mock<AILink>("http://localhost:11434", "qwen");

        ai.Setup(x => x.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new float[768]);

        structuredStore.Setup(x => x.ListCollectionsAsync("knowledge_", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { "knowledge_robotics_v2", "knowledge_computer_science_v2", "knowledge_encyclopedias_v2" });
        structuredStore.Setup(x => x.GetCollectionPointCountAsync("knowledge_robotics_v2", It.IsAny<CancellationToken>())).ReturnsAsync(4000);
        structuredStore.Setup(x => x.GetCollectionPointCountAsync("knowledge_computer_science_v2", It.IsAny<CancellationToken>())).ReturnsAsync(25000);
        structuredStore.Setup(x => x.GetCollectionPointCountAsync("knowledge_encyclopedias_v2", It.IsAny<CancellationToken>())).ReturnsAsync(24000);

        vectorStore.Setup(x => x.ScrollMetadataAsync("knowledge_robotics_v2", It.IsAny<int>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<KnowledgeChunk> { CreateChunk("robot-profile", "Параметры Денавита-Хартенберга и кинематика манипуляторов.", "doc-robot-profile", "Introduction to Robotics.pdf", 0.6, domain: "robotics") });
        vectorStore.Setup(x => x.ScrollMetadataAsync("knowledge_computer_science_v2", It.IsAny<int>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<KnowledgeChunk> { CreateChunk("cs-profile", "Параметры конфигурации программных систем.", "doc-cs-profile", "Code Complete.pdf", 0.6, domain: "computer_science") });
        vectorStore.Setup(x => x.ScrollMetadataAsync("knowledge_encyclopedias_v2", It.IsAny<int>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<KnowledgeChunk> { CreateChunk("encyc-profile", "Словарные статьи о параметрах и системах.", "doc-encyc-profile", "СЭС.pdf", 0.6, domain: "encyclopedias") });

        var searchedCollections = new List<string>();
        vectorStore.Setup(x => x.SearchAsync(It.IsAny<float[]>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((float[] _, string collection, int _, CancellationToken _) =>
            {
                searchedCollections.Add(collection);
                return collection switch
                {
                    "knowledge_robotics_v2" => new List<KnowledgeChunk> { CreateChunk("robot-hit", "Параметры Денавита-Хартенберга задают относительные преобразования звеньев.", "doc-robot-hit", "Introduction to Robotics.pdf", 0.77, domain: "robotics") },
                    "knowledge_computer_science_v2" => new List<KnowledgeChunk> { CreateChunk("cs-hit", "Параметры конфигурации программных модулей.", "doc-cs-hit", "Code Complete.pdf", 0.88, domain: "computer_science") },
                    _ => new List<KnowledgeChunk>()
                };
            });

        var service = new ContextAssemblyService(vectorStore.Object, structuredStore.Object, ai.Object, new RerankingService());

        var results = await service.AssembleAsync("Как записываются параметры Денавита-Хартенберга?", domain: null, limit: 3, pipelineVersion: "v2", expandContext: false, ct: CancellationToken.None);

        Assert.Contains("knowledge_robotics_v2", searchedCollections);
        Assert.NotEmpty(results);
        Assert.Equal("robotics", results[0].Metadata["domain"]);
    }

    private static KnowledgeChunk CreateChunk(string id, string content, string documentId, string title, double vectorScore, double routingScore = 0d, string domain = "physics", string? sourcePath = null, string? collection = null, Dictionary<string, string>? extraMetadata = null)
    {
        var resolvedCollection = collection ?? $"knowledge_{domain}_v2";
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["document_id"] = documentId,
            ["title"] = title,
            ["source_path"] = sourcePath ?? title,
            ["vector_score"] = vectorScore.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["chunk_role"] = "standalone",
            ["page_start"] = "1",
            ["section_path"] = "chapter",
            ["domain"] = domain,
            ["collection"] = resolvedCollection
        };

        if (routingScore > 0d)
        {
            metadata["routing_score"] = routingScore.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        if (extraMetadata is not null)
        {
            foreach (var pair in extraMetadata)
            {
                metadata[pair.Key] = pair.Value;
            }
        }

        return new KnowledgeChunk(
            id,
            content,
            Array.Empty<float>(),
            metadata,
            resolvedCollection);
    }
}

