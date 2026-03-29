using Helper.Runtime.Core;
using Helper.Runtime.Infrastructure;
using Helper.Runtime.Knowledge;
using Helper.Runtime.Knowledge.Chunking;
using Helper.Runtime.Knowledge.Retrieval;

namespace Helper.Api.Hosting;

public static partial class ServiceRegistrationExtensions
{
    private static IServiceCollection AddHelperDocumentParserServices(this IServiceCollection services)
    {
        services.AddSingleton<IIndexingTelemetrySink, IndexingTelemetrySink>();
        services.AddSingleton<KnowledgeDomainResolver>();
        services.AddSingleton<StructuredLibrarianV2Pipeline>();
        services.AddSingleton<IDocumentNormalizer, DocumentNormalizationService>();
        services.AddSingleton<IStructureRecoveryService, StructureRecoveryService>();
        services.AddSingleton<IChunkingStrategyResolver, ChunkingStrategyResolver>();
        services.AddSingleton<IChunkBuilder, RecursiveChunkBuilder>();
        services.AddSingleton<IChunkBuilder, StructuralChunkBuilder>();
        services.AddSingleton<IChunkBuilder, ParentChildChunkBuilder>();
        services.AddSingleton<SemanticChunkBoundaryService>();
        services.AddSingleton<IRerankingService, RerankingService>();
        services.AddSingleton<IRetrievalContextAssembler, ContextAssemblyService>();

        services.AddSingleton<IStructuredDocumentParser>(sp => new StructuredPdfParser(sp.GetRequiredService<AILink>()));
        services.AddSingleton<IStructuredDocumentParser, StructuredEpubParser>();
        services.AddSingleton<IStructuredDocumentParser, StructuredHtmlParser>();
        services.AddSingleton<IStructuredDocumentParser, StructuredDocxParser>();
        services.AddSingleton<IStructuredDocumentParser, StructuredFb2Parser>();
        services.AddSingleton<IStructuredDocumentParser, StructuredMarkdownParser>();
        services.AddSingleton<IStructuredDocumentParser>(sp => new StructuredDjvuParser(sp.GetRequiredService<AILink>()));
        services.AddSingleton<IStructuredDocumentParser, StructuredChmParser>();
        services.AddSingleton<IStructuredDocumentParser, StructuredZimParser>();

        services.AddSingleton<IDocumentParser, PdfParser>();
        services.AddSingleton<IDocumentParser, DocxParser>();
        services.AddSingleton<IDocumentParser, ImageParser>();
        services.AddSingleton<IDocumentParser, DjvuParser>();
        services.AddSingleton<IDocumentParser, EpubParser>();
        services.AddSingleton<IDocumentParser, HtmlParser>();
        services.AddSingleton<IDocumentParser, Fb2Parser>();
        services.AddSingleton<IDocumentParser, MarkdownParser>();
        services.AddSingleton<IDocumentParser, ChmParser>();
        services.AddSingleton<IDocumentParser, ZimParser>();

        return services;
    }
}

