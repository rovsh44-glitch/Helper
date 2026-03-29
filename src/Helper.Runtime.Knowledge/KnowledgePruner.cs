using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Helper.Runtime.Core;

namespace Helper.Runtime.Knowledge
{
    public class KnowledgePruner : IKnowledgePruner
    {
        private readonly IVectorStore _store;

        public KnowledgePruner(IVectorStore store)
        {
            _store = store;
        }

        public async Task<int> PruneDeadKnowledgeAsync(string collection, int thresholdDays = 30, CancellationToken ct = default)
        {
            // Logic: Scan collection for chunks that haven't been 'accessed' or are marked as low importance.
            // In current QdrantStore, we don't have 'last_accessed', so we simulate pruning 
            // by removing chunks from books that were indexed but never utilized in recent successes.
            
            // For now, we implement a safe version: remove duplicated content that might have slipped through.
            var chunks = await _store.ScrollMetadataAsync(collection, 1000, null, ct);
            var seen = new HashSet<string>();
            var idsToDelete = new List<string>();

            foreach (var chunk in chunks)
            {
                if (seen.Contains(chunk.Content))
                {
                    idsToDelete.Add(chunk.Id);
                }
                else
                {
                    seen.Add(chunk.Content);
                }
            }

            if (idsToDelete.Any())
            {
                await _store.DeletePointsAsync(idsToDelete, collection, ct);
            }

            return idsToDelete.Count;
        }
    }
}

