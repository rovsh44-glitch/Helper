using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Helper.Runtime.Core;
using Helper.Runtime.Infrastructure;

namespace Helper.Runtime.Evolution
{
    public class SurgeonAgent : ISurgeonAgent
    {
        private readonly AILink _ai;
        private readonly ShadowWorkspace _shadow;

        public SurgeonAgent(AILink ai, ShadowWorkspace shadow)
        {
            _ai = ai;
            _shadow = shadow;
        }

        public async Task<bool> EvolveSelfAsync(string relativePath, string instruction, bool commit = false, CancellationToken ct = default)
        {
            if (commit)
            {
                Console.WriteLine($"[Security Alert] SYSTEM EVOLUTION COMMIT REQUESTED for {relativePath}.");
            }
            // 1. Prepare Womb
            Console.WriteLine($"[Surgeon] Cloning system to shadow workspace...");
            if (!await _shadow.CloneAsync(ct)) return false;

            // 2. Read Source
            var shadowPath = Path.Combine(_shadow.GetShadowPath(), relativePath);
            if (!File.Exists(shadowPath)) 
            {
                Console.WriteLine($"[Surgeon] Target file not found in shadow: {shadowPath}");
                return false;
            }
            var currentCode = await File.ReadAllTextAsync(shadowPath, ct);

            // 3. Propose Mutation
            Console.WriteLine($"[Surgeon] Proposing mutation for {relativePath}...");
            var prompt = $@"ACT AS A SYSTEM SURGEON.
            OBJECTIVE: {instruction}
            CURRENT CODE:
            {currentCode}
            
            OUTPUT ONLY RAW CORRECTED CODE. NO MARKDOWN.";
            
            var proposedCode = await _ai.AskAsync(prompt, ct);
            
            // Clean markdown if present
            if (proposedCode.Contains("```"))
            {
                var lines = proposedCode.Split('\n');
                var cleanLines = new List<string>();
                bool inBlock = false;
                foreach (var line in lines)
                {
                    if (line.Trim().StartsWith("```")) { inBlock = !inBlock; continue; }
                    cleanLines.Add(line);
                }
                proposedCode = string.Join("\n", cleanLines);
            }

            // 4. Apply to Shadow
            await _shadow.ApplyPatchAsync(relativePath, proposedCode.Trim(), ct);

            // 5. Verify
            Console.WriteLine($"[Surgeon] Verifying shadow integrity...");
            var errors = await _shadow.VerifyShadowBuildAsync(ct);

            if (errors.Count == 0)
            {
                Console.WriteLine($"[Surgeon] MUTATION VERIFIED. Build successful.");
                
                if (commit)
                {
                    await ApplyToLiveAsync(relativePath, proposedCode.Trim(), ct);
                    Console.WriteLine($"[Surgeon] 🧬 HOT SWAP COMPLETE. Mutation applied to live source.");
                    Console.WriteLine($"[Surgeon] ⚠️ RESTART REQUIRED for changes to take effect.");
                }
                else
                {
                    Console.WriteLine($"[Surgeon] Mutation valid but NOT committed. Use 'commit: true' to apply.");
                }
                return true;
            }
            else
            {
                Console.WriteLine($"[Surgeon] MUTATION REJECTED. Shadow build failed.");
                foreach (var err in errors) Console.WriteLine($"  - {err.Message}");
                return false;
            }
        }

        private async Task ApplyToLiveAsync(string relativePath, string newContent, CancellationToken ct)
        {
            var livePath = _shadow.GetSourcePath(relativePath);
            var normalizedLivePath = livePath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            var srcSegment = $"{Path.DirectorySeparatorChar}src{Path.DirectorySeparatorChar}";
            // Safety check: ensure we are writing inside the source tree.
            if (!normalizedLivePath.Contains(srcSegment, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Safety Lock: Cannot write outside source tree.");
            }
            
            await File.WriteAllTextAsync(livePath, newContent, ct);
        }
    }
}

