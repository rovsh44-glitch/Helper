using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Helper.Runtime.Core;

namespace Helper.Runtime.Infrastructure
{
    public interface IAutoDebugger
    {
        Task<string?> ProposeFixAsync(string filePath, string code, List<BuildError> errors, CancellationToken ct = default);
    }

    public class AutoDebugger : IAutoDebugger
    {
        private readonly AILink _ai;
        private readonly IReflectionService _reflection;

        public AutoDebugger(AILink ai, IReflectionService reflection)
        {
            _ai = ai;
            _reflection = reflection;
        }

        public async Task<string?> ProposeFixAsync(string filePath, string code, List<BuildError> errors, CancellationToken ct = default)
        {
            var errorContext = string.Join("\n", errors.Select(e => $"Line {e.Line}: [{e.Code}] {e.Message}"));
            
            // Recall similar lessons
            var lessons = await _reflection.SearchLessonsAsync(errorContext, 2, ct);
            var lessonText = lessons.Any() 
                ? "\nRELEVANT PAST LESSONS:\n" + string.Join("\n", lessons.Select(l => $"- {l.ErrorPattern}: {l.Solution}"))
                : "";

            var prompt = $@"
            ACT AS AN ELITE DEBUGGING AGENT.
            
            FILE: {filePath}
            
            ERRORS:
            {errorContext}
            
            CODE:
            {code}
            {lessonText}
            
            TASK: Identify the EXACT cause of these build errors and provide the FULL corrected code for the file.
            Output ONLY the raw code.";

            try
            {
                var fixedCode = await _ai.AskAsync(prompt, ct, _ai.GetBestModel("coder"));
                // Clean markdown
                if (fixedCode.Contains("```"))
                {
                    var lines = fixedCode.Split('\n');
                    var cleanLines = new List<string>();
                    bool inBlock = false;
                    foreach (var line in lines)
                    {
                        if (line.Trim().StartsWith("```")) { inBlock = !inBlock; continue; }
                        if (inBlock) cleanLines.Add(line);
                    }
                    if (cleanLines.Any()) return string.Join("\n", cleanLines).Trim();
                }
                return fixedCode.Trim();
            }
            catch
            {
                return null;
            }
        }
    }
}

