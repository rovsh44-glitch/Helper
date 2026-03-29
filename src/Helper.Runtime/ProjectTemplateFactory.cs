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
    public class ProjectTemplateFactory : ITemplateFactory
    {
        private readonly AILink _ai;
        private readonly ShellExecutor _shell;
        private readonly IProcessGuard _processGuard;
        private readonly string _targetBaseDir;

        public ProjectTemplateFactory(AILink ai, IProcessGuard processGuard, string? targetBaseDir = null)
        {
            _ai = ai;
            _shell = new ShellExecutor();
            _processGuard = processGuard;
            _targetBaseDir = HelperWorkspacePathResolver.ResolveTemplatesRoot(targetBaseDir);
        }

        public async Task<bool> ProcureTemplateAsync(string request, Action<string>? onProgress = null, CancellationToken ct = default)
        {
            onProgress?.Invoke($"🔍 Analyzing procurement strategy for: {request}...");

            var prompt = $@"
            ACT AS A DEVOPS ARCHITECT.
            Create an offline project template procurement strategy for: {request}.
            
            STRICT RULES:
            1. All commands run in ONE sequence.
            2. WINDOWS BATCH: Use 'call npm' instead of 'npm'.
            3. Output ONLY valid JSON:
            {{
                ""TemplateId"": ""Language_FrameworkName"",
                ""Commands"": [""command1"", ""command2""],
                ""VerificationCommand"": ""command to verify""
            }}";

            var response = await _ai.AskAsync(prompt, ct, "qwen2.5-coder:14b");
            var json = response.Contains("```json") ? response.Split("```json")[1].Split("```")[0].Trim() : response.Trim();
            
            var strategy = JsonSerializer.Deserialize<TemplateProcurementStrategy>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (strategy == null) return false;

            // --- SECURITY: Validate all AI-generated commands ---
            try 
            {
                foreach (var cmd in strategy.Commands) { _processGuard.EnsureSafeCommand(cmd); }
                _processGuard.EnsureSafeCommand(strategy.VerificationCommand);
            }
            catch (Exception ex)
            {
                onProgress?.Invoke($"🛡️ Security Block: LLM proposed dangerous command. {ex.Message}");
                return false;
            }

            onProgress?.Invoke($"🛠️ Building '{strategy.TemplateId}' template...");
            
            var tempDir = Path.Combine(Path.GetTempPath(), "helper_procurement_" + Guid.NewGuid().ToString("N")[..6]);
            Directory.CreateDirectory(tempDir);

            try
            {
                var result = await _shell.ExecuteSequenceAsync(tempDir, strategy.Commands);
                if (!result.Success) return false;

                var verify = await _shell.ExecuteSequenceAsync(tempDir, new List<string> { strategy.VerificationCommand });
                if (!verify.Success) return false;

                var finalPath = Path.Combine(_targetBaseDir, strategy.TemplateId);
                if (Directory.Exists(finalPath)) Directory.Delete(finalPath, true);
                CopyDirectory(tempDir, finalPath);
                return true;
            }
            catch (Exception ex)
            {
                onProgress?.Invoke($"❌ Template procurement failed: {ex.Message}");
                return false;
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        }

        private static void CopyDirectory(string sourceDir, string destinationDir)
        {
            Directory.CreateDirectory(destinationDir);
            foreach (string file in Directory.GetFiles(sourceDir))
            {
                File.Copy(file, Path.Combine(destinationDir, Path.GetFileName(file)));
            }
            foreach (string dir in Directory.GetDirectories(sourceDir))
            {
                CopyDirectory(dir, Path.Combine(destinationDir, Path.GetFileName(dir)));
            }
        }
    }
}

