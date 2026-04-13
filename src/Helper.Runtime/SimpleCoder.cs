using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Helper.Runtime.Core;

namespace Helper.Runtime.Infrastructure
{
    public class SimpleCoder : ICodeGenerator
    {
        private readonly AILink _ai;
        private readonly ICodeSanitizer _sanitizer;
        private readonly RoslynLiveCorrector _corrector = new();

        public SimpleCoder(AILink ai, ICodeSanitizer sanitizer) 
        { 
            _ai = ai; 
            _sanitizer = sanitizer;
        }

        public async Task<GeneratedFile> GenerateFileAsync(FileTask task, ProjectPlan context, List<GeneratedFile>? previousFiles = null, CancellationToken ct = default)
        {
            var normalizedPath = task.Path ?? string.Empty;
            var isXaml = normalizedPath.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase);
            var isCSharp = normalizedPath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase);
            var isCsproj = normalizedPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase);

            var existingClasses = new List<string>();

            if (previousFiles != null)
            {
                foreach (var pf in previousFiles)
                {
                    var matches = System.Text.RegularExpressions.Regex.Matches(pf.Content, @"(class|interface|record|struct)\s+(\w+)");
                    foreach (System.Text.RegularExpressions.Match m in matches)
                    {
                        existingClasses.Add(m.Groups[2].Value);
                    }
                }
            }

            var forbiddenClasses = string.Join(", ", existingClasses.Distinct());
            var projectNamespace = "GeneratedApp";
            if (!string.IsNullOrEmpty(context.Description))
            {
                var words = context.Description.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (words.Length > 0)
                {
                    var firstWord = new string(words[0].Where(c => char.IsLetterOrDigit(c)).ToArray());
                    if (!string.IsNullOrEmpty(firstWord) && firstWord.Length > 2) projectNamespace = firstWord;
                }
            }

            var languageGuidance = BuildLanguageGuidance(normalizedPath, isCSharp, isXaml, isCsproj, projectNamespace);
            var prompt = $@"
            TASK: Write source code for {task.Path}. 
            Project Purpose: {context.Description}.
            File Purpose: {task.Purpose}. 
            TECHNICAL CONTRACT (MUST FOLLOW): {task.TechnicalContract}
            FILE-TYPE RULES: {languageGuidance}
            GENERAL RULES:
            1. Output ONLY the raw file content. Do not include markdown fences, headers, or explanations.
            2. Implement EVERYTHING that belongs in THIS file, but do not redefine types that already exist elsewhere.
            3. FORBIDDEN (exist elsewhere): {forbiddenClasses}.
            4. Namespace rule: when the file type uses namespaces, use ONLY the namespace '{projectNamespace}' unless the technical contract explicitly requires otherwise.
            5. Include ONLY the using/import directives actually required by this file.
            6. Prefer platform-neutral, framework-appropriate code. Do not assume WPF, CommunityToolkit, or any specific UI stack unless the file type or technical contract explicitly requires it.";

            string finalCode = "";
            int retry = 0;
            while (retry < 3)
            {
                var response = await _ai.AskAsync(prompt, ct);
                finalCode = _sanitizer.Sanitize(response, isCSharp ? "csharp" : (isXaml ? "xaml" : "text"));

                if (isCsproj) finalCode = _sanitizer.FixCsproj(finalCode);

                if (isCSharp)
                {
                    var validation = _corrector.ValidateSyntax(finalCode);
                    if (validation.Valid) break;
                    retry++;
                    prompt += $"\n\n⚠️ SYNTAX ERROR:\n{string.Join("\n", validation.Errors)}";
                }
                else break;
            }
            return new GeneratedFile(normalizedPath, finalCode);
        }

        private static string BuildLanguageGuidance(string path, bool isCSharp, bool isXaml, bool isCsproj, string projectNamespace)
        {
            if (isCsproj)
            {
                return "Return valid SDK-style project XML only. Preserve well-formed XML and include only required project metadata, references, and build items.";
            }

            if (isXaml)
            {
                return $"This is a XAML file. Return valid XAML only. Use x:Class that matches the '{projectNamespace}' namespace when the file requires a code-behind pair.";
            }

            if (isCSharp)
            {
                return $"This is a C# file. Return compilable C# only. Infer the minimal set of namespaces and framework dependencies from '{path}' and the technical contract.";
            }

            return $"Return the exact file content for '{path}' without markdown fencing.";
        }
    }
}

