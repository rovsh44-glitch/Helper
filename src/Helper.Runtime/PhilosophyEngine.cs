using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Helper.Runtime.Core;

namespace Helper.Runtime.Infrastructure
{
    public class PhilosophyEngine : IPhilosophyEngine
    {
        private readonly AILink _ai;
        private readonly IReflectionService _reflection;
        private readonly IVectorStore _memory;

        public PhilosophyEngine(AILink ai, IReflectionService reflection, IVectorStore memory)
        {
            _ai = ai;
            _reflection = reflection;
            _memory = memory;
        }

        public async Task<List<EngineeringPrinciple>> DistillPrinciplesAsync(CancellationToken ct = default)
        {
            Console.WriteLine("[Philosophy] 🧘 Deep meditation: Reconciling Expert Theory with Practical Experience...");

            // 1. Gather theoretical base (from the newly indexed books)
            var theoryChunks = await _memory.SearchAsync(new float[768], HelperKnowledgeCollections.CanonicalSemantic, limit: 10, ct: ct);
            var theoryContext = string.Join("\n", theoryChunks.Select(c => $"- Theory: {c.Content}"));

            // 2. Gather practical base (from past failures)
            var lessons = await _reflection.SearchLessonsAsync("Critical engineering failures", 10, ct);
            var practiceContext = string.Join("\n", lessons.Select(l => $"- Practice Failure: {l.ErrorPattern} | Solution: {l.Solution}"));

            // 3. Synthesis Prompt
            var prompt = $@"
            ACT AS AN AGI ARCHITECT (LEVEL 4).
            
            EXPERT THEORY BASE:
            {theoryContext}
            
            PRACTICAL EXPERIENCE BASE:
            {practiceContext}
            
            TASK: Synthesize these two sources into 3 unique 'Helper Engineering Principles'. 
            The principles must describe how to build robust software specifically in an AI-driven, C#/.NET environment.
            
            FOR EACH PRINCIPLE, PROVIDE:
            1. Title (Professional & Visionary)
            2. Description (Deep architectural meaning)
            3. Rationale (Why this bridge between theory and practice is necessary)
            4. ProofOfConcept (A tiny C# snippet or pseudocode demonstrating the principle)
            
            OUTPUT ONLY JSON (List of EngineeringPrinciple):
            [
              {{ 
                ""Title"": ""..."", 
                ""Description"": ""..."", 
                ""Rationale"": ""..."", 
                ""Examples"": [""Proof of concept code string here""] 
              }}
            ]";

            try
            {
                return await _ai.AskJsonAsync<List<EngineeringPrinciple>>(prompt, ct, _ai.GetBestModel("reasoning"));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Philosophy] Meditation interrupted: {ex.Message}");
                return new List<EngineeringPrinciple>();
            }
        }

        public async Task PublishManifestoAsync(CancellationToken ct = default)
        {
            var principles = await DistillPrinciplesAsync(ct);
            if (principles == null || !principles.Any()) return;

            var path = HelperWorkspacePathResolver.ResolveDocPath("HELPER_MANIFESTO.md");
            
            var content = "# 📜 The Helper Engineering Manifesto (AGI Level 4 Edition)\n";
            content += $"*Synthesized on {DateTime.Now:f} after indexing 17 expert volumes.*\n\n";
            content += "## The Intersection of Expert Theory and AI Autonomy\n\n";

            foreach (var p in principles)
            {
                content += $"### 💎 {p.Title}\n";
                content += $"> {p.Description}\n\n";
                content += $"**Why it matters:** {p.Rationale}\n\n";
                content += "**Demonstration:**\n```csharp\n";
                foreach (var ex in p.Examples) content += ex + "\n";
                content += "```\n";
                content += "\n---\n";
            }

            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            
            await File.WriteAllTextAsync(path, content, ct);
            Console.WriteLine($"[Philosophy] ✨ Manifesto EVOLVED and published to {path}");
        }
    }
}

