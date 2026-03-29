using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Helper.Runtime.Core;
using Helper.Runtime.Infrastructure;

namespace Helper.Runtime.Evolution
{
    public class BlueprintEngine : IBlueprintEngine
    {
        private readonly AILink _ai;
        private readonly IPersonaOrchestrator _shadowRoundtable;

        public BlueprintEngine(AILink ai, IPersonaOrchestrator shadowRoundtable)
        {
            _ai = ai;
            _shadowRoundtable = shadowRoundtable;
        }

        public async Task<ProjectBlueprint> DesignBlueprintAsync(string prompt, OSPlatform targetOS, CancellationToken ct = default)
        {
            var designPrompt = $@"ACT AS A MASTER ARCHITECT. 
            Design a clean system structure for: {prompt}
            TARGET OS: {targetOS}
            
            RULES:
            1. Use '/' for paths.
            2. Define files with roles: Infrastructure, Model, Service.
            3. FOR NuGetPackages: USE A FLAT ARRAY OF STRINGS ONLY (e.g., [""Package1"", ""Package2""]). DO NOT USE OBJECTS.
            4. Output ONLY JSON:
            {{
                ""Name"": ""ProjectName"",
                ""TargetOS"": ""{targetOS}"",
                ""Files"": [ {{ ""Path"": ""src/..."", ""Purpose"": ""..."", ""Role"": ""Infrastructure"" }} ],
                ""NuGetPackages"": [],
                ""ArchitectureReasoning"": ""...""
            }}";

            return await _ai.AskJsonAsync<ProjectBlueprint>(designPrompt, ct);
        }

        public async Task<bool> ValidateBlueprintAsync(ProjectBlueprint blueprint, CancellationToken ct = default)
        {
            // Use the Shadow Roundtable to critique the blueprint
            var report = await _shadowRoundtable.ConductRoundtableAsync(
                $"Validate this project blueprint for {blueprint.TargetOS}: " + 
                System.Text.Json.JsonSerializer.Serialize(blueprint), ct);
            
            return report.ConflictLevel < 0.8; // Reject if conflict is too high
        }
    }
}

