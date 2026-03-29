using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Helper.Runtime.Core;
using Helper.Runtime.Generation;
using Helper.Runtime.Infrastructure;
using Helper.Runtime.Swarm.Core;

namespace Helper.Runtime.Swarm.Agents
{
    public class SwarmArchitect
    {
        private readonly AILink _ai;
        private readonly IBlueprintJsonSchemaValidator _schemaValidator;
        private readonly IBlueprintContractValidator _contractValidator;
        private bool _experimentalMode = false;

        public SwarmArchitect(
            AILink ai,
            IBlueprintJsonSchemaValidator schemaValidator,
            IBlueprintContractValidator contractValidator)
        {
            _ai = ai;
            _schemaValidator = schemaValidator;
            _contractValidator = contractValidator;
        }

        public void SetExperimentalMode(bool enabled) => _experimentalMode = enabled;

        public async Task<SwarmBlueprint> DesignSystemAsync(string userRequest, CancellationToken ct = default)
        {
            var mutationHint = _experimentalMode 
                ? "EXPERIMENTAL MODE ACTIVE: DO NOT use standard MVVM/Standard patterns. Combine unconventional patterns (ECS, Actor Model, Functional, etc.). INNOVATE."
                : "STRICT RULES: Design a modular, flat-structure WPF application. Follow best practices.";

            var basePrompt = $@"
            You are the Lead Software Architect.
            User Request: {userRequest}
            
            OBJECTIVE: Design the project structure.
            
            {mutationHint}
            
            STRICT RULES:
            1. Root Namespace MUST be strictly defined.
            2. ALL files must be listed.
            3. XAML FILES (*.xaml) MUST contain ONLY XML/XAML markup.
            4. CODE-BEHIND (*.xaml.cs) MUST contain ONLY C# code. 
            5. Categorize files by Role. ALLOWED ROLES: Infrastructure, Model, Interface, ViewModel, View, Service, Logic, Contract, Configuration, Script, Resource, Test.
            6. DECOMPOSITION: For each C# class, you MUST define 3-7 specific Methods (ArbanMethodTask). 
            7. File Dependencies: Every file object MUST contain 'Dependencies' as an array of strings (empty array is allowed).
            8. Every method MUST include ContextHints (empty string allowed).
            9. Dependencies: Only include essential packages (CommunityToolkit.Mvvm).
            
            OUTPUT FORMAT: JSON ONLY. Matches 'SwarmBlueprint' structure.
            Example:
            {{
              ""ProjectName"": ""ChessV1"",
              ""RootNamespace"": ""ChessV1"",
              ""NuGetPackages"": [""Microsoft.Xaml.Behaviors.Wpf""],
              ""Files"": [
                {{ 
                   ""Path"": ""Models/Board.cs"", 
                   ""Purpose"": ""8x8 Grid Data"", 
                   ""Role"": ""Model"", 
                   ""Dependencies"": [],
                   ""Methods"": [
                      {{ ""Name"": ""Init"", ""Signature"": ""public void Initialize()"", ""Purpose"": ""Reset board"", ""ContextHints"": ""Use 2D array"" }}
                   ]
                }}
              ]
            }}";
            var attemptPrompt = basePrompt;
            var accumulatedErrors = new List<string>();

            for (var attempt = 1; attempt <= 3; attempt++)
            {
                var rawJson = await _ai.AskAsync(attemptPrompt, ct, _ai.GetBestModel("reasoning"));
                var json = SwarmCleaner.Clean(rawJson, "plan.json");
                var schema = _schemaValidator.ValidateRawJson(json);
                if (!schema.IsValid)
                {
                    accumulatedErrors = schema.Errors.ToList();
                    attemptPrompt = BuildRepairPrompt(basePrompt, json, accumulatedErrors, attempt);
                    continue;
                }

                try
                {
                    var blueprint = JsonSerializer.Deserialize<SwarmBlueprint>(json, JsonDefaults.Options)
                                    ?? throw new Exception("Architect returned null blueprint.");

                    var validation = _contractValidator.ValidateAndNormalize(blueprint);
                    if (validation.IsValid && validation.Blueprint is not null)
                    {
                        return validation.Blueprint;
                    }

                    accumulatedErrors = validation.Errors.ToList();
                    attemptPrompt = BuildRepairPrompt(basePrompt, json, accumulatedErrors, attempt);
                }
                catch (Exception ex)
                {
                    accumulatedErrors = new List<string> { ex.Message };
                    attemptPrompt = BuildRepairPrompt(basePrompt, json, accumulatedErrors, attempt);
                }
            }

            if (ReadFlag("HELPER_ARCHITECT_FALLBACK_BLUEPRINT", true))
            {
                return BuildFallbackBlueprint(userRequest);
            }

            throw new Exception("Architect failed to design valid blueprint after retries: " + string.Join("; ", accumulatedErrors.Take(8)));
        }

