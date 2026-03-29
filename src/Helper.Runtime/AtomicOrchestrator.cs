using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Helper.Runtime.Core;

namespace Helper.Runtime.Infrastructure
{
    public class AtomicOrchestrator : IAtomicOrchestrator
    {
        private readonly ICodeGenerator _coder;
        private readonly ICriticService _critic;
        private readonly IIntentBcaster _bcaster;
        private readonly AILink _ai;

        public AtomicOrchestrator(ICodeGenerator coder, ICriticService critic, IIntentBcaster bcaster, AILink ai)
        {
            _coder = coder;
            _critic = critic;
            _bcaster = bcaster;
            _ai = ai;
        }

        public async Task<GeneratedFile?> BuildAndValidateFileAsync(
            FileTask task, 
            ProjectPlan context, 
            List<GeneratedFile> existingFiles, 
            SystemSnapshot snapshot,
            Action<string>? onProgress, 
            CancellationToken ct = default)
        {
            await _bcaster.BroadcastIntentAsync($"Generate file: {task.Path}", $"Fulfill purpose: {task.Purpose} avoiding platform forbidden tech.", onProgress, ct);

            // Give context about current platform to prevent hallucinations
            var enrichedContext = new ProjectPlan(
                $"Target OS: {snapshot.Platform.OS} | Current Tree: {snapshot.DirectoryTree}\n" + context.Description,
                context.PlannedFiles);

            var file = await _coder.GenerateFileAsync(task, enrichedContext, existingFiles, ct);
            if (file == null) return null;

            onProgress?.Invoke($"🔎 [Atomic] Validating {file.RelativePath}...");

            // Logic Audit
            var audit = await _critic.AnalyzeAsync($"Ensure code complies with {snapshot.Platform.OS}. Code:\n{file.Content}", ct);
            
            if (!audit.IsApproved)
            {
                onProgress?.Invoke($"⚠️ [Atomic] Criticism received: {audit.Feedback}. Attempting fix...");
                
                // Single autonomous fix iteration
                var fixPrompt = $"The critic rejected this file '{file.RelativePath}'. Feedback: {audit.Feedback}\nOriginal:\n{file.Content}\nRewrite and OUTPUT ONLY CODE.";
                var fixedContent = await _ai.AskAsync(fixPrompt, ct, "qwen2.5-coder:14b");
                if (fixedContent.Contains("```csharp")) fixedContent = fixedContent.Split("```csharp")[1].Split("```")[0].Trim();
                else if (fixedContent.Contains("```")) fixedContent = fixedContent.Split("```")[1].Split("```")[0].Trim();
                
                file = new GeneratedFile(file.RelativePath, fixedContent, file.Language);
            }
            
            return file;
        }
    }
}
