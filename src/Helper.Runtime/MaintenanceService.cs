using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Helper.Runtime.Core;

namespace Helper.Runtime.Infrastructure
{
    public class MaintenanceService : IMaintenanceService
    {
        private readonly IVectorStore _memory;
        private readonly AILink _ai;
        private readonly IHealthMonitor _health;
        private readonly IRecursiveTester _tester;
        private readonly ISyntheticLearningService _learner;
        private readonly IKnowledgePruner _pruner;
        private readonly bool _autonomousEvolutionAutostartEnabled;

        public MaintenanceService(IVectorStore memory, AILink ai, IHealthMonitor health, IRecursiveTester tester, ISyntheticLearningService learner, IKnowledgePruner pruner)
        {
            _memory = memory;
            _ai = ai;
            _health = health;
            _tester = tester;
            _learner = learner;
            _pruner = pruner;
            _autonomousEvolutionAutostartEnabled = ReadFlag("HELPER_ENABLE_AUTONOMOUS_EVOLUTION_AUTOSTART", false);
        }

        public async Task RunPrometheusLoopAsync(CancellationToken ct, Func<string, Task>? onThought = null)
        {
            if (!_autonomousEvolutionAutostartEnabled)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                return;
            }

            int boredom = 0;
            var distiller = new KnowledgeDistillerService(_memory, _ai);

            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(10), ct);
                boredom += 20;
                
                if (boredom >= 1000)
                {
                    if (onThought != null) await onThought("🚀 Initiating autonomous evolution cycle...");
                    
                    // --- CONTEXT PRUNING (Directive 3) ---
                    var prunedCount = await _pruner.PruneDeadKnowledgeAsync("knowledge_generic", 30, ct);
                    if (prunedCount > 0 && onThought != null) await onThought($"🧹 Maintenance: Pruned {prunedCount} redundant knowledge chunks.");

                    var health = await _health.DiagnoseAsync(ct);
                    if (!health.IsHealthy && onThought != null)
                    {
                        await onThought($"⚠️ Health alert: {health.ErrorRate:P}. Analyzing causes...");
                    }

                    // --- ADVANCED SELF-PLAY / SYNTHETIC DATA CYCLE ---
                    await _learner.StartEvolutionAsync();
                    if (onThought != null)
                    {
                        await onThought("🧪 Evolution cycle is active.");
                    }

                    await distiller.MaintenanceAsync(ct);
                    boredom = 0;
                }
            }
        }

        private static bool ReadFlag(string name, bool defaultValue)
        {
            var raw = Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return defaultValue;
            }

            if (bool.TryParse(raw, out var parsed))
            {
                return parsed;
            }

            return raw switch
            {
                "1" => true,
                "0" => false,
                _ => defaultValue
            };
        }

        public async Task ConsolidateMemoryAsync(CancellationToken ct = default)
        {
            var logPath = HelperWorkspacePathResolver.ResolveLogsPath("global_helper_log.txt");
            if (!File.Exists(logPath)) return;

            var rawLogs = await File.ReadAllTextAsync(logPath, ct);
            if (string.IsNullOrWhiteSpace(rawLogs) || rawLogs.Length < 100) return;

            var logEmbedding = await _ai.EmbedAsync(rawLogs.Substring(0, Math.Min(rawLogs.Length, 2000)), ct);
            await _memory.UpsertAsync(new KnowledgeChunk(
                Guid.NewGuid().ToString(),
                rawLogs,
                logEmbedding,
                new Dictionary<string, string> { { "type", "episodic_log" }, { "date", DateTime.Now.ToShortDateString() } }
            ), ct);

            var prompt = $@"Analyze chat logs and extract key semantic facts. Return each fact on a new line starting with '-'.
LOGS:
{rawLogs.Substring(Math.Max(0, rawLogs.Length - 5000))}";

            var facts = await _ai.AskAsync(prompt, ct);
            var factLines = facts.Split('\n').Where(l => l.Trim().StartsWith("-")).ToList();

            foreach (var fact in factLines)
            {
                var content = fact.Trim().TrimStart('-').Trim();
                if (string.IsNullOrEmpty(content)) continue;

                var embedding = await _ai.EmbedAsync(content, ct);
                await _memory.UpsertAsync(new KnowledgeChunk(
                    Guid.NewGuid().ToString(),
                    content,
                    embedding,
                    new Dictionary<string, string> { { "type", "semantic_fact" }, { "timestamp", DateTime.Now.ToString("O") } }
                ), ct);
            }
        }
    }
}

