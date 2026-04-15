using Helper.Runtime.Core;
using Helper.Runtime.Infrastructure;
using Helper.Runtime.Knowledge.Chunking;
using DocumentFormatType = Helper.Runtime.Core.DocumentFormat;
using System.Diagnostics;

namespace Helper.Runtime.Knowledge;

public sealed class StructuredLibrarianV2Pipeline
{
    private static readonly int DefaultMaxChunksPerDocument = StructuredParserUtilities.ReadBoundedIntEnvironment("HELPER_INDEX_MAX_CHUNKS_PER_DOCUMENT", 1600, 128, 64000);
    private static readonly int LargeReferenceMaxChunksPerDocument = StructuredParserUtilities.ReadBoundedIntEnvironment("HELPER_INDEX_MAX_CHUNKS_PER_DOCUMENT_LARGE_REFERENCE", 24000, 1600, 120000);
    private static readonly int LargeReferenceMinPages = StructuredParserUtilities.ReadBoundedIntEnvironment("HELPER_INDEX_LARGE_REFERENCE_MIN_PAGES", 400, 64, 10000);
    private static readonly int LargeDocumentMaxChunksPerDocument = StructuredParserUtilities.ReadBoundedIntEnvironment("HELPER_INDEX_MAX_CHUNKS_PER_DOCUMENT_LARGE_DOCUMENT", 12000, 1600, 120000);
    private static readonly int LargeDocumentMinPages = StructuredParserUtilities.ReadBoundedIntEnvironment("HELPER_INDEX_LARGE_DOCUMENT_MIN_PAGES", 250, 64, 10000);
    private static readonly int LargeDocumentMinBlocks = StructuredParserUtilities.ReadBoundedIntEnvironment("HELPER_INDEX_LARGE_DOCUMENT_MIN_BLOCKS", 400, 64, 100000);
    private static readonly int LargeDocumentMinObservedChunks = StructuredParserUtilities.ReadBoundedIntEnvironment("HELPER_INDEX_LARGE_DOCUMENT_MIN_OBSERVED_CHUNKS", 1700, 1601, 120000);

    private readonly IVectorStore _store;
    private readonly AILink _ai;
    private readonly IDocumentNormalizer _normalizer;
    private readonly IStructureRecoveryService _structureRecovery;
    private readonly IChunkingStrategyResolver _chunkingStrategyResolver;
    private readonly IReadOnlyList<IChunkBuilder> _chunkBuilders;
    private readonly IIndexingTelemetrySink _telemetry;
    private readonly KnowledgeDomainResolver _domainResolver;
    private readonly ChunkDescriptorEnrichmentService _chunkDescriptorEnrichment;

    public StructuredLibrarianV2Pipeline(
        IVectorStore store,
        AILink ai,
        IDocumentNormalizer normalizer,
        IStructureRecoveryService structureRecovery,
        IChunkingStrategyResolver chunkingStrategyResolver,
        IEnumerable<IChunkBuilder> chunkBuilders,
        IIndexingTelemetrySink telemetry,
        KnowledgeDomainResolver domainResolver)
    {
        _store = store;
        _ai = ai;
        _normalizer = normalizer;
        _structureRecovery = structureRecovery;
        _chunkingStrategyResolver = chunkingStrategyResolver;
        _chunkBuilders = chunkBuilders.ToList();
        _telemetry = telemetry;
        _domainResolver = domainResolver;
        _chunkDescriptorEnrichment = new ChunkDescriptorEnrichmentService();
    }

