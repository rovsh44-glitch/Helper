using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Helper.Runtime.Core;
using Helper.Runtime.Infrastructure;
using Helper.Runtime.Knowledge.Chunking;

namespace Helper.Runtime.Knowledge
{
    public class LibrarianAgent : ILibrarianAgent
    {
        private readonly IVectorStore _store;
        private readonly AILink _ai;
        private readonly List<IDocumentParser> _parsers;
        private readonly List<IStructuredDocumentParser> _structuredParsers;
        private readonly StructuredLibrarianV2Pipeline _v2Pipeline;
        private readonly IIndexingTelemetrySink _telemetry;
        private readonly KnowledgeDomainResolver _domainResolver;
        private readonly string _helperRoot;
        private readonly string _libraryDocsRoot;
        private static readonly int DefaultMaxChunksPerDocument = StructuredParserUtilities.ReadBoundedIntEnvironment("HELPER_INDEX_MAX_CHUNKS_PER_DOCUMENT_LEGACY", 1200, 128, 64000);
        private static readonly int LargeReferenceMaxChunksPerDocument = StructuredParserUtilities.ReadBoundedIntEnvironment("HELPER_INDEX_MAX_CHUNKS_PER_DOCUMENT_LARGE_REFERENCE", 24000, 1600, 120000);
        private static readonly int LegacyLargeReferenceMinChars = StructuredParserUtilities.ReadBoundedIntEnvironment("HELPER_INDEX_LARGE_REFERENCE_MIN_CHARS_LEGACY", 250000, 20000, 2000000);

        public LibrarianAgent(
            IVectorStore store,
            AILink ai,
            IEnumerable<IDocumentParser> parsers,
            IEnumerable<IStructuredDocumentParser> structuredParsers,
            StructuredLibrarianV2Pipeline v2Pipeline,
            IIndexingTelemetrySink telemetry,
            KnowledgeDomainResolver domainResolver)
        {
            _store = store;
            _ai = ai;
            _parsers = parsers.ToList();
            _structuredParsers = structuredParsers.ToList();
            _v2Pipeline = v2Pipeline;
            _telemetry = telemetry;
            _domainResolver = domainResolver;
            _helperRoot = HelperWorkspacePathResolver.ResolveHelperRoot();
            _libraryDocsRoot = HelperWorkspacePathResolver.ResolveLibraryDocsRoot(helperRoot: _helperRoot);
        }

        public async Task<string> IndexFileAsync(string filePath, Func<double, Task>? onProgress = null, CancellationToken ct = default)
        {
            filePath = HelperWorkspacePathResolver.CanonicalizeLibraryPath(filePath, helperRoot: _helperRoot);
            var fileName = Path.GetFileName(filePath);
            string? resolvedDomain = TryResolveDomainFromPath(filePath);
            var pipelineVersion = ReadPipelineVersion();
            _telemetry.Reset();
            _telemetry.Report(new IndexingTelemetry(pipelineVersion));
            
            // 1. CALCULATE DIGITAL FINGERPRINT (HASH)
            string fileHash = CalculateFileHash(filePath);
            
            // 2. CHECK IF HASH ALREADY EXISTS IN DB
            if (await IsAlreadyIndexedByHashAsync(fileHash, pipelineVersion, ct))
            {
                Console.WriteLine($"[Librarian] SKIPPING: {fileName} (Content already indexed via hash: {fileHash})");
                if (onProgress != null) await onProgress(100);
                return $"[Skipped: {fileName}]";
            }

            var ext = Path.GetExtension(filePath).ToLower();
            var structuredParser = pipelineVersion == "v2"
                ? _structuredParsers.FirstOrDefault(parser => parser.CanParse(ext))
                : null;

            if (structuredParser is not null)
            {
                return await _v2Pipeline.IndexAsync(filePath, structuredParser, resolvedDomain, fileHash, onProgress, ct);
            }

            var parser = _parsers.FirstOrDefault(p => p.CanParse(ext));

            if (parser == null)
            {
                var binaryExts = new[] { ".exe", ".dll", ".zip", ".bin" };
                if (binaryExts.Contains(ext)) return $"[Skipped binary file: {fileName}]";

                var content = await File.ReadAllTextAsync(filePath, ct);
                resolvedDomain = await ResolveDomainForFileAsync(filePath, content, resolvedDomain, ct);
                await IndexContentAsync(fileName, content, resolvedDomain, filePath, ct, fileHash, pipelineVersion);
                if (onProgress != null) await onProgress(100);
                return content;
            }

            var resultCollector = new StringBuilder();
            int current = 0;

            await parser.ParseStreamingAsync(filePath, async chunk => {
                current++;
                if (current % 10 == 0) Console.WriteLine($"[Librarian] Indexed {current} chunks of {fileName}...");

                resolvedDomain = await ResolveDomainForFileAsync(filePath, chunk, resolvedDomain, ct);
                
                await IndexContentAsync(fileName, chunk, resolvedDomain, filePath, ct, fileHash, pipelineVersion);
                resultCollector.AppendLine(chunk);
                
                if (onProgress != null) 
                {
                    double pct = Math.Min(99, (double)current / (current + 20) * 100); 
                    await onProgress(pct);
                }

                await Task.Yield();
            }, ct);

            if (onProgress != null) await onProgress(100);
            Console.WriteLine($"[Librarian] COMPLETED indexing {fileName}. Total chunks: {current}");
            return resultCollector.ToString();
        }

