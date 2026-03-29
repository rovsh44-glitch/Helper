using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Helper.Runtime.Core;
using Helper.Runtime.Generation;
using Helper.Runtime.Infrastructure;
using Helper.Runtime.Swarm.Agents;
using Helper.Runtime.Swarm.Core;

namespace Helper.Runtime.Swarm
{
    public class TumenOrchestrator
    {
        private readonly SwarmArchitect _architect;
        private readonly IProjectForgeOrchestrator _forge;
        private readonly IInternalObserver _observer;
        private readonly IIntentBcaster _bcaster;
        private readonly IIdentifierSanitizer _identifierSanitizer;
        private readonly IGenerationPathPolicy _pathPolicy;
        private readonly IGenerationCompileGate _compileGate;
        private readonly IGenerationMetricsService _metrics;
        private readonly ITemplateRoutingService _templateRouting;
        private readonly TumenRuntimeSettings _runtimeSettings;
        private readonly TumenGeneratedFileStore _fileStore;
        private readonly TumenFileBatchService _fileBatchService;
        private readonly TumenRunReportService _runReportService;

        public TumenOrchestrator(
            AILink ai,
            ICodeSanitizer sanitizer,
            IProjectForgeOrchestrator forge,
            ITemplateRoutingService templateRouting,
            IBuildExecutor executor,
            ICriticService critic,
            IReflectionService reflection,
            IInternalObserver observer,
            IAtomicOrchestrator atomic,
            IIntentBcaster bcaster,
            IIdentifierSanitizer identifierSanitizer,
            IGenerationPathSanitizer pathSanitizer,
            IMethodSignatureValidator methodSignatureValidator,
            IMethodSignatureNormalizer methodSignatureNormalizer,
            IUsingInferenceService usingInference,
            IMethodBodySemanticGuard semanticGuard,
            IBlueprintJsonSchemaValidator schemaValidator,
            IBlueprintContractValidator blueprintContractValidator,
            IGeneratedFileAstValidator fileAstValidator,
            IGenerationPathPolicy pathPolicy,
            IGenerationCompileGate compileGate,
            IGenerationValidationReportWriter reportWriter,
            IGenerationHealthReporter healthReporter,
            IGenerationMetricsService metrics)
        {
            _forge = forge;
            _templateRouting = templateRouting;
            _observer = observer;
            _bcaster = bcaster;
            _identifierSanitizer = identifierSanitizer;
            _pathPolicy = pathPolicy;
            _compileGate = compileGate;
            _metrics = metrics;
            _runtimeSettings = TumenRuntimeSettings.Load();
            var critiqueRunner = new TumenCritiqueRunner(critic, _runtimeSettings.CriticTimeout);
            var arban = new ArbanAgent(ai, sanitizer, reflection, methodSignatureValidator);
            var zuun = new ZuunAssembler(methodSignatureNormalizer, semanticGuard);
            _architect = new SwarmArchitect(ai, schemaValidator, blueprintContractValidator);
            _fileStore = new TumenGeneratedFileStore(identifierSanitizer, usingInference);
            _fileBatchService = new TumenFileBatchService(arban, zuun, bcaster, pathSanitizer, fileAstValidator, _runtimeSettings, critiqueRunner, _fileStore);
            _runReportService = new TumenRunReportService(reportWriter, healthReporter);
        }

