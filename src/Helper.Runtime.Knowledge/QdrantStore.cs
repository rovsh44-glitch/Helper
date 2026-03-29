using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using Helper.Runtime.Core;
using GrpcValue = Qdrant.Client.Grpc.Value;

namespace Helper.Runtime.Knowledge
{
    public class QdrantStore : IVectorStore, IStructuredVectorStore
    {
        private readonly QdrantClient _client;
        private const int DefaultVectorDimensions = 768;

        public QdrantStore(string host = "localhost", int port = 6334)
        {
            _client = new QdrantClient(host, port);
        }

        public async Task UpsertAsync(KnowledgeChunk chunk, CancellationToken ct = default)
        {
            var collection = HelperKnowledgeCollections.NormalizeWriteCollection(chunk.Collection);
            await EnsureCollectionExistsAsync(collection, 768, ct);

            var point = new PointStruct
            {
                Id = (PointId)NormalizeGuid(chunk.Id),
                Vectors = chunk.Embedding
            };

            foreach (var meta in chunk.Metadata)
            {
                point.Payload.Add(meta.Key, meta.Value);
            }
            point.Payload.Add("content", chunk.Content);

            await _client.UpsertAsync(collection, new[] { point }, cancellationToken: ct);
        }

        public async Task<List<KnowledgeChunk>> SearchAsync(float[] queryEmbedding, string collection = HelperKnowledgeCollections.CanonicalDefault, int limit = 5, CancellationToken ct = default)
        {
            try
            {
                return await SearchAcrossCandidateCollectionsAsync(
                    collection,
                    limit,
                    candidate => SearchInternalAsync(queryEmbedding, candidate, limit, ct),
                    nameof(SearchAsync));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[QdrantStore] SearchAsync error: {ex.Message}");
                return new List<KnowledgeChunk>();
            }
        }

