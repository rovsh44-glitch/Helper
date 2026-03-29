using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Helper.Runtime.Core;
using Helper.Runtime.Swarm;
using GTaskStatus = Helper.Runtime.Core.TaskStatus;

namespace Helper.Runtime.Infrastructure
{
    public class GraphOrchestrator : IGraphOrchestrator
    {
        private readonly AILink _ai;
        private readonly TumenOrchestrator _tumen; // We still use Tumen's units
        private readonly ICriticService _critic;
        private readonly IVisualInspector _inspector;
        private readonly IComplexityAnalyzer _complexity;
        private readonly IContextDistiller _distiller;

        public GraphOrchestrator(AILink ai, TumenOrchestrator tumen, ICriticService critic, IVisualInspector inspector, IComplexityAnalyzer complexity, IContextDistiller distiller)
        {
            _ai = ai;
            _tumen = tumen;
            _critic = critic;
            _inspector = inspector;
            _complexity = complexity;
            _distiller = distiller;
        }

        public async Task<GenerationResult> ExecuteGraphAsync(string prompt, string outputPath, Action<string>? onProgress = null, CancellationToken ct = default)
        {
            var distilledPrompt = _distiller.DistillPrompt(prompt);
            onProgress?.Invoke("🧠 [Graph] Building cognitive task map (MLA Compressed)...");

            // PREDICTIVE MOE: Start preloading the Reasoning model while we prepare
            try
            {
                await _ai.PreloadModelAsync("reasoning", ct);
            }
            catch (Exception ex)
            {
                onProgress?.Invoke($"⚠️ [Graph] Model preload skipped: {ex.Message}");
            }
            
            // 1. Build the initial graph (Using Reasoning model via MoE Router)
            var graph = await BuildTaskGraphAsync(distilledPrompt, ct);
            onProgress?.Invoke($"📊 [Graph] Map ready: {graph.Nodes.Count} specialized tasks planned.");

            // 2. Validate the strategy
            var strategyApproved = await ValidateStrategyAsync(graph, ct);
            if (!strategyApproved)
            {
                onProgress?.Invoke("🔄 [Graph] Strategy rejected. Re-planning with higher reasoning...");
                graph = await BuildTaskGraphAsync(distilledPrompt + " (SIMPLIFY AND ROBUSTIFY)", ct);
            }

            // 3. Execution Loop (MoE Actuator logic)
            var results = new List<GeneratedFile>();
            var allErrors = new List<BuildError>();

            while (graph.Nodes.Any(n => n.Status == GTaskStatus.Pending || n.Status == GTaskStatus.InProgress))
            {
                var readyTasks = graph.Nodes.Where(n => n.Status == GTaskStatus.Pending && 
                    n.Dependencies.All(depId => {
                        var dep = graph.Nodes.FirstOrDefault(x => x.Id == depId);
                        return dep != null && dep.Status == GTaskStatus.Completed;
                    })).ToList();

                if (!readyTasks.Any() && graph.Nodes.Any(n => n.Status == GTaskStatus.Pending))
                {
                    onProgress?.Invoke("⚠️ [Graph] Progress blocked by previous failures.");
                    break;
                }

                foreach (var task in readyTasks)
                {
                    // MOE ROUTING: Architects get Fusion, Developers get Uncensored
                    var taskComplexity = await _complexity.AnalyzeComplexityAsync(task.Name, ct);
                    string targetModel = task.AgentRole.Contains("Architect") ? _ai.GetBestModel("reasoning") : _ai.GetBestModel("coder");
                    
                    onProgress?.Invoke($"🚀 [Graph] Dispatching task: {task.Name} -> {targetModel} [Complexity: {taskComplexity}]");
                    
                    try 
                    {
                        if (task.AgentRole.Contains("Architect") || task.AgentRole.Contains("Developer"))
                        {
                            // Apply MLA-style compression to task description
                            var taskDescription = _distiller.DistillPrompt(task.Description);
                            var taskResult = await _tumen.ForgeWithTumenAsync(taskDescription, outputPath, onProgress, ct);
                            
                            // Compress generated code for future context
                            foreach (var f in taskResult.Files)
                            {
                                results.Add(f with { Content = _distiller.DistillCode(f.Content) });
                            }
                            allErrors.AddRange(taskResult.Errors);
                            
                            var nodeIdx = graph.Nodes.FindIndex(n => n.Id == task.Id);
                            if (nodeIdx != -1)
                            {
                                if (taskResult.Success)
                                    graph.Nodes[nodeIdx] = graph.Nodes[nodeIdx] with { Status = GTaskStatus.Completed, Result = "Success" };
                                else
                                {
                                    await AdaptGraphAsync(graph, graph.Nodes[nodeIdx], string.Join("\n", taskResult.Errors.Select(e => e.Message)), ct);
                                    onProgress?.Invoke($"🧬 [Graph] Failure detected. PLAN MUTATED.");
                                }
                            }
                        }
                        else 
                        {
                            var nodeIdx = graph.Nodes.FindIndex(n => n.Id == task.Id);
                            if (nodeIdx != -1) graph.Nodes[nodeIdx] = graph.Nodes[nodeIdx] with { Status = GTaskStatus.Completed };
                        }
                    }
                    catch (Exception ex)
                    {
                        onProgress?.Invoke($"❌ [Graph] Task {task.Name} crashed: {ex.Message}");
                        var nodeIdx = graph.Nodes.FindIndex(n => n.Id == task.Id);
                        if (nodeIdx != -1) graph.Nodes[nodeIdx] = graph.Nodes[nodeIdx] with { Status = GTaskStatus.Failed };
                    }
                }
            }

            // 4. Visual Quality Gate (Phase 4)
            bool visualPass = await _inspector.InspectProjectAsync(outputPath, onProgress, ct);
            if (!visualPass)
            {
                allErrors.Add(new BuildError("Visual", 0, "UI_DEFECT", "Project failed visual inspection"));
            }

            bool success = graph.Nodes.All(n => n.Status == GTaskStatus.Completed || n.AgentRole == "Optional") && visualPass;
            return new GenerationResult(success, results, outputPath, allErrors, TimeSpan.Zero);
        }

