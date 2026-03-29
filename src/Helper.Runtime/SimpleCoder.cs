using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
            bool isWpf = task.Path.EndsWith(".xaml") || task.Path.EndsWith(".cs");
            bool isCSharp = task.Path.EndsWith(".cs");
            bool isCsproj = task.Path.EndsWith(".csproj");

            var wpfGuidelines = isWpf ? @"WPF RULES: 1. NO SUBDIRECTORIES: Save files directly in root. 2. STARTUP: App.xaml MUST have StartupUri=""MainWindow.xaml"". 3. BOOTSTRAP: MainWindow.xaml.cs MUST contain 'this.DataContext = new MainViewModel();'. 4. X:CLASS: x:Class=""Namespace.ClassName"". 5. INTERFACE: Implement planned interfaces exactly. 6. PATTERNS: Reuse the project's shared WPF command and observable patterns when available." : "";

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
            
            var prompt = $@"
            TASK: Write source code for {task.Path}. 
            Project Purpose: {context.Description}.
            File Purpose: {task.Purpose}. 
            TECHNICAL CONTRACT (MUST FOLLOW): {task.TechnicalContract}
            {wpfGuidelines}
            CRITICAL: 1. Output ONLY raw code. 2. DO NOT include headers or explanations.
            3. Implement EVERYTHING that belongs in THIS file.
            4. FORBIDDEN (exist elsewhere): {forbiddenClasses}.
            5. NAMESPACE RULE: Use ONLY the namespace '{projectNamespace}' for ALL files. 
            6. ALWAYS INCLUDE these using directives:
               using System;
               using System.Collections.Generic;
               using System.Collections.ObjectModel;
               using System.Linq;
               using System.Windows;
               using System.Windows.Input;
               using CommunityToolkit.Mvvm.ComponentModel;
               using CommunityToolkit.Mvvm.Input;
               using {projectNamespace};";

            string finalCode = "";
            int retry = 0;
            while (retry < 3)
            {
                var response = await _ai.AskAsync(prompt, ct);
                finalCode = _sanitizer.Sanitize(response, isCSharp ? "csharp" : (isWpf ? "xaml" : "text"));

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
            return new GeneratedFile(task.Path, finalCode);
        }
    }
}

