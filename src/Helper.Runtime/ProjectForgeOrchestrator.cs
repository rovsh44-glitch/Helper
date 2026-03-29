using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Helper.Runtime.Core;

namespace Helper.Runtime.Infrastructure
{
    public class ProjectForgeOrchestrator : IProjectForgeOrchestrator
    {
        private readonly ITemplateManager _templateManager;
        private readonly ITemplateFactory _templateFactory;
        private readonly IProjectPlanner _planner;
        private readonly ICodeGenerator _coder;
        private readonly IBuildValidator _validator;
        private readonly IForgeArtifactValidator _artifactValidator;
        private readonly IAutoHealer _healer;
        private readonly IntegrityAuditor _auditor;
        private readonly AILink _ai;
        public ProjectForgeOrchestrator(
            ITemplateManager templateManager,
            ITemplateFactory templateFactory,
            IProjectPlanner planner,
            ICodeGenerator coder,
            IBuildValidator validator,
            IForgeArtifactValidator artifactValidator,
            IAutoHealer healer,
            IntegrityAuditor auditor,
            AILink ai)
        {
            _templateManager = templateManager;
            _templateFactory = templateFactory;
            _planner = planner;
            _coder = coder;
            _validator = validator;
            _artifactValidator = artifactValidator;
            _healer = healer;
            _auditor = auditor;
            _ai = ai;
        }

        public async Task<GenerationResult> ForgeProjectAsync(
            string prompt, 
            string templateId, 
            Action<string>? onProgress = null, 
            CancellationToken ct = default)
        {
            var sw = Stopwatch.StartNew();

            var availability = await _templateManager.ResolveTemplateAvailabilityAsync(templateId, ct);
            var template = availability.Template;
            if (template == null)
            {
                if (availability.State != TemplateAvailabilityState.Missing)
                {
                    onProgress?.Invoke($"⛔ {availability.Reason}");
                    sw.Stop();
                    return new GenerationResult(
                        false,
                        new List<GeneratedFile>(),
                        "ERROR",
                        new List<BuildError>
                        {
                            new("System", 0, "TEMPLATE_BLOCKED_BY_CERTIFICATION_STATUS", availability.Reason)
                        },
                        sw.Elapsed);
                }

                onProgress?.Invoke($"⚠️ Template '{templateId}' missing. Attempting procurement...");
                var procured = await _templateFactory.ProcureTemplateAsync(templateId, onProgress, ct);
                if (!procured) 
                {
                    sw.Stop();
                    return new GenerationResult(false, new List<GeneratedFile>(), "ERROR", 
                        new List<BuildError> { new BuildError("System", 0, "TEMPLATE_NOT_FOUND", $"Failed to procure template '{templateId}'") }, 
                        sw.Elapsed);
                }
            }

            var projectPath = Path.Combine(ResolveForgeOutputRoot(), $"{templateId}_{Guid.NewGuid().ToString("N")[..6]}");

            // 1. Preparation
            onProgress?.Invoke($"🔨 Cloning template '{templateId}' to forge...");
            await _templateManager.CloneTemplateAsync(templateId, projectPath, ct);

            // 2. Strict Golden Template Mode (No AI Interference)
            onProgress?.Invoke("✨ Golden Template Mode: Using template AS IS (No modifications).");
            
            var filesInTemplate = Directory.GetFiles(projectPath, "*.*", SearchOption.AllDirectories);
            var generatedFiles = new List<GeneratedFile>();
            foreach (var f in filesInTemplate)
            {
                var relativePath = Path.GetRelativePath(projectPath, f);
                if (ShouldSkipGeneratedFile(relativePath, f))
                {
                    continue;
                }

                generatedFiles.Add(new GeneratedFile(relativePath, File.ReadAllText(f)));
            }

            return await FinalizeForgeAsync(prompt, projectPath, generatedFiles, sw, 0, onProgress, ct);
        }

        private async Task<GenerationResult> FinalizeForgeAsync(string prompt, string projectPath, List<GeneratedFile> generatedFiles, Stopwatch sw, int healCycles, Action<string>? onProgress, CancellationToken ct)
        {
            onProgress?.Invoke("⚖️ Verifying build integrity...");
            var errors = await _validator.ValidateAsync(projectPath, ct);
            
            // NO AUTO-HEALING FOR GOLDEN TEMPLATES
            
            sw.Stop();
            var verification = await _artifactValidator.ValidateAsync(projectPath, errors, ct);
            var success = verification.Success;

            if (!verification.Success)
            {
                onProgress?.Invoke($"⚠️ Forge verification failed: {verification.Reason}");
            }

            onProgress?.Invoke(success ? "✅ Forge completed successfully!" : "❌ Forge failed quality gate.");

            return new GenerationResult(success, generatedFiles, projectPath, errors, sw.Elapsed, healCycles);
        }

        private static bool ShouldSkipGeneratedFile(string relativePath, string fullPath)
        {
            var segments = relativePath
                .Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Any(segment =>
                string.Equals(segment, "bin", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(segment, "obj", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(segment, ".compile_gate", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            return fullPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                   fullPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
        }

        private static string ResolveForgeOutputRoot()
        {
            var configuredRoot = Environment.GetEnvironmentVariable("HELPER_FORGE_OUTPUT_ROOT");
            if (!string.IsNullOrWhiteSpace(configuredRoot))
            {
                return Path.GetFullPath(configuredRoot);
            }

            var projectsRoot = HelperWorkspacePathResolver.ResolveWritableProjectsRoot();
            return Path.Combine(projectsRoot, "FORGE_OUTPUT");
        }
    }
}