        public async Task<GenerationResult> ForgeWithTumenAsync(
            string request,
            string outputBase,
            Action<string>? onProgress = null,
            CancellationToken ct = default,
            bool skipTemplateRouting = false,
            TemplateRoutingDecision? routeHint = null)
        {
            var isGoldenEligible = GoldenTemplateIntentClassifier.HasExplicitGoldenTemplateRequest(request);
            var workloadClass = GenerationWorkloadClassifier.Resolve(request);
            var routeDecision = routeHint;
            if (!skipTemplateRouting)
            {
                var route = await _templateRouting.RouteAsync(request, ct);
                routeDecision = route;
                if (route.Matched && !string.IsNullOrWhiteSpace(route.TemplateId))
                {
                    onProgress?.Invoke($"✨ [Tumen] Golden Template detected: {route.TemplateId} (confidence {route.Confidence:0.00}). Delegating to Forge...");
                    return await _forge.ForgeProjectAsync(request, route.TemplateId, onProgress, ct);
                }
            }

            var startedAt = DateTime.UtcNow;
            var runErrors = new List<string>();
            var runWarnings = new List<string>();
            var buildErrors = new List<BuildError>();
            var allFiles = new List<GeneratedFile>();
            var placeholderFindings = new List<GeneratedArtifactPlaceholderFinding>();
            var retryCount = 0;
            var methodCount = 0;
            var stageDurationsSec = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            string? validatedProjectPath = null;
            GenerationRunContext? runContext = null;
            SwarmBlueprint? blueprint = null;

            _metrics.RecordRun();

            try
            {
                await _bcaster.BroadcastIntentAsync("Capture Directory State", "Ensure Tumen is aware of current project state.", onProgress, ct);
                var captureTimer = Stopwatch.StartNew();
                var snapshot = await _observer.CaptureSnapshotAsync(outputBase, ct);
                captureTimer.Stop();
                stageDurationsSec["capture_snapshot"] = Math.Round(captureTimer.Elapsed.TotalSeconds, 3);

                await _bcaster.BroadcastIntentAsync("Architect Design", "Creating initial files structure.", onProgress, ct);
                onProgress?.Invoke("🚩 [Tumen] Great Khan planning invasion...");
                var blueprintTimer = Stopwatch.StartNew();
                blueprint = await _architect.DesignSystemAsync(request, ct);
                blueprintTimer.Stop();
                stageDurationsSec["design_blueprint"] = Math.Round(blueprintTimer.Elapsed.TotalSeconds, 3);

                var projectName = $"{_identifierSanitizer.SanitizeProjectName(blueprint.ProjectName)}_Tumen";
                runContext = _pathPolicy.CreateRunContext(outputBase, projectName);
                Directory.CreateDirectory(runContext.RawProjectRoot);
                var fileDefinitions = blueprint.Files.Take(_runtimeSettings.SmokeMaxFiles).ToList();
                if (fileDefinitions.Count < blueprint.Files.Count)
                {
                    runWarnings.Add($"Smoke profile truncated files from {blueprint.Files.Count} to {fileDefinitions.Count}.");
                }

                var fileGenTimer = Stopwatch.StartNew();
                var batchResult = await _fileBatchService.GenerateAsync(
                    new TumenFileBatchRequest(
                        runContext.RawProjectRoot,
                        blueprint.RootNamespace,
                        snapshot.Platform.OS.ToString(),
                        fileDefinitions,
                        onProgress),
                    ct);
                fileGenTimer.Stop();
                stageDurationsSec["generate_files"] = Math.Round(fileGenTimer.Elapsed.TotalSeconds, 3);
                methodCount += batchResult.MethodCount;
                retryCount += batchResult.RetryCount;
                runErrors.AddRange(batchResult.Errors);
                runWarnings.AddRange(batchResult.Warnings);
                placeholderFindings.AddRange(batchResult.PlaceholderFindings);
                allFiles.AddRange(batchResult.Files);

                if (placeholderFindings.Count > 0)
                {
                    runErrors.AddRange(placeholderFindings.Select(x => $"PlaceholderScan: {x.ToDisplayString()}"));
                }

                onProgress?.Invoke("⚖️ [CompileGate] Building generated package...");
                var compileTimer = Stopwatch.StartNew();
                var compileGate = await _compileGate.ValidateAsync(runContext.RawProjectRoot, ct);
                compileTimer.Stop();
                stageDurationsSec["compile_gate"] = Math.Round(compileTimer.Elapsed.TotalSeconds, 3);
                if (!compileGate.Success)
                {
                    runErrors.AddRange(compileGate.Errors.Select(x =>
                        string.IsNullOrWhiteSpace(x.Code) ? x.Message : $"[{x.Code}] {x.Message}"));
                    buildErrors.AddRange(compileGate.Errors);
                    _metrics.RecordCompileFail();
                }
                else if (placeholderFindings.Count == 0)
                {
                    _fileStore.CopyValidatedProject(runContext.RawProjectRoot, runContext.ValidatedProjectRoot);
                    validatedProjectPath = runContext.ValidatedProjectRoot;
                }

                if (runErrors.Count > 0)
                {
                    _metrics.RecordValidationFail();
                }

                var report = _runReportService.Build(
                    new TumenRunReportContext(
                        runContext.RunId,
                        request,
                        projectName,
                        startedAt,
                        runContext.RawProjectRoot,
                        validatedProjectPath,
                        allFiles.Count,
                        methodCount,
                        retryCount,
                        blueprint != null,
                        compileGate.Success,
                        runErrors,
                        runWarnings,
                        routeDecision,
                        isGoldenEligible,
                        workloadClass,
                        stageDurationsSec,
                        placeholderFindings.Select(x => x.ToDisplayString()).ToArray()));

                await _runReportService.PersistAsync(report, ct);

                var success = runErrors.Count == 0 && compileGate.Success && placeholderFindings.Count == 0;
                return new GenerationResult(success, allFiles, validatedProjectPath ?? runContext.RawProjectRoot, buildErrors, DateTime.UtcNow - startedAt);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                var runId = runContext?.RunId ?? $"TIMEOUT_{DateTime.UtcNow:yyyyMMdd_HHmmss}";
                var rawPath = runContext?.RawProjectRoot ?? outputBase;
                if (runErrors.Count == 0)
                {
                    runErrors.Add("GENERATION_TIMEOUT: Tumen pipeline was canceled before completion.");
                }

                var timeoutReport = _runReportService.Build(
                    new TumenRunReportContext(
                        runId,
                        request,
                        TumenRunReportService.ResolveProjectName(runContext, blueprint),
                        startedAt,
                        rawPath,
                        validatedProjectPath,
                        allFiles.Count,
                        methodCount,
                        retryCount,
                        blueprint != null,
                        false,
                        runErrors,
                        runWarnings,
                        routeDecision,
                        isGoldenEligible,
                        workloadClass,
                        stageDurationsSec,
                        placeholderFindings.Select(x => x.ToDisplayString()).ToArray()));
                await _runReportService.PersistSafelyAsync(timeoutReport);
                throw;
            }
            catch (Exception ex)
            {
                var runId = runContext?.RunId ?? $"FAILED_{DateTime.UtcNow:yyyyMMdd_HHmmss}";
                var rawPath = runContext?.RawProjectRoot ?? outputBase;
                runErrors.Add(ex.Message);
                _metrics.RecordValidationFail();

                var failureReport = _runReportService.Build(
                    new TumenRunReportContext(
                        runId,
                        request,
                        TumenRunReportService.ResolveProjectName(runContext, blueprint),
                        startedAt,
                        rawPath,
                        validatedProjectPath,
                        allFiles.Count,
                        methodCount,
                        retryCount,
                        blueprint != null,
                        false,
                        runErrors,
                        runWarnings,
                        routeDecision,
                        isGoldenEligible,
                        workloadClass,
                        stageDurationsSec,
                        placeholderFindings.Select(x => x.ToDisplayString()).ToArray()));
                await _runReportService.PersistSafelyAsync(failureReport);

                buildErrors.Add(new BuildError("Tumen", 0, "GENERATION_FAILURE", ex.Message));
                return new GenerationResult(false, allFiles, runContext?.RawProjectRoot ?? outputBase, buildErrors, DateTime.UtcNow - startedAt);
            }
        }
    }
}