    public async Task<string> IndexAsync(
        string filePath,
        IStructuredDocumentParser parser,
        string? initialDomain,
        string fileHash,
        Func<double, Task>? onProgress,
        CancellationToken ct)
    {
        await ReportProgressAsync(onProgress, 5);
        var parsed = await parser.ParseStructuredAsync(
            filePath,
            progress => ReportProgressAsync(onProgress, 5 + (Math.Clamp(progress, 0, 100) * 0.10)),
            ct);
        _telemetry.Report(new IndexingTelemetry("v2", parser.ParserVersion));

        await ReportProgressAsync(onProgress, 15);
        var normalized = await _normalizer.NormalizeAsync(parsed, ct);

        await ReportProgressAsync(onProgress, 25);
        var recovered = await _structureRecovery.RecoverAsync(normalized, ct);

        var flatContent = StructuredDocumentFormatter.Flatten(recovered);
        var rawDomain = await _domainResolver.ResolveAsync(flatContent, initialDomain, ct) ?? "generic";
        var domain = KnowledgeCollectionNaming.ResolveDomain(rawDomain, filePath, recovered.Title);
        var plan = await _chunkingStrategyResolver.ResolveAsync(recovered, domain, ct);
        var chunkBuilder = _chunkBuilders.FirstOrDefault(builder => builder.CanBuild(plan.Strategy))
            ?? throw new InvalidOperationException($"No chunk builder registered for strategy '{plan.Strategy}'.");

        _telemetry.Report(new IndexingTelemetry(
            PipelineVersion: plan.PipelineVersion,
            ParserVersion: parser.ParserVersion,
            ChunkingStrategy: plan.Strategy.ToString(),
            CurrentSection: recovered.Blocks.FirstOrDefault(static block => !string.IsNullOrWhiteSpace(block.SectionPath))?.SectionPath,
            CurrentPageStart: recovered.Pages.FirstOrDefault()?.PageNumber,
            CurrentPageEnd: recovered.Pages.LastOrDefault()?.PageNumber));

        await ReportProgressAsync(onProgress, 35);
        var chunkBuildStopwatch = Stopwatch.StartNew();
        var rawChunks = (await chunkBuilder.BuildChunksAsync(recovered, plan, ct)).ToList();
        chunkBuildStopwatch.Stop();
        Console.WriteLine($"[StructuredLibrarianV2Pipeline] Chunk build completed for {Path.GetFileName(filePath)} in {chunkBuildStopwatch.ElapsedMilliseconds} ms. Total chunks before compaction: {rawChunks.Count}.");
        var allChunks = _chunkDescriptorEnrichment
            .Enrich(StructuredChunkCompactor.Compact(rawChunks, plan))
            .ToList();
        if (allChunks.Count != rawChunks.Count)
        {
            Console.WriteLine($"[StructuredLibrarianV2Pipeline] Chunk compaction reduced {Path.GetFileName(filePath)} from {rawChunks.Count} to {allChunks.Count} chunks.");
        }
        var maxChunksPerDocument = ResolveMaxChunksPerDocument(recovered, domain, plan, allChunks.Count);
        if (maxChunksPerDocument != DefaultMaxChunksPerDocument)
        {
            Console.WriteLine($"[StructuredLibrarianV2Pipeline] Adaptive chunk cap for {Path.GetFileName(filePath)}: {maxChunksPerDocument} chunks (domain={domain}, pages={recovered.Pages.Count}, strategy={plan.Strategy}).");
        }
        var wasTruncated = allChunks.Count > maxChunksPerDocument;
        var chunks = wasTruncated
            ? allChunks.Take(maxChunksPerDocument).ToList()
            : allChunks;
        if (wasTruncated)
        {
            Console.WriteLine($"[StructuredLibrarianV2Pipeline] Backpressure applied for {Path.GetFileName(filePath)}: truncated to {maxChunksPerDocument} of {allChunks.Count} chunks.");
        }

        var qualityGateStopwatch = Stopwatch.StartNew();
        StructuredIndexQualityGate.EnsureAccepted(filePath, recovered, chunks, flatContent, wasTruncated, allChunks.Count);
        qualityGateStopwatch.Stop();
        Console.WriteLine($"[StructuredLibrarianV2Pipeline] Quality gate accepted {Path.GetFileName(filePath)} in {qualityGateStopwatch.ElapsedMilliseconds} ms.");

        var collection = KnowledgeCollectionNaming.ResolveCollectionName(domain, plan.PipelineVersion, filePath, recovered.Title);
        var year = recovered.PublishedYear ?? DetectYear(recovered.Title, flatContent);
        for (var index = 0; index < chunks.Count; index++)
        {
            ct.ThrowIfCancellationRequested();
            var structuredChunk = chunks[index];
            _telemetry.Report(new IndexingTelemetry(
                PipelineVersion: plan.PipelineVersion,
                ParserVersion: parser.ParserVersion,
                ChunkingStrategy: plan.Strategy.ToString(),
                CurrentSection: structuredChunk.SectionPath,
                CurrentPageStart: structuredChunk.PageStart,
                CurrentPageEnd: structuredChunk.PageEnd));

            var content = BuildStorageContent(recovered.Title, year, structuredChunk);
            var embeddingInput = BuildEmbeddingInput(recovered.Title, structuredChunk);
            var metadata = BuildStructuredMetadata(filePath, recovered, structuredChunk, domain, fileHash, year, plan, parser.ParserVersion);
            var embedding = await _ai.EmbedAsync(embeddingInput, ct);
            await _store.UpsertAsync(new KnowledgeChunk(
                Id: structuredChunk.ChunkId,
                Content: content,
                Embedding: embedding,
                Metadata: metadata,
                Collection: collection), ct);

            if ((index + 1) % 32 == 0)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(10), ct);
            }