        public async Task<List<KnowledgeChunk>> SearchMetadataAsync(string key, string value, string collection, CancellationToken ct = default)
        {
            try
            {
                return await SearchAcrossCandidateCollectionsAsync(
                    collection,
                    1,
                    candidate => SearchMetadataInternalAsync(key, value, candidate, ct),
                    nameof(SearchMetadataAsync));
            }
            catch (Exception ex) when (IsCancellationException(ex, ct))
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"SearchMetadataAsync failed for collection '{collection}': {ex.Message}", ex);
            }
        }

        public async Task<List<KnowledgeChunk>> ScrollMetadataAsync(string collection, int limit = 100, string? offset = null, CancellationToken ct = default)
        {
            try
            {
                return await SearchAcrossCandidateCollectionsAsync(
                    collection,
                    limit,
                    candidate => ScrollMetadataInternalAsync(candidate, limit, ct),
                    nameof(ScrollMetadataAsync));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[QdrantStore] ScrollMetadataAsync error: {ex.Message}");
                return new List<KnowledgeChunk>();
            }
        }

        public async Task DeletePointsAsync(List<string> ids, string collection, CancellationToken ct = default)
        {
            var pointIds = ids.Select(id => (PointId)NormalizeGuid(id)).ToList();
            foreach (var candidate in HelperKnowledgeCollections.ExpandReadCandidates(collection))
            {
                try
                {
                    await _client.DeleteAsync(candidate, pointIds, cancellationToken: ct);
                }
                catch (Exception ex)
                {
                    if (!TryHandleMissingCollection(ex, candidate, nameof(DeletePointsAsync)))
                    {
                        Console.WriteLine($"[QdrantStore] DeletePointsAsync error: {ex.Message}");
                    }
                }
            }
        }

        public async Task DeleteByMetadataAsync(string key, string value, string collection, CancellationToken ct = default)
        {
            foreach (var candidate in HelperKnowledgeCollections.ExpandReadCandidates(collection))
            {
                try
                {
                    await _client.DeleteAsync(candidate, BuildMatchFilter(key, value), cancellationToken: ct);
                }
                catch (Exception ex)
                {
                    if (TryHandleMissingCollection(ex, candidate, nameof(DeleteByMetadataAsync)))
                    {
                        continue;
                    }

                    Console.WriteLine($"[QdrantStore] DeleteByMetadataAsync error: {ex.Message}");
                }
            }
        }

        public async Task DeleteCollectionAsync(string collection, CancellationToken ct = default)
        {
            try { await _client.DeleteCollectionAsync(collection, cancellationToken: ct); }
            catch (Exception ex) { Console.WriteLine($"[QdrantStore] DeleteCollectionAsync error: {ex.Message}"); }
        }

        public async Task<IReadOnlyList<string>> ListCollectionsAsync(string? prefix = null, CancellationToken ct = default)
        {
            try
            {
                var collections = await _client.ListCollectionsAsync(ct);
                if (string.IsNullOrWhiteSpace(prefix))
                {
                    return collections.OrderBy(static name => name, StringComparer.OrdinalIgnoreCase).ToList();
                }

                return collections
                    .Where(name => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[QdrantStore] ListCollectionsAsync error: {ex.Message}");
                return Array.Empty<string>();
            }
        }

        public async Task<int?> GetCollectionPointCountAsync(string collection, CancellationToken ct = default)
        {
            int total = 0;
            var anyMatched = false;

            try
            {
                foreach (var candidate in HelperKnowledgeCollections.ExpandReadCandidates(collection))
                {
                    try
                    {
                        var info = await _client.GetCollectionInfoAsync(candidate, ct);
                        if (info is null)
                        {
                            continue;
                        }

                        if (info.HasPointsCount)
                        {
                            total += checked((int)info.PointsCount);
                            anyMatched = true;
                            continue;
                        }

                        if (info.HasIndexedVectorsCount)
                        {
                            total += checked((int)info.IndexedVectorsCount);
                            anyMatched = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        if (!TryHandleMissingCollection(ex, candidate, nameof(GetCollectionPointCountAsync)))
                        {
                            Console.WriteLine($"[QdrantStore] GetCollectionPointCountAsync error: {ex.Message}");
                        }
                    }
                }

                return anyMatched ? total : null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[QdrantStore] GetCollectionPointCountAsync error: {ex.Message}");
                return null;
            }
        }

        public async Task<List<KnowledgeChunk>> SearchByMetadataAsync(IReadOnlyDictionary<string, string> filters, string collection, int limit = 10, CancellationToken ct = default)
        {
            try
            {
                return await SearchAcrossCandidateCollectionsAsync(
                    collection,
                    Math.Max(limit, 1),
                    candidate => SearchByMetadataInternalAsync(filters, candidate, Math.Max(limit, 1), ct),
                    nameof(SearchByMetadataAsync));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[QdrantStore] SearchByMetadataAsync error: {ex.Message}");
                return new List<KnowledgeChunk>();
            }
        }

        public async Task<KnowledgeChunk?> GetChunkByChunkIdAsync(string collection, string chunkId, CancellationToken ct = default)
        {
            var matches = await SearchByMetadataAsync(
                new Dictionary<string, string> { ["chunk_id"] = chunkId },
                collection,
                1,
                ct);
            return matches.FirstOrDefault();
        }

        public async Task<IReadOnlyList<KnowledgeChunk>> GetChunksByChunkIdsAsync(string collection, IEnumerable<string> chunkIds, CancellationToken ct = default)
        {
            var ids = chunkIds
                .Where(static id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (ids.Count == 0)
            {
                return Array.Empty<KnowledgeChunk>();
            }

            var chunks = new List<KnowledgeChunk>(ids.Count);
            foreach (var chunkId in ids)
            {
                var chunk = await GetChunkByChunkIdAsync(collection, chunkId, ct);
                if (chunk is not null)
                {
                    chunks.Add(chunk);
                }
            }

            return chunks;
        }

        public async Task<IReadOnlyList<KnowledgeChunk>> GetDocumentLocalGroupAsync(string collection, string documentId, string? sectionPath = null, int? pageStart = null, int limit = 8, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(documentId))
            {
                return Array.Empty<KnowledgeChunk>();
            }

            var candidateLimit = Math.Max(limit * 8, 48);
            var matches = await SearchByMetadataAsync(
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["document_id"] = documentId
                },
                collection,
                candidateLimit,
                ct);

            return matches
                .Select(chunk => new
                {
                    Chunk = chunk,
                    SameSection = !string.IsNullOrWhiteSpace(sectionPath) &&
                                  string.Equals(
                                      chunk.Metadata.GetValueOrDefault("section_path"),
                                      sectionPath,
                                      StringComparison.OrdinalIgnoreCase),
                    PageDistance = GetPageDistance(chunk.Metadata, pageStart),
                    ChunkIndex = ParseChunkIndex(chunk.Metadata.GetValueOrDefault("chunk_index")),
                    SectionPath = chunk.Metadata.GetValueOrDefault("section_path") ?? string.Empty
                })
                .OrderByDescending(static entry => entry.SameSection)
                .ThenBy(static entry => entry.PageDistance)
                .ThenBy(static entry => entry.ChunkIndex)
                .ThenBy(static entry => entry.SectionPath, StringComparer.OrdinalIgnoreCase)
                .Take(limit)
                .Select(static entry => entry.Chunk)
                .ToList();
        }

        public async Task EnsureCollectionExistsAsync(string name, int dimensions, CancellationToken ct = default)
        {
            var canonicalName = HelperKnowledgeCollections.NormalizeWriteCollection(name);
            try 
            {
                var collections = await _client.ListCollectionsAsync(ct);
                if (!collections.Contains(canonicalName))
                {
                    await _client.CreateCollectionAsync(canonicalName, new VectorParams { Size = (ulong)dimensions, Distance = Distance.Cosine }, cancellationToken: ct);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[QdrantStore] EnsureCollectionExistsAsync error: {ex.Message}");
            }
        }

        private async Task<List<KnowledgeChunk>> SearchAcrossCandidateCollectionsAsync(
            string collection,
            int limit,
            Func<string, Task<List<KnowledgeChunk>>> executor,
            string operation)
        {
            var merged = new List<KnowledgeChunk>();
            var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var candidate in HelperKnowledgeCollections.ExpandReadCandidates(collection))
            {
                try
                {
                    var results = await executor(candidate);
                    foreach (var chunk in results)
                    {
                        if (seenIds.Add(chunk.Id))
                        {
                            merged.Add(chunk);
                        }
                    }
                }
                catch (Exception ex) when (TryHandleMissingCollection(ex, candidate, operation))
                {
                    continue;
                }
            }

            return merged
                .OrderByDescending(GetVectorScore)
                .Take(Math.Max(limit, 1))
                .ToList();
        }

        private async Task<List<KnowledgeChunk>> SearchInternalAsync(float[] queryEmbedding, string collection, int limit, CancellationToken ct)
        {
            var results = await _client.SearchAsync(
                collection,
                queryEmbedding,
                limit: (uint)limit,
                cancellationToken: ct);

            return results.Select(r => ToKnowledgeChunk(r, collection)).ToList();
        }

        private async Task<List<KnowledgeChunk>> SearchMetadataInternalAsync(string key, string value, string collection, CancellationToken ct)
            => await SearchByMetadataInternalAsync(
                new Dictionary<string, string> { [key] = value },
                collection,
                1,
                ct);

        private async Task<List<KnowledgeChunk>> SearchByMetadataInternalAsync(IReadOnlyDictionary<string, string> filters, string collection, int limit, CancellationToken ct)
        {
            // Metadata existence check via a constrained vector search.
            var filter = BuildMatchFilter(filters);
            var dummyVector = new float[DefaultVectorDimensions];

            var results = await _client.SearchAsync(
                collection,
                dummyVector,
                filter: filter,
                limit: (uint)Math.Max(limit, 1),
                cancellationToken: ct);

            return results.Select(r => ToKnowledgeChunk(r, collection)).ToList();
        }

        private async Task<List<KnowledgeChunk>> ScrollMetadataInternalAsync(string collection, int limit, CancellationToken ct)
        {
            var scrollResult = await _client.ScrollAsync(
                collection,
                limit: (uint)limit,
                cancellationToken: ct);

            return scrollResult.Result.Select(r => new KnowledgeChunk(
                Id: r.Id.ToString(),
                Content: ReadContent(r.Payload),
                Embedding: Array.Empty<float>(),
                Metadata: ConvertMetadata(r.Payload, collection),
                Collection: collection
            )).ToList();
        }

        private static bool TryHandleMissingCollection(Exception ex, string collection, string operation)
        {
            if (!IsMissingCollectionError(ex) || string.IsNullOrWhiteSpace(collection))
            {
                return false;
            }

            Console.WriteLine($"[QdrantStore] {operation}: missing collection '{collection}', returning empty result.");
            return true;
        }

        private static Filter BuildMatchFilter(string key, string value)
        {
            return new Filter
            {
                Must =
                {
                    Conditions.MatchKeyword(key, value)
                }
            };
        }

        private static Filter BuildMatchFilter(IReadOnlyDictionary<string, string> filters)
        {
            var filter = new Filter();
            foreach (var pair in filters)
            {
                filter.Must.Add(Conditions.MatchKeyword(pair.Key, pair.Value));
            }

            return filter;
        }

        private static bool IsMissingCollectionError(Exception ex)
        {
            var message = ex.Message ?? string.Empty;
            return message.Contains("collection", StringComparison.OrdinalIgnoreCase) &&
                   (message.Contains("not found", StringComparison.OrdinalIgnoreCase) ||
                    message.Contains("doesn't exist", StringComparison.OrdinalIgnoreCase) ||
                    message.Contains("does not exist", StringComparison.OrdinalIgnoreCase));
        }

        private static Guid NormalizeGuid(string? value)
        {
            if (!string.IsNullOrWhiteSpace(value) && Guid.TryParse(value, out var parsed))
            {
                return parsed;
            }

            var source = string.IsNullOrWhiteSpace(value) ? Guid.NewGuid().ToString("N") : value.Trim();
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(source));
            return new Guid(hash.AsSpan(0, 16));
        }

        private static string ReadContent(IDictionary<string, GrpcValue> payload)
        {
            return payload.TryGetValue("content", out var content) ? ConvertValue(content) : string.Empty;
        }

        private static Dictionary<string, string> ConvertMetadata(IDictionary<string, GrpcValue> payload, string? collection = null, double? vectorScore = null)
        {
            var metadata = payload.ToDictionary(item => item.Key, item => ConvertValue(item.Value));
            if (!string.IsNullOrWhiteSpace(collection))
            {
                metadata["collection"] = collection;
            }

            if (vectorScore.HasValue)
            {
                metadata["vector_score"] = vectorScore.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            return metadata;
        }

        private static string ConvertValue(GrpcValue value)
        {
            return value.KindCase switch
            {
                GrpcValue.KindOneofCase.StringValue => value.StringValue,
                GrpcValue.KindOneofCase.IntegerValue => value.IntegerValue.ToString(System.Globalization.CultureInfo.InvariantCulture),
                GrpcValue.KindOneofCase.DoubleValue => value.DoubleValue.ToString(System.Globalization.CultureInfo.InvariantCulture),
                GrpcValue.KindOneofCase.BoolValue => value.BoolValue ? "true" : "false",
                GrpcValue.KindOneofCase.StructValue => value.StructValue.ToString(),
                GrpcValue.KindOneofCase.ListValue => string.Join(",", value.ListValue.Values.Select(ConvertValue)),
                _ => value.ToString()
            };
        }

        private static KnowledgeChunk ToKnowledgeChunk(ScoredPoint point, string collection)
        {
            return new KnowledgeChunk(
                Id: point.Id.ToString(),
                Content: ReadContent(point.Payload),
                Embedding: Array.Empty<float>(),
                Metadata: ConvertMetadata(point.Payload, collection, point.Score),
                Collection: collection);
        }

        private static double GetVectorScore(KnowledgeChunk chunk)
        {
            if (chunk.Metadata.TryGetValue("vector_score", out var raw) &&
                double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var score))
            {
                return score;
            }

            return double.MinValue;
        }

        private static int ParseChunkIndex(string? raw)
        {
            return int.TryParse(raw, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var value)
                ? value
                : int.MaxValue;
        }

        private static int GetPageDistance(IReadOnlyDictionary<string, string> metadata, int? currentPage)
        {
            if (!currentPage.HasValue)
            {
                return int.MaxValue / 4;
            }

            var start = ParseOptionalInt(metadata.GetValueOrDefault("page_start"));
            var end = ParseOptionalInt(metadata.GetValueOrDefault("page_end")) ?? start;
            if (!start.HasValue && !end.HasValue)
            {
                return int.MaxValue / 4;
            }

            var effectiveStart = start ?? end!.Value;
            var effectiveEnd = end ?? start!.Value;
            if (currentPage.Value >= effectiveStart && currentPage.Value <= effectiveEnd)
            {
                return 0;
            }

            return Math.Min(
                Math.Abs(effectiveStart - currentPage.Value),
                Math.Abs(effectiveEnd - currentPage.Value));
        }

        private static int? ParseOptionalInt(string? raw)
        {
            return int.TryParse(raw, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var value)
                ? value
                : null;
        }

        private static bool IsCancellationException(Exception ex, CancellationToken ct)
        {
            if (ct.IsCancellationRequested || ex is OperationCanceledException)
            {
                return true;
            }

            if (ex is RpcException rpcEx && rpcEx.StatusCode == StatusCode.Cancelled)
            {
                return true;
            }

            return ex.InnerException is not null && IsCancellationException(ex.InnerException, ct);
        }
    }
}

