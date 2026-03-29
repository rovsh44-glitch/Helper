using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Helper.Runtime.Core;

namespace Helper.Runtime.Infrastructure
{
    public class EvolutionEngine : IEvolutionEngine
    {
        private readonly AILink _ai;
        private readonly ISurgeonAgent _surgeon;
        private readonly string _sourceRoot;

        public EvolutionEngine(AILink ai, ISurgeonAgent surgeon)
        {
            _ai = ai;
            _surgeon = surgeon;
            _sourceRoot = HelperWorkspacePathResolver.ResolveHelperRoot();
        }

        public async Task<MutationProposal?> ProposeEvolutionAsync(HealthStatus status, CancellationToken ct = default)
        {
            if (status.IsHealthy && !status.Issues.Any()) return null;

            // 1. Identify problematic component from issues
            var issueText = string.Join("\n", status.Issues);
            var analyzerPrompt = $@"Based on these system health issues, which source file in the current project is most likely responsible?
            ISSUES:
            {issueText}
            
            Return ONLY the relative file path (e.g., src/Helper.Api/Program.cs).";

            var relativePath = await _ai.AskAsync(analyzerPrompt, ct, _ai.GetBestModel("fast"));
            relativePath = relativePath.Trim();

            var fullPath = Path.Combine(_sourceRoot, relativePath);
            if (!File.Exists(fullPath)) return null;

            // 2. Read original code
            var originalCode = await File.ReadAllTextAsync(fullPath, ct);

            // 3. Ask AI for a fix instruction
            var instructionPrompt = $"Given these health issues:\n{issueText}\nHow should we fix {relativePath}? Provide a one-sentence instruction.";
            var instruction = await _ai.AskAsync(instructionPrompt, ct);

            // 4. Use Surgeon to propose mutation (without commit)
            // Note: We need to modify SurgeonAgent to return the proposed code.
            // For now, we will simulate the proposal logic here to avoid deep refactoring.
            
            var mutationPrompt = $@"ACT AS A SYSTEM SURGEON.
            FIX THIS CODE TO RESOLVE ISSUES: {issueText}
            FILE: {relativePath}
            CODE:
            {originalCode}
            
            OUTPUT ONLY RAW CORRECTED CODE. NO MARKDOWN.";

            var proposedCode = await _ai.AskAsync(mutationPrompt, ct);

            return new MutationProposal(
                Guid.NewGuid(),
                relativePath,
                originalCode,
                proposedCode,
                $"Auto-detected fix for: {status.Issues.FirstOrDefault()}",
                DateTime.Now
            );
        }

        public async Task<bool> ApplyMutationAsync(MutationProposal mutation, CancellationToken ct = default)
        {
            var fullPath = Path.Combine(_sourceRoot, mutation.FilePath);
            await File.WriteAllTextAsync(fullPath, mutation.ProposedCode, ct);
            return true;
        }
    }
}

