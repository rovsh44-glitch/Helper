using Helper.Runtime.Core;
using Helper.Runtime.Infrastructure;
using Helper.Runtime.Knowledge.Retrieval;
using Moq;

namespace Helper.Runtime.Tests;

public partial class RetrievalPipelineTests
{
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
}
