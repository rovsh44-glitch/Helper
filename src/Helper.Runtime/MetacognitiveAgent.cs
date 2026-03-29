using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Helper.Runtime.Core;

namespace Helper.Runtime.Infrastructure
{
    public class MetacognitiveAgent : IMetacognitiveAgent
    {
        private readonly AILink _ai;
        private readonly ISurgicalToolbox _surgery;

        public MetacognitiveAgent(AILink ai, ISurgicalToolbox surgery)
        {
            _ai = ai;
            _surgery = surgery;
        }

        public async Task<bool> DebugSelfAsync(string failureReason, Action<string>? onProgress, CancellationToken ct = default)
        {
            onProgress?.Invoke("🧠 [Metacognition] Initiating deep self-reflection over Helper source code...");
            
            // 1. Identify keywords from failure
            var keywordPrompt = "Extract a single 1-word C# class or keyword related to this failure: " + failureReason;
            var keyword = await _ai.AskAsync(keywordPrompt, ct);
            keyword = keyword.Split(new[] { ' ', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "Orchestrator";

            onProgress?.Invoke($"🔍 [Metacognition] Grepping source for '{keyword}'...");
            
            // 2. Grep own source code
            var srcDir = HelperWorkspacePathResolver.ResolveWorkspaceFile(System.IO.Path.Combine("src", "Helper.Runtime"));
            var grepResults = await _surgery.GrepAsync(srcDir, keyword, "*.cs", ct);
            
            if (!grepResults.Any())
            {
                onProgress?.Invoke("⚠️ [Metacognition] Could not find relevant source code context.");
                return false;
            }

            var context = string.Join("\n", grepResults.Take(5).Select(g => $"{System.IO.Path.GetFileName(g.FilePath)}:{g.LineNumber} -> {g.Content}"));

            // 3. Propose fix
            var debugPrompt = "ACT AS A SENIOR C# ARCHITECT. \n" +
                              "You are debugging your own core framework.\n\n" +
                              "FAILURE: " + failureReason + "\n\n" +
                              "GREP CONTEXT:\n" + context + "\n\n" +
                              "Task: Propose a surgical fix. Explain WHAT needs to change in WHICH file.\n" +
                              "Output your reasoning clearly.";

            var proposal = await _ai.AskAsync(debugPrompt, ct, "qwen2.5-coder:14b");
            onProgress?.Invoke($"💡 [Metacognition] Self-Fix Proposal:\n{proposal}");
            
            return true;
        }
    }
}