        private static string BuildRepairPrompt(string basePrompt, string previousJson, IReadOnlyList<string> errors, int attempt)
        {
            var sb = new StringBuilder(basePrompt.Length + 600);
            sb.AppendLine(basePrompt);
            sb.AppendLine();
            sb.AppendLine($"REPAIR ATTEMPT: {attempt}");
            sb.AppendLine("Your previous JSON was rejected.");
            sb.AppendLine("Validation errors:");
            foreach (var error in errors.Take(12))
            {
                sb.AppendLine($"- {error}");
            }

            sb.AppendLine();
            sb.AppendLine("Return FULL corrected JSON only.");
            sb.AppendLine("Every Files[i] MUST contain Dependencies: [].");
            sb.AppendLine("Every Methods[j] MUST contain ContextHints.");
            sb.AppendLine();
            sb.AppendLine("Previous JSON:");
            sb.AppendLine(previousJson);
            return sb.ToString();
        }

        private static SwarmBlueprint BuildFallbackBlueprint(string userRequest)
        {
            var projectName = userRequest.Contains("todo", StringComparison.OrdinalIgnoreCase) ? "TodoApp" : "GeneratedApp";
            var rootNamespace = projectName;
            var files = new List<SwarmFileDefinition>
            {
                new(
                    "Models/TodoItem.cs",
                    "Task domain model",
                    FileRole.Model,
                    new List<string>(),
                    new List<ArbanMethodTask>
                    {
                        new("Normalize", "public void Normalize()", "Normalize entity state", string.Empty)
                    }),
                new(
                    "Interfaces/ITodoService.cs",
                    "Service contract",
                    FileRole.Interface,
                    new List<string>(),
                    new List<ArbanMethodTask>
                    {
                        new("ExecuteAsync", "Task ExecuteAsync()", "Execute task service", string.Empty)
                    }),
                new(
                    "Services/TodoService.cs",
                    "Service implementation",
                    FileRole.Service,
                    new List<string> { "Interfaces/ITodoService.cs", "Models/TodoItem.cs" },
                    new List<ArbanMethodTask>
                    {
                        new("ExecuteAsync", "public Task ExecuteAsync()", "Perform task operations", string.Empty)
                    }),
                new(
                    "ViewModels/MainViewModel.cs",
                    "ViewModel coordination",
                    FileRole.ViewModel,
                    new List<string> { "Interfaces/ITodoService.cs" },
                    new List<ArbanMethodTask>
                    {
                        new("Execute", "public void Execute()", "Entry action for UI", string.Empty)
                    })
            };

            return new SwarmBlueprint(projectName, rootNamespace, files, new List<string>());
        }

        private static bool ReadFlag(string envName, bool fallback)
        {
            var raw = Environment.GetEnvironmentVariable(envName);
            return bool.TryParse(raw, out var parsed) ? parsed : fallback;
        }
    }
}

