using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Helper.Runtime.Core;

namespace Helper.Runtime.Infrastructure
{
    public class KnowledgeDistillerService
    {
        private readonly IVectorStore _memory;
        private readonly AILink _ai;
        private const int MaxPointsPerCollection = 10000;

        public KnowledgeDistillerService(IVectorStore memory, AILink ai)
        {
            _memory = memory;
            _ai = ai;
        }

        public async Task MaintenanceAsync(CancellationToken ct = default)
        {
            // 1. Check episodic memory size
            var episodicMemories = await _memory.SearchAsync(new float[768], HelperKnowledgeCollections.CanonicalEpisodic, limit: 100, ct: ct);
            
            if (episodicMemories.Count > 50) 
            {
                Console.WriteLine("[Distiller] Memory pressure detected. Compressing episodic logs...");
                
                // Compress old logs into semantic facts
                var oldLogs = episodicMemories.Take(10);
                foreach (var log in oldLogs)
                {
                    var summaryPrompt = $@"Distill this raw log into 3 core technical facts. Respond only with facts starting with '-':
{log.Content}";
                    var summary = await _ai.AskAsync(summaryPrompt, ct);
                    
                    var embedding = await _ai.EmbedAsync(summary, ct);
                    await _memory.UpsertAsync(new KnowledgeChunk(
                        Guid.NewGuid().ToString(),
                        $"[Distilled from {DateTime.Now.ToShortDateString()}] {summary}",
                        embedding,
                        new System.Collections.Generic.Dictionary<string, string> { { "source", "distiller" } },
                        HelperKnowledgeCollections.CanonicalSemantic
                    ), ct);

                    // Note: In a real Qdrant we would call DeletePoint here. 
                    // For now we assume we just move/add and the limit is monitored.
                }
            }
        }
    }
}