        public async Task CleanupFileArtifactsAsync(string filePath, CancellationToken ct = default)
        {
            filePath = HelperWorkspacePathResolver.CanonicalizeLibraryPath(filePath, helperRoot: _helperRoot);
            if (!File.Exists(filePath))
            {
                return;
            }

            string fileHash = CalculateFileHash(filePath);
            foreach (var collection in BuildCandidateCollections(ReadPipelineVersion()))
            {
                await _store.DeleteByMetadataAsync("file_hash", fileHash, collection, ct);
            }
        }

        private string CalculateFileHash(string path)
        {
            using var sha = System.Security.Cryptography.SHA256.Create();
            using var stream = File.OpenRead(path);
            var hash = sha.ComputeHash(stream);
            return BitConverter.ToString(hash).Replace("-", "").ToLower();
        }

        private async Task<bool> IsAlreadyIndexedByHashAsync(string hash, string pipelineVersion, CancellationToken ct)
        {
            var collections = await GetExistingCandidateCollectionsAsync(pipelineVersion, ct);
            
            foreach (var col in collections)
            {
                var existing = await _store.SearchMetadataAsync("file_hash", hash, col, ct);
                if (existing.Any()) return true;
            }
            return false;
        }

        private List<string> BuildCandidateCollections(string pipelineVersion)
        {
            return KnowledgeDomainCatalog.AllowedDomains
                .Select(domain => KnowledgeCollectionNaming.BuildCollectionName(domain, pipelineVersion))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private async Task<List<string>> GetExistingCandidateCollectionsAsync(string pipelineVersion, CancellationToken ct)
        {
            var candidates = BuildCandidateCollections(pipelineVersion);
            if (_store is not IStructuredVectorStore structuredStore)
            {
                return candidates;
            }

            var existingCollections = await structuredStore.ListCollectionsAsync("knowledge_", ct);
            if (existingCollections.Count == 0)
            {
                return new List<string>();
            }

            var existingSet = new HashSet<string>(existingCollections, StringComparer.OrdinalIgnoreCase);
            return candidates
                .Where(existingSet.Contains)
                .ToList();
        }

        private async Task<string?> ResolveDomainForFileAsync(string filePath, string contentSample, string? currentDomain, CancellationToken ct)
        {
            if (!string.IsNullOrWhiteSpace(currentDomain))
            {
                return KnowledgeCollectionNaming.ResolveDomain(currentDomain, filePath, Path.GetFileName(filePath));
            }

            var resolvedDomain = await _domainResolver.ResolveAsync(contentSample, currentDomain, ct);
            return KnowledgeCollectionNaming.ResolveDomain(resolvedDomain, filePath, Path.GetFileName(filePath));
        }

        private async Task IndexContentAsync(string title, string content, string? manualDomain, string? sourcePath, CancellationToken ct, string? fileHash = null, string pipelineVersion = "v1")
        {
            string rawDomain = manualDomain ?? await _domainResolver.DetectAsync(content, ct);
            string domain = KnowledgeCollectionNaming.ResolveDomain(rawDomain, sourcePath, title);
            string collection = KnowledgeCollectionNaming.BuildCollectionName(domain, pipelineVersion);
            
            string year = DetectYear(title, content);

            var chunkSize = GetAdaptiveChunkSize(content.Length);
            var chunks = ChunkText(content, chunkSize);
            var maxChunksPerDocument = ResolveLegacyMaxChunksPerDocument(domain, content.Length);
            if (chunks.Count > maxChunksPerDocument)
            {
                chunks = chunks.Take(maxChunksPerDocument).ToList();
                Console.WriteLine($"[Librarian] Backpressure applied for {title}: truncated to {maxChunksPerDocument} chunks.");
            }

            int chunkIdx = 1;
            foreach (var chunkText in chunks)
            {
                string epochAwareContent = $"[Source: {title} | Year: {year}]\n{chunkText}";
                var embedding = await _ai.EmbedAsync(epochAwareContent, ct);
                var chunk = new KnowledgeChunk(
                    Id: Guid.NewGuid().ToString(),
                    Content: epochAwareContent,
                    Embedding: embedding,
                    Metadata: new Dictionary<string, string>
                    {
                        { "title", title },
                        { "domain", domain },
                        { "format", Path.GetExtension(title) },
                        { "chunk_index", chunkIdx.ToString() },
                        { "file_hash", fileHash ?? "" },
                        { "published_year", year },
                        { "source_path", sourcePath ?? string.Empty },
                        { "pipeline_version", pipelineVersion },
                        { "chunking_strategy", "legacy_substring" },
                        { "metadata_schema_version", pipelineVersion == "v2" ? "v2_fallback" : "v1" },
                        { "chunk_role", "standalone" },
                        { "record_type", "legacy" }
                    },
                    Collection: collection
                );
                await _store.UpsertAsync(chunk, ct);
                if (chunkIdx % 64 == 0)
                {
                    // Adaptive throttle to avoid overload on embedding/vector backends.
                    await Task.Delay(TimeSpan.FromMilliseconds(10), ct);
                }
                chunkIdx++;
            }
        }

        private string DetectYear(string title, string content)
        {
            var match = System.Text.RegularExpressions.Regex.Match(title, @"\b(19|20)\d{2}\b");
            if (match.Success) return match.Value;
            
            var sample = content.Length > 2000 ? content.Substring(0, 2000) : content;
            var contentMatch = System.Text.RegularExpressions.Regex.Match(sample, @"\b(19|20)\d{2}\b");
            if (contentMatch.Success) return contentMatch.Value;
            
            return "unknown";
        }

        private string NormalizeDomain(string domain)
        {
            return _domainResolver.Normalize(domain);
        }

        private static int ResolveLegacyMaxChunksPerDocument(string domain, int contentLength)
        {
            if (!KnowledgeCollectionNaming.IsEncyclopediaLikeDomain(domain))
            {
                return DefaultMaxChunksPerDocument;
            }

            return contentLength >= LegacyLargeReferenceMinChars
                ? LargeReferenceMaxChunksPerDocument
                : DefaultMaxChunksPerDocument;
        }

        private async Task<string> DetectDomainAsync(string content, CancellationToken ct)
        {
            return await _domainResolver.DetectAsync(content, ct);
        }

        private string? TryResolveDomainFromPath(string filePath)
        {
            if (!HelperWorkspacePathResolver.IsPathUnderRoot(filePath, _libraryDocsRoot))
            {
                return null;
            }

            var relativePath = Path.GetRelativePath(_libraryDocsRoot, filePath);
            var relativeDirectory = Path.GetDirectoryName(relativePath);
            if (string.IsNullOrWhiteSpace(relativeDirectory))
            {
                return null;
            }

            var firstSegment = relativeDirectory
                .Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault();

            return string.IsNullOrWhiteSpace(firstSegment)
                ? null
                : KnowledgeCollectionNaming.ResolveDomain(firstSegment, filePath, Path.GetFileName(filePath));
        }

        private List<string> ChunkText(string text, int size)
        {
            var result = new List<string>();
            for (int i = 0; i < text.Length; i += size) { result.Add(text.Substring(i, Math.Min(size, text.Length - i))); }
            return result;
        }

        private static int GetAdaptiveChunkSize(int contentLength)
        {
            if (contentLength > 120_000) return 800;
            if (contentLength > 40_000) return 1000;
            if (contentLength > 10_000) return 1200;
            return 1500;
        }

        private static string ReadPipelineVersion()
        {
            var configured = Environment.GetEnvironmentVariable("HELPER_INDEX_PIPELINE_VERSION");
            return KnowledgeCollectionNaming.NormalizePipelineVersion(configured ?? "v2");
        }
    }
}

