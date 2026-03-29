using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Helper.Runtime.Core;

namespace Helper.Runtime.Infrastructure
{
    public interface ISyntheticTaskRunner
    {
        Task RunAsync(CancellationToken ct, Func<string, Task>? onThought = null);
    }

    public sealed class SyntheticTaskRunner : ISyntheticTaskRunner
    {
        private readonly AILink _ai;
        private readonly IGraphOrchestrator _graph;
        private readonly ILearningPathPolicy _pathPolicy;

        public SyntheticTaskRunner(AILink ai, IGraphOrchestrator graph, ILearningPathPolicy pathPolicy)
        {
            _ai = ai;
            _graph = graph;
            _pathPolicy = pathPolicy;
        }

        public async Task RunAsync(CancellationToken ct, Func<string, Task>? onThought = null)
        {
            if (!_pathPolicy.ActiveLearningGenerationEnabled)
            {
                if (onThought != null)
                {
                    await onThought("⏸️ Active learning project generation is disabled by HELPER_ENABLE_ACTIVE_LEARNING_GENERATION=false.");
                }

                return;
            }

            var taskPrompt = "ACT AS A MASTER INNOVATOR. Invent a complex C#/.NET challenge. OUTPUT ONLY THE TASK.";
            var syntheticTask = await _ai.AskAsync(taskPrompt, ct, _ai.GetBestModel("reasoning"));

            if (onThought != null)
            {
                await onThought($"🧪 Active Challenge: {syntheticTask}");
            }

            Directory.CreateDirectory(_pathPolicy.ActiveLearningOutputPath);
            await _graph.ExecuteGraphAsync(syntheticTask, _pathPolicy.ActiveLearningOutputPath, null, ct);
        }
    }
}

