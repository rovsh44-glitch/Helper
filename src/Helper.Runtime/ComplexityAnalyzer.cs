using System;
using System.Threading;
using System.Threading.Tasks;
using Helper.Runtime.Core;

namespace Helper.Runtime.Infrastructure
{
    public class ComplexityAnalyzer : IComplexityAnalyzer
    {
        private readonly AILink _ai;
        private readonly IHealthMonitor _health;

        public ComplexityAnalyzer(AILink ai, IHealthMonitor health)
        {
            _ai = ai;
            _health = health;
        }

        public async Task<TaskComplexity> AnalyzeComplexityAsync(string taskDescription, CancellationToken ct = default)
        {
            var status = await _health.DiagnoseAsync(ct);
            double vram = status.CurrentVramAvailableGb;

            // 1. Fast heuristics (Visual)
            if (taskDescription.Contains(".xaml") || taskDescription.Contains("UI") || taskDescription.Contains("Visual") || taskDescription.Contains("Layout"))
            {
                if (taskDescription.Contains("Check") || taskDescription.Contains("Verify") || taskDescription.Contains("Inspect"))
                    return TaskComplexity.Visual;
            }

            // 2. AI Analysis
            var fastModel = _ai.GetBestModel("fast");
            var prompt = $@"CLASSIFY TASK COMPLEXITY: {taskDescription}. Output: Simple, Standard, Hard, Reasoning.";

            try
            {
                var response = await _ai.AskAsync(prompt, ct, fastModel);
                var clean = response.Trim().Replace(".", "");
                
                if (Enum.TryParse<TaskComplexity>(clean, true, out var result))
                {
                    // --- HARDWARE ADAPTATION ---
                    // If VRAM is tight (< 6GB), downgrade Hard tasks to Standard to avoid OOM or slow swap
                    if (vram > 0 && vram < 6.0 && result == TaskComplexity.Hard)
                    {
                        Console.WriteLine($"[Complexity] 📉 Hardware Limit: Downgrading Hard -> Standard (VRAM: {vram}GB)");
                        return TaskComplexity.Standard;
                    }
                    return result;
                }
                Console.Error.WriteLine($"[Complexity] Unrecognized classifier response '{clean}'. Falling back to Standard.");
                return TaskComplexity.Standard;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[Complexity] Classifier failed: {ex.Message}. Falling back to Standard.");
                return TaskComplexity.Standard;
            }
        }
    }
}