            var progress = 35 + (double)(index + 1) / Math.Max(chunks.Count, 1) * 64;
            await ReportProgressAsync(onProgress, Math.Min(progress, 99));
        }

        await ReportProgressAsync(onProgress, 100);
        Console.WriteLine($"[StructuredLibrarianV2Pipeline] COMPLETED indexing {Path.GetFileName(filePath)} via v2. Total chunks: {chunks.Count}");
        return flatContent;
    }

    private static string BuildStorageContent(string title, string year, StructuredChunk chunk)
    {
        var prefix = $"[Source: {title} | Year: {year}]";
        if (!string.IsNullOrWhiteSpace(chunk.SectionPath))
        {
            prefix += $" [Section: {chunk.SectionPath}]";
        }

        if (chunk.PageStart.HasValue)
        {
            prefix += chunk.PageEnd.HasValue && chunk.PageEnd != chunk.PageStart
                ? $" [Pages: {chunk.PageStart}-{chunk.PageEnd}]"
                : $" [Page: {chunk.PageStart}]";
        }

        var builder = new System.Text.StringBuilder(prefix);
        if (!string.IsNullOrWhiteSpace(chunk.Title))
        {
            builder.AppendLine();
            builder.Append("[Chunk: ");
            builder.Append(chunk.Title.Trim());
            builder.Append(']');
        }

        if (!string.IsNullOrWhiteSpace(chunk.Summary))
        {
            builder.AppendLine();
            builder.Append("[Summary: ");
            builder.Append(chunk.Summary.Trim());
            builder.Append(']');
        }

        builder.AppendLine();
        builder.Append(chunk.Text);
        return builder.ToString();
    }

    private static string BuildEmbeddingInput(string title, StructuredChunk chunk)
    {
        var builder = new System.Text.StringBuilder();
        if (!string.IsNullOrWhiteSpace(title))
        {
            builder.AppendLine(title.Trim());
        }

        if (!string.IsNullOrWhiteSpace(chunk.Title))
        {
            builder.AppendLine(chunk.Title.Trim());
        }

        if (!string.IsNullOrWhiteSpace(chunk.SectionPath))
        {
            builder.AppendLine(chunk.SectionPath.Trim());
        }

        if (!string.IsNullOrWhiteSpace(chunk.Summary))
        {
            builder.AppendLine(chunk.Summary.Trim());
        }

        if (chunk.Metadata?.TryGetValue("semantic_terms", out var semanticTerms) == true && !string.IsNullOrWhiteSpace(semanticTerms))
        {
            builder.AppendLine(semanticTerms.Trim());
        }

        if (chunk.PageStart.HasValue)
        {
            builder.Append("page ");
            builder.Append(chunk.PageStart.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (chunk.PageEnd.HasValue && chunk.PageEnd != chunk.PageStart)
            {
                builder.Append('-');
                builder.Append(chunk.PageEnd.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            builder.AppendLine();
        }

        builder.Append(chunk.Text);
        return builder.ToString();
    }

    private static Dictionary<string, string> BuildStructuredMetadata(
        string filePath,
        DocumentParseResult document,
        StructuredChunk chunk,
        string domain,
        string fileHash,
        string year,
        ChunkPlan plan,
        string parserVersion)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["title"] = document.Title,
            ["domain"] = domain,
            ["format"] = NormalizeSourceFormat(document.Format, filePath),
            ["source_layer"] = "local_library",
            ["source_format"] = NormalizeSourceFormat(document.Format, filePath),
            ["source_id"] = ResolveSourceId(document, filePath),
            ["display_title"] = string.IsNullOrWhiteSpace(document.Title) ? Path.GetFileName(filePath) : document.Title.Trim(),
            ["locator"] = BuildLocator(chunk),
            ["indexed_at_utc"] = DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            ["content_hash"] = fileHash,
            ["parser_name"] = ResolveParserName(parserVersion),
            ["source_freshness_class"] = ResolveSourceFreshnessClass(year, domain),
            ["allowed_claim_roles"] = ResolveAllowedClaimRoles(year, domain),
            ["chunk_index"] = chunk.ChunkIndex.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["file_hash"] = fileHash,
            ["published_year"] = year,
            ["document_id"] = document.DocumentId,
            ["source_path"] = filePath,
            ["section_path"] = chunk.SectionPath ?? string.Empty,
            ["page_start"] = chunk.PageStart?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            ["page_end"] = chunk.PageEnd?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            ["parent_id"] = chunk.ParentId ?? string.Empty,
            ["prev_chunk_id"] = chunk.PrevChunkId ?? string.Empty,
            ["next_chunk_id"] = chunk.NextChunkId ?? string.Empty,
            ["chunk_role"] = chunk.ChunkRole.ToString().ToLowerInvariant(),
            ["chunking_strategy"] = plan.Strategy.ToString(),
            ["parser_version"] = parserVersion,
            ["warning_count"] = (document.Warnings?.Count ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["structure_confidence"] = document.StructureConfidence.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["chunk_token_count"] = chunk.ChunkTokenCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["pipeline_version"] = plan.PipelineVersion,
            ["metadata_schema_version"] = plan.MetadataSchemaVersion,
            ["chunk_id"] = chunk.ChunkId,
            ["record_type"] = chunk.ChunkRole switch
            {
                ChunkRole.Parent => "parent",
                ChunkRole.Child => "child",
                _ => "standalone"
            }
        };

        if (!string.IsNullOrWhiteSpace(chunk.Title))
        {
            metadata["chunk_title"] = chunk.Title.Trim();
        }

        if (!string.IsNullOrWhiteSpace(chunk.Summary))
        {
            metadata["chunk_summary"] = chunk.Summary.Trim();
        }

        if (chunk.Metadata is not null)
        {
            foreach (var pair in chunk.Metadata)
            {
                if (!string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
                {
                    metadata[pair.Key] = pair.Value;
                }
            }
        }

        return metadata;
    }

    private static string NormalizeSourceFormat(DocumentFormatType format, string filePath)
    {
        if (format != DocumentFormatType.Unknown)
        {
            return format.ToString().ToLowerInvariant();
        }

        var extension = Path.GetExtension(filePath).TrimStart('.').Trim();
        return string.IsNullOrWhiteSpace(extension) ? "unknown" : extension.ToLowerInvariant();
    }

    private static string ResolveSourceId(DocumentParseResult document, string filePath)
    {
        if (document.Metadata?.TryGetValue("source_id", out var sourceId) == true &&
            !string.IsNullOrWhiteSpace(sourceId))
        {
            return sourceId.Trim();
        }

        return string.IsNullOrWhiteSpace(document.DocumentId)
            ? StructuredParserUtilities.CreateDocumentId(filePath)
            : document.DocumentId.Trim();
    }

    private static string ResolveParserName(string parserVersion)
    {
        var parts = parserVersion.Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 0 ? parserVersion : parts[0];
    }

    private static string BuildLocator(StructuredChunk chunk)
    {
        var parts = new List<string>();
        if (chunk.PageStart.HasValue)
        {
            var page = chunk.PageEnd.HasValue && chunk.PageEnd != chunk.PageStart
                ? $"pages:{chunk.PageStart}-{chunk.PageEnd}"
                : $"page:{chunk.PageStart}";
            parts.Add(page);
        }

        if (!string.IsNullOrWhiteSpace(chunk.SectionPath))
        {
            parts.Add($"section:{chunk.SectionPath.Trim()}");
        }

        parts.Add($"chunk:{chunk.ChunkIndex}");
        return string.Join(" | ", parts);
    }

    private static string ResolveSourceFreshnessClass(string year, string domain)
    {
        if (string.Equals(year, "unknown", StringComparison.OrdinalIgnoreCase))
        {
            return "unknown_date";
        }

        return KnowledgeCollectionNaming.IsHistoricalEncyclopediaSource(domain, domain)
            ? "historical"
            : "stable_reference";
    }

    private static string ResolveAllowedClaimRoles(string year, string domain)
    {
        var roles = new List<string>
        {
            "background",
            "definition",
            "methodology",
            "historical_context",
            "user_context"
        };

        if (!string.Equals(year, "unknown", StringComparison.OrdinalIgnoreCase) &&
            !KnowledgeCollectionNaming.IsHistoricalEncyclopediaSource(domain, domain))
        {
            roles.Add("stable_reference");
        }

        return string.Join(",", roles);
    }

    private static string DetectYear(string title, string content)
    {
        var titleMatch = System.Text.RegularExpressions.Regex.Match(title, @"\b(19|20)\d{2}\b");
        if (titleMatch.Success)
        {
            return titleMatch.Value;
        }

        var sample = content.Length > 2000 ? content[..2000] : content;
        var contentMatch = System.Text.RegularExpressions.Regex.Match(sample, @"\b(19|20)\d{2}\b");
        return contentMatch.Success ? contentMatch.Value : "unknown";
    }

    private static Task ReportProgressAsync(Func<double, Task>? onProgress, double value)
        => onProgress is null ? Task.CompletedTask : onProgress(value);

    private static int ResolveMaxChunksPerDocument(DocumentParseResult document, string domain, ChunkPlan plan, int observedChunkCount)
    {
        var isReferenceLikeDocument =
            KnowledgeCollectionNaming.IsEncyclopediaLikeDomain(domain) ||
            KnowledgeCollectionNaming.IsReferenceLikeSource(document.SourcePath, document.Title);

        if (!isReferenceLikeDocument)
        {
            if (observedChunkCount <= DefaultMaxChunksPerDocument)
            {
                return DefaultMaxChunksPerDocument;
            }

            var qualifiesLargeDocument =
                document.Pages.Count >= LargeDocumentMinPages ||
                document.Blocks.Count >= LargeDocumentMinBlocks ||
                observedChunkCount >= LargeDocumentMinObservedChunks;

            if (!qualifiesLargeDocument)
            {
                return DefaultMaxChunksPerDocument;
            }

            return Math.Min(Math.Max(DefaultMaxChunksPerDocument, observedChunkCount), LargeDocumentMaxChunksPerDocument);
        }

        if (observedChunkCount <= DefaultMaxChunksPerDocument)
        {
            return DefaultMaxChunksPerDocument;
        }

        var qualifiesLargeReference =
            document.Pages.Count >= LargeReferenceMinPages ||
            document.Blocks.Count >= LargeDocumentMinBlocks ||
            observedChunkCount >= LargeDocumentMinObservedChunks;

        if (!qualifiesLargeReference)
        {
            return DefaultMaxChunksPerDocument;
        }

        if (document.Format is not DocumentFormatType.Pdf and not DocumentFormatType.Djvu)
        {
            return Math.Min(Math.Max(DefaultMaxChunksPerDocument, observedChunkCount), LargeReferenceMaxChunksPerDocument);
        }

        var perPageBudget = plan.Strategy == ChunkStrategyType.TableAware ? 12 : 8;
        var adaptiveCap = Math.Max(DefaultMaxChunksPerDocument, document.Pages.Count * perPageBudget);
        return Math.Min(adaptiveCap, LargeReferenceMaxChunksPerDocument);
    }
}

