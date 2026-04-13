using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Helper.Runtime.Core;

namespace Helper.Runtime.Infrastructure
{
    public class ProcessGuard : IProcessGuard
    {
        private readonly AILink _ai;
        private readonly string[] _interpreterCommands = {
            "pwsh", "powershell", "python", "node", "cmd"
        };
        private readonly string[] _forbiddenCommands = { 
            "rm", "del", "erase", "format", "mkfs", "chmod", "chown", 
            "attrib", "net", "user", "kill", "taskkill", "shutdown", "reboot" 
        };
        private readonly string[] _allowedCommands = {
            "dotnet", "npm", "git", "mkdir", "cd", "ls", "dir", "echo", "type", "cat", "code", "nuget"
        };

        private readonly string[] _protectedPaths = { 
            "src", "mcp_config", ".git", ".env", "Program.cs", "Core/Contracts" 
        };
        private static readonly Regex SafeDotnetTestFilterRegex = new(
            @"--filter\s+(?:""(?<value>[^""]*)""|'(?<value>[^']*)')",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        public ProcessGuard(AILink ai)
        {
            _ai = ai;
        }

        public void EnsureSafeCommand(string command, string? workingDir = null, List<Goal>? activeGoals = null)
        {
            var lowerCommand = command.ToLower().Replace("\\", "/"); // Normalize slashes for check
            var parts = lowerCommand.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return;

            var baseCmd = parts[0];

            // 1. Static Rules: Forbidden base commands
            if (_forbiddenCommands.Contains(baseCmd))
            {
                throw new UnauthorizedAccessException($"[ProcessGuard] ⛔ BLOCK: Command '{baseCmd}' is restricted for safety.");
            }

            if (_interpreterCommands.Contains(baseCmd))
            {
                throw new UnauthorizedAccessException($"[ProcessGuard] ⛔ BLOCK: Interpreter command '{baseCmd}' is not allowed.");
            }

            if (!_allowedCommands.Contains(baseCmd))
            {
                throw new UnauthorizedAccessException($"[ProcessGuard] ⛔ BLOCK: Command '{baseCmd}' is not in allow-list.");
            }

            // 2. Static Rules: Dangerous chaining
            string[] dangerousSequences = { "|", ">", ">>", "&", "&&", ";", "`", "$(" };
            foreach (var seq in dangerousSequences)
            {
                if (lowerCommand.Contains(seq))
                {
                    if (seq == "|" && IsSafeDotnetTestFilterPipeUsage(command))
                    {
                        continue;
                    }

                    throw new UnauthorizedAccessException($"[ProcessGuard] ⛔ BLOCK: Command chaining or redirection detected ('{seq}').");
                }
            }

            // 3. Static Rules: Protected system paths
            foreach (var path in _protectedPaths)
            {
                var normalizedPath = path.ToLower().Replace("\\", "/");
                if (lowerCommand.Contains(normalizedPath))
                {
                    if (lowerCommand.Contains("dotnet build") || lowerCommand.Contains("dotnet test")) continue;
                    throw new UnauthorizedAccessException($"[ProcessGuard] ⛔ BLOCK: Access to protected system path '{path}' is denied.");
                }
            }

            // 4. SEMANTIC RULE: Intent-Based Validation (Phase 1)
            if (activeGoals != null && activeGoals.Any())
            {
                ValidateSemanticAlignmentHeuristic(command, activeGoals);
            }
        }

        public async Task EnsureSafeCommandAsync(string command, string? workingDir = null, List<Goal>? activeGoals = null, System.Threading.CancellationToken ct = default)
        {
            EnsureSafeCommand(command, workingDir, activeGoals);

            if (activeGoals == null || !activeGoals.Any())
            {
                return;
            }

            if (!bool.TryParse(Environment.GetEnvironmentVariable("HELPER_PROCESS_GUARD_MODEL_CHECK"), out var enabled) || !enabled)
            {
                return;
            }

            var goalsSummary = string.Join("\n", activeGoals.Select(g => $"- {g.Title}: {g.Description}"));
            var prompt = $@"
You are a security validator.
Decide whether command is aligned with goals.
Goals:
{goalsSummary}
Command: {command}
Reply SAFE or DANGEROUS.";

            var response = await _ai.AskAsync(prompt, ct, _ai.GetBestModel("reasoning"));
            if (response.Trim().Contains("DANGEROUS", StringComparison.OrdinalIgnoreCase))
            {
                throw new UnauthorizedAccessException($"[ProcessGuard] 🛡️ MODEL BLOCK: Command '{command}' flagged as dangerous.");
            }
        }

        private void ValidateSemanticAlignmentHeuristic(string command, List<Goal> goals)
        {
            var normalizedCommand = command.ToLowerInvariant();
            var allowedPrefixes = new[]
            {
                "dotnet", "npm", "git", "mkdir", "cd", "ls", "dir", "echo"
            };

            if (allowedPrefixes.Any(prefix => normalizedCommand == prefix || normalizedCommand.StartsWith(prefix + " ")))
            {
                return;
            }

            var goalKeywords = goals
                .SelectMany(g => $"{g.Title} {g.Description}".ToLowerInvariant()
                    .Split(new[] { ' ', ',', '.', ':', ';', '-', '_', '/', '\\', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries))
                .Where(token => token.Length > 2)
                .Distinct()
                .ToList();

            bool hasGoalReference = goalKeywords.Any(token => normalizedCommand.Contains(token));
            if (!hasGoalReference)
            {
                throw new UnauthorizedAccessException($"[ProcessGuard] 🛡️ SEMANTIC BLOCK: Command '{command}' does not align with current intent/goals.");
            }
        }

        private static bool IsSafeDotnetTestFilterPipeUsage(string command)
        {
            if (string.IsNullOrWhiteSpace(command) ||
                !DotnetTestCommandSupport.IsDotnetTestCommand(command))
            {
                return false;
            }

            var match = SafeDotnetTestFilterRegex.Match(command);
            if (!match.Success)
            {
                return false;
            }

            var filterValue = match.Groups["value"].Value;
            if (string.IsNullOrWhiteSpace(filterValue) || !filterValue.Contains('|'))
            {
                return false;
            }

            var filterStart = match.Groups["value"].Index;
            var filterEnd = filterStart + match.Groups["value"].Length;
            foreach (var pipeIndex in command
                .Select((character, index) => (character, index))
                .Where(static item => item.character == '|')
                .Select(static item => item.index))
            {
                if (pipeIndex < filterStart || pipeIndex >= filterEnd)
                {
                    return false;
                }
            }

            return true;
        }
    }
}

