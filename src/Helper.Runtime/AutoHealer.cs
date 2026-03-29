using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Helper.Runtime.Core;

namespace Helper.Runtime.Infrastructure
{
    public class AutoHealer : IAutoHealer
    {
        private readonly AILink _ai;
        private readonly IBuildValidator _validator;

        public AutoHealer(AILink ai, IBuildValidator validator)
        {
            _ai = ai;
            _validator = validator;
        }

        public async Task<List<BuildError>> HealAsync(string projectPath, List<BuildError> initialErrors, Action<string>? onProgress = null, CancellationToken ct = default)
        {
             var currentErrors = initialErrors;
             int attempts = 0;
             while (currentErrors != null && currentErrors.Count > 0 && attempts < 3)
             {
                 onProgress?.Invoke($"🩹 Healing Attempt {attempts + 1}/3...");
                 var error = currentErrors.FirstOrDefault();
                 if (error == null || string.IsNullOrEmpty(error.File)) { attempts++; continue; }

                 var filePath = Path.Combine(projectPath, error.File);
                 if (!File.Exists(filePath)) 
                 {
                     var foundFiles = Directory.GetFiles(projectPath, error.File, SearchOption.AllDirectories);
                     if (foundFiles.Length > 0) filePath = foundFiles[0]; 
                     else 
                     {
                         var csprojs = Directory.GetFiles(projectPath, "*.csproj");
                         if (csprojs.Length > 0) filePath = csprojs[0]; else break; 
                     }
                 }
                 
                 var sourceCode = await File.ReadAllTextAsync(filePath, ct);
                 var fixedCode = await _ai.AskAsync($"FIX ERROR: {error.Message}\nFILE: {error.File}\nCODE:\n{sourceCode}\nRETURN ONLY CORRECTED CODE.", ct, "qwen2.5-coder:14b");
                 var cleanCode = fixedCode.Replace("```csharp", "").Replace("```xml", "").Replace("```", "").Trim();
                 await File.WriteAllTextAsync(filePath, cleanCode, ct);
                 attempts++;
                 currentErrors = await _validator.ValidateAsync(projectPath, ct);
             }
             return currentErrors ?? new List<BuildError>();
        }
    }
}

