using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Helper.Runtime.Core;

namespace Helper.Runtime.Infrastructure
{
    public class SimplePlanner : IProjectPlanner
    {
        private readonly AILink _ai;
        public SimplePlanner(AILink ai) => _ai = ai;

        public async Task<ProjectPlan> PlanProjectAsync(string prompt, CancellationToken ct = default)
        {
            var userPrompt = $@"Project Request: {prompt}
            Plan this project. CRITICAL: For .NET projects, you MUST include a .csproj file as the first file.
            Output format: {{ ""Description"": ""..."", ""PlannedFiles"": [ {{ ""Path"": ""..."", ""Purpose"": ""..."", ""Dependencies"": [], ""TechnicalContract"": ""..."" }} ] }}";
            
            var response = await _ai.AskAsync(userPrompt, ct, "qwen2.5-coder:14b");
            var json = response.Contains("```json") ? response.Split("```json")[1].Split("```")[0].Trim() : response.Trim();
            
                        try 
                        {
                            return JsonSerializer.Deserialize<ProjectPlan>(json, JsonDefaults.Options) ?? throw new Exception("Plan is null");
                        }            catch 
            { 
                return new ProjectPlan(prompt, new List<FileTask> { 
                    new FileTask("MainWindow.xaml", "View", new List<string>()), 
                    new FileTask("MainWindow.xaml.cs", "Code", new List<string>()) 
                }); 
            }
        }
    }
}

