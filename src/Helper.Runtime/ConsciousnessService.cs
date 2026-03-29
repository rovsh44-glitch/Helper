using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Helper.Runtime.Core;

namespace Helper.Runtime.Infrastructure
{
    public record ThoughtStreamEntry(string Thought, DateTime Timestamp, Dictionary<string, string>? Metadata = null);

    public interface IConsciousnessService
    {
        Task RecordThoughtAsync(string thought, Dictionary<string, string>? metadata = null, CancellationToken ct = default);
        Task<List<ThoughtStreamEntry>> RecallThoughtsAsync(string query, int limit = 5, CancellationToken ct = default);
    }

    public class ConsciousnessService : IConsciousnessService
    {
        private readonly IVectorStore _memory;
        private readonly AILink _ai;
        private const string CollectionName = "consciousness_stream";

        public ConsciousnessService(IVectorStore memory, AILink ai)
        {
            _memory = memory;
            _ai = ai;
        }

        public async Task RecordThoughtAsync(string thought, Dictionary<string, string>? metadata = null, CancellationToken ct = default)
        {
            var embedding = await _ai.EmbedAsync(thought, ct);
            var chunk = new KnowledgeChunk(
                Id: Guid.NewGuid().ToString(),
                Content: thought,
                Embedding: embedding,
                Metadata: metadata ?? new Dictionary<string, string>(),
                Collection: CollectionName
            );
            
            chunk.Metadata["timestamp"] = DateTime.UtcNow.ToString("O");
            await _memory.UpsertAsync(chunk, ct);
        }

        public async Task<List<ThoughtStreamEntry>> RecallThoughtsAsync(string query, int limit = 5, CancellationToken ct = default)
        {
            var embedding = await _ai.EmbedAsync(query, ct);
            var results = await _memory.SearchAsync(embedding, CollectionName, limit, ct);

            return results.Select(r => new ThoughtStreamEntry(
                r.Content,
                DateTime.Parse(r.Metadata.GetValueOrDefault("timestamp", DateTime.UtcNow.ToString("O"))),
                r.Metadata
            )).OrderByDescending(t => t.Timestamp).ToList();
        }
    }
}

