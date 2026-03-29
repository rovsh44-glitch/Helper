using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Helper.Runtime.Core;

namespace Helper.Runtime.Infrastructure
{
    public class TemplateGeneralizer : ITemplateGeneralizer
    {
        private readonly AILink _ai;
        private readonly ICodeSanitizer _sanitizer;
        private readonly string _templatesRoot;

        public TemplateGeneralizer(AILink ai, ICodeSanitizer sanitizer, string? templatesRoot = null)
        {
            _ai = ai;
            _sanitizer = sanitizer;
            _templatesRoot = HelperWorkspacePathResolver.ResolveTemplatesRoot(templatesRoot);
        }

        public async Task<ProjectTemplate?> GeneralizeProjectAsync(string projectPath, string targetTemplateId, CancellationToken ct = default)
        {
            if (!Directory.Exists(projectPath)) return null;

            Console.WriteLine($"[TemplateMiner] Mining project at {projectPath}...");

            var libraryPath = Path.Combine(_templatesRoot, targetTemplateId.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(libraryPath);

            var files = Directory.GetFiles(projectPath, "*.*", SearchOption.AllDirectories)
                .Where(f => !f.Contains(@"\obj") && !f.Contains(@"\bin") && !f.Contains(".vs"))
                .ToList();

            foreach (var file in files)
            {
                var relativePath = Path.GetRelativePath(projectPath, file);
                var content = await File.ReadAllTextAsync(file, ct);
                
                // Если это C# код, пытаемся его абстрагировать
                if (file.EndsWith(".cs"))
                {
                    content = await AbstractCodeAsync(content, relativePath, ct);
                }

                var targetFile = Path.Combine(libraryPath, relativePath);
                var targetDir = Path.GetDirectoryName(targetFile);
                if (targetDir != null) Directory.CreateDirectory(targetDir);
                
                await File.WriteAllTextAsync(targetFile, content, ct);
            }

            // Создаем template.json
            var templateInfo = new
            {
                Id = targetTemplateId,
                Name = $"{targetTemplateId} (Auto-Generated)",
                Description = "Automatically generalized template from successful project.",
                Language = "csharp",
                Tags = new[] { "auto-generated", "mined" }
            };

            await File.WriteAllTextAsync(Path.Combine(libraryPath, "template.json"), 
                JsonSerializer.Serialize(templateInfo, new JsonSerializerOptions { WriteIndented = true }), ct);

            return new ProjectTemplate(targetTemplateId, templateInfo.Name, templateInfo.Description, "csharp", libraryPath);
        }

        private async Task<string> AbstractCodeAsync(string code, string fileName, CancellationToken ct)
        {
            var prompt = $@"
            ACT AS A SOFTWARE ARCHITECT.
            
            OBJECTIVE: Transform a specific C# file into a generic template.
            
            RULES:
            1. Replace specific business logic names with generic ones where appropriate (but keep the architectural structure).
            2. Remove heavy implementations of methods, leave them as stubs or with basic logic.
            3. Keep all namespaces and standard library imports.
            4. Keep MVVM/Infrastructure boilerplate.
            
            FILE: {fileName}
            CODE:
            {code}
            
            OUTPUT ONLY RAW TRANSFORMED CODE.";

            try
            {
                var result = await _ai.AskAsync(prompt, ct);
                return _sanitizer.Sanitize(result, "csharp");
            }
            catch
            {
                return code; // Fallback to original if AI fails
            }
        }
    }
}

