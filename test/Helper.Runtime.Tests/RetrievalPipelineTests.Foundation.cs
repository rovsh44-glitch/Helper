using Helper.Runtime.Core;
using Helper.Runtime.Infrastructure;
using Helper.Runtime.Knowledge.Retrieval;
using Moq;

namespace Helper.Runtime.Tests;

public partial class RetrievalPipelineTests
{
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

}
