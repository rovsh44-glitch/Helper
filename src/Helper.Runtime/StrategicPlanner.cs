using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Helper.Runtime.Core;
using Helper.Runtime.Infrastructure;

namespace Helper.Runtime.Infrastructure
{
    public class StrategicPlanner : IStrategicPlanner
    {
        private readonly AILink _ai;

        public StrategicPlanner(AILink ai) => _ai = ai;

        public async Task<StrategicPlan> PlanStrategyAsync(string task, string availableContext, CancellationToken ct = default)
        {
            var prompt = $@"
            ACT AS A STRATEGIC AI ARCHITECT.
            
            TASK: {task}
            
            AVAILABLE CONTEXT:
            {availableContext}
            
            OBJECTIVE: 
            1. Analyze if the available context is enough to complete the task accurately.
            2. Propose 3 different strategies/branches of thought to solve this.
            3. Evaluate risks for each branch.
            4. Suggest tools to use (WebSearch, VectorSearch, CodeExecution, Vision).
            
            STRICT RULES:
            - DETECT the language of the TASK and respond (Reasoning, Description, Questions) in the SAME language.
            - If info is missing or technical requirements are ambiguous, set 'RequiresMoreInfo' to true and list specific questions.
            - Output ONLY valid JSON:
            {{
                ""SelectedStrategyId"": ""branch_1"",
                ""Options"": [
                    {{ 
                        ""Id"": ""branch_1"", 
                        ""Description"": ""..."", 
                        ""ConfidenceScore"": 0.9, 
                        ""Risks"": [""...""],
                        ""SuggestedTools"": [""WebSearch""]
                    }}
                ],
                ""Reasoning"": ""Why this plan was chosen"",
                ""RequiresMoreInfo"": false,
                ""ClarifyingQuestions"": [""Question 1"", ""Question 2""]
            }}";

            var response = await _ai.AskAsync(prompt, ct);
            var json = ExtractJson(response);

            try 
            {
                return System.Text.Json.JsonSerializer.Deserialize<StrategicPlan>(json) 
                    ?? new StrategicPlan("default", new(), "Parser failure", true);
            }
            catch 
            {
                return new StrategicPlan("error", new(), "Critical error in strategic engine", true);
            }
        }

        private string ExtractJson(string text)
        {
            if (text.Contains("```json"))
                return text.Split("```json")[1].Split("```")[0].Trim();
            return text.Trim();
        }
    }
}

