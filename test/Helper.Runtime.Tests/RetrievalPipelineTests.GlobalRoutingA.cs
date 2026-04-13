using Helper.Runtime.Core;
using Helper.Runtime.Infrastructure;
using Helper.Runtime.Knowledge.Retrieval;
using Moq;

namespace Helper.Runtime.Tests;

public partial class RetrievalPipelineTests
{
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

}
