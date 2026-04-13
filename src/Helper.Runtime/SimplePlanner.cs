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
            var json = ExtractJsonPayload(response);

            try
            {
                var plan = JsonSerializer.Deserialize<ProjectPlan>(json, JsonDefaults.Options)
                    ?? throw new ProjectPlanningException("planner_null_plan", "Planner returned an empty plan payload.");

                if (plan.PlannedFiles is null || plan.PlannedFiles.Count == 0)
                {
                    throw new ProjectPlanningException("planner_empty_plan", "Planner returned a plan without files.");
                }

                return plan;
            }
            catch (JsonException ex)
            {
                throw new ProjectPlanningException("planner_invalid_json", "Planner returned invalid JSON.", ex);
            }
        }

        private static string ExtractJsonPayload(string response)
        {
            if (string.IsNullOrWhiteSpace(response))
            {
                throw new ProjectPlanningException("planner_empty_response", "Planner returned an empty response.");
            }

            const string fencedPrefix = "```json";
            var startIndex = response.IndexOf(fencedPrefix, StringComparison.OrdinalIgnoreCase);
            if (startIndex < 0)
            {
                return response.Trim();
            }

            var payloadStart = startIndex + fencedPrefix.Length;
            var endIndex = response.IndexOf("```", payloadStart, StringComparison.Ordinal);
            if (endIndex < 0)
            {
                throw new ProjectPlanningException("planner_unclosed_fence", "Planner returned an unterminated JSON code fence.");
            }

            return response[payloadStart..endIndex].Trim();
        }
    }
}

