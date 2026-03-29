using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Helper.Runtime.Core;
using Helper.Runtime.Infrastructure;
using Helper.Runtime.Swarm.Core;

namespace Helper.Runtime.Swarm.Agents
{
    public class SwarmContractor
    {
        private readonly AILink _ai;
        public SwarmContractor(AILink ai) => _ai = ai;

        public async Task<List<SwarmArtifact>> EstablishContractsAsync(SwarmBlueprint blueprint, CancellationToken ct = default)
        {
            var contracts = blueprint.Files.Where(f => f.Role == FileRole.Model || f.Role == FileRole.Interface).ToList();
            var artifacts = new List<SwarmArtifact>();

            foreach (var file in contracts)
            {
                var prompt = $@"
                PROJECT: {blueprint.ProjectName}
                NAMESPACE: {blueprint.RootNamespace}
                TASK: Write shared contract code for {file.Path}.
                TYPE: {file.Role}
                
                RULES:
                1. Use 'namespace {blueprint.RootNamespace}'.
                2. ALWAYS Include:
                   using System;
                   using System.Collections.Generic;
                   using System.Collections.ObjectModel;
                   using {blueprint.RootNamespace}.Models;
                   using {blueprint.RootNamespace}.Interfaces;
                
                CONTEXT: This file will be used by other agents. Keep it clean. Defining data structures and signatures ONLY.
                ";
                
                var code = await _ai.AskAsync(prompt, ct);
                artifacts.Add(new SwarmArtifact(file.Path, SwarmCleaner.Clean(code, file.Path)));
            }
            return artifacts;
        }
    }

    public class SwarmSpecialist
    {
        private readonly AILink _ai;
        public SwarmSpecialist(AILink ai) => _ai = ai;

        public async Task<SwarmArtifact> ImplementAsync(SwarmFileDefinition task, SwarmBlueprint blueprint, List<SwarmArtifact> sharedContext, CancellationToken ct = default)
        {
            var existingTypes = new List<string>();
            foreach (var art in sharedContext)
            {
                var matches = System.Text.RegularExpressions.Regex.Matches(art.Content, @"(class|interface|record|struct|enum)\s+(\w+)");
                foreach (System.Text.RegularExpressions.Match m in matches) existingTypes.Add(m.Groups[2].Value);
            }
            var forbidden = string.Join(", ", existingTypes.Distinct());

            var contextStr = new StringBuilder();
            contextStr.AppendLine("--- SHARED CONTRACTS (READ ONLY) ---");
            foreach(var art in sharedContext)
            {
                contextStr.AppendLine($"File: {art.Path}");
                contextStr.AppendLine(art.Content);
                contextStr.AppendLine("---");
            }

            bool isXaml = task.Path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase);
            
            var prompt = $@"
            ROLE: Stateless Code Implementation Unit.
            STRICT RULES:
            1. IMPLEMENT bodies for methods defined in the SHARED CONTRACTS.
            2. FORBIDDEN: Do not declare ANY types (class, struct, enum, interface) found in FORBIDDEN TYPES.
            3. FORBIDDEN: Do not declare ANY new public types that are not part of this file's purpose.
            4. SCOPE: ONLY code for {task.Path}. 
            5. NAMESPACE: Must be '{blueprint.RootNamespace}'.
            
            FORBIDDEN TYPES (DO NOT REDEFINE): {forbidden}

            {contextStr}
            
            TASK: Implement {task.Path}.
            PURPOSE: {task.Purpose}.
            
            TYPE SPECIFIC RULES:
            {(isXaml ? "This is a GUI Definition. OUTPUT XAML ONLY. Start with <Window or <UserControl. DO NOT WRITE C#." : "This is Logic/CodeBehind. OUTPUT C# ONLY. Start with using statements.")}
            
            ALWAYS INCLUDE these using directives for C# files:
            using System;
            using System.Collections.Generic;
            using System.Collections.ObjectModel;
            using System.Linq;
            using System.Windows;
            using {blueprint.RootNamespace};
            using {blueprint.RootNamespace}.Models;
            using {blueprint.RootNamespace}.Interfaces;
            using {blueprint.RootNamespace}.Services;
            using {blueprint.RootNamespace}.ViewModels;

            CRITICAL: You MUST implement ALL abstract methods inherited from base classes. Do NOT leave them empty or abstract.
            
            CODE ONLY. NO MARKDOWN. NO CHAT.";

            var code = await _ai.AskAsync(prompt, ct);
            return new SwarmArtifact(task.Path, SwarmCleaner.Clean(code, task.Path));
        }
    }
}