        private async Task AdaptGraphAsync(TaskGraph graph, TaskNode failedNode, string errorContext, CancellationToken ct)
        {
            var adaptPrompt = $@"
            ACT AS A STRATEGIC ADAPTATION AGENT.
            
            FAILURE DETECTED:
            Node ID: {failedNode.Id}
            Task Name: {failedNode.Name}
            Error: {errorContext}
            
            CURRENT GRAPH NODES:
            {string.Join("\n", graph.Nodes.Select(n => $"- {n.Id}: {n.Name} (Status: {n.Status})"))}
            
            OBJECTIVE: Propose 1-3 NEW correction tasks to fix this failure and allow the project to continue.
            
            RULES:
            1. New nodes MUST have unique IDs.
            2. New nodes should depend on the failure context and be prerequisites for the children of the failed node.
            
            OUTPUT ONLY JSON (List of TaskNodes):
            [
              {{ ""Id"": ""FIX_1"", ""Name"": ""Fix Code"", ""Description"": ""..."", ""AgentRole"": ""Developer"", ""Dependencies"": [""{failedNode.Id}""] }}
            ]";

            try
            {
                var newNodes = await _ai.AskJsonAsync<List<TaskNode>>(adaptPrompt, ct);
                if (newNodes == null || !newNodes.Any()) return;
                
                var idx = graph.Nodes.FindIndex(n => n.Id == failedNode.Id);
                graph.Nodes[idx] = graph.Nodes[idx] with { Status = GTaskStatus.Failed, Result = "Mutated" };

                foreach (var node in newNodes)
                {
                    if (!graph.Nodes.Any(n => n.Id == node.Id))
                    {
                        graph.Nodes.Add(node with { Status = GTaskStatus.Pending });
                    }
                }

                var children = graph.Nodes.Where(n => n.Dependencies.Contains(failedNode.Id)).ToList();
                foreach (var child in children)
                {
                    var cIdx = graph.Nodes.FindIndex(n => n.Id == child.Id);
                    var newDeps = child.Dependencies.ToList();
                    newDeps.AddRange(newNodes.Select(n => n.Id));
                    graph.Nodes[cIdx] = graph.Nodes[cIdx] with { Dependencies = newDeps };
                }
            }
            catch
            {
            }
        }

        private async Task<TaskGraph> BuildTaskGraphAsync(string prompt, CancellationToken ct)
        {
            var graphPrompt = $@"
            ACT AS A MASTER SYSTEMS ORCHESTRATOR.
            Task: {prompt}
            
            OBJECTIVE: Break down the task into a dependency graph of sub-tasks.
            
            ROLES AVAILABLE: Architect, Developer, Critic, Researcher.
            
            OUTPUT ONLY JSON (TaskGraph structure):
            {{
              ""Objective"": ""Short summary"",
              ""Nodes"": [
                {{ ""Id"": ""T1"", ""Name"": ""Design"", ""Description"": ""..."", ""AgentRole"": ""Architect"", ""Dependencies"": [] }},
                {{ ""Id"": ""T2"", ""Name"": ""Code"", ""Description"": ""..."", ""AgentRole"": ""Developer"", ""Dependencies"": [""T1""] }}
              ]
            }}";

            return await _ai.AskJsonAsync<TaskGraph>(graphPrompt, ct);
        }

        private async Task<bool> ValidateStrategyAsync(TaskGraph graph, CancellationToken ct)
        {
            var strategyText = string.Join("\n", graph.Nodes.Select(n => $"- {n.Name}: {n.Description} (Deps: {string.Join(",", n.Dependencies)})"));
            var critique = await _critic.CritiqueAsync(graph.Objective, strategyText, "Meta-Strategy Analysis", ct);
            return critique.IsApproved;
        }
    }
}

