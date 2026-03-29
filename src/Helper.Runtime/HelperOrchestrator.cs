using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Helper.Runtime.Core;
using Helper.Runtime.Generation;
using Helper.Runtime.Swarm;

namespace Helper.Runtime.Infrastructure
{
    public partial class HelperOrchestrator : IHelperOrchestrator
    {
        private readonly IModelOrchestrator _modelSelector;
        private readonly IResearchEngine _researchEngine;
        private readonly IMaintenanceService _maintenance;
        private readonly TumenOrchestrator _tumen;
        private readonly IReflectionService _reflection;
        private readonly IGraphOrchestrator _graph;
        private readonly IToolService _tools;
        private readonly IConsciousnessService _consciousness;
        private readonly IAutoDebugger _debugger;
        private readonly IArchitectMutation _mutation;
        private readonly IExpertConsultant _expertConsultant;
        private readonly IBlueprintEngine _blueprints;
        private readonly ISurgicalToolbox _surgery;
        private readonly IPlatformGuard _platforms;
        private readonly IInternalObserver _observer;
        private readonly IIntentBcaster _bcaster;
        private readonly IMetacognitiveAgent _metacognitive;
        private readonly ITemplateRoutingService _templateRouting;
        private readonly IGenerationMetricsService _metrics;
        private readonly IGenerationValidationReportWriter _generationReportWriter;
        private readonly IGenerationHealthReporter _generationHealthReporter;
        private readonly IFailureEnvelopeFactory _failureEnvelopeFactory;
        private readonly IGenerationStageTimeoutPolicy _stageTimeoutPolicy;
        private readonly IFixStrategyRunner _fixStrategyRunner;
        private readonly IGenerationTemplatePromotionService _templatePromotionService;
        private readonly IRouteTelemetryService? _routeTelemetry;

        public IProjectForgeOrchestrator Forge { get; }

        public HelperOrchestrator(
            IModelOrchestrator modelSelector,
            IResearchEngine researchEngine,
            IMaintenanceService maintenance,
            IProjectForgeOrchestrator forge,
            TumenOrchestrator tumen,
            IReflectionService reflection,
            IGraphOrchestrator graph,
            IToolService tools,
            IConsciousnessService consciousness,
            IAutoDebugger debugger,
            IArchitectMutation mutation,
            IExpertConsultant expertConsultant,
            IBlueprintEngine blueprints,
            ISurgicalToolbox surgery,
            IPlatformGuard platforms,
            IInternalObserver observer,
            IIntentBcaster bcaster,
            IMetacognitiveAgent metacognitive,
            ITemplateRoutingService templateRouting,
            IGenerationMetricsService metrics,
            IGenerationValidationReportWriter generationReportWriter,
            IGenerationHealthReporter generationHealthReporter,
            IFailureEnvelopeFactory failureEnvelopeFactory,
            IGenerationStageTimeoutPolicy stageTimeoutPolicy,
            IFixStrategyRunner fixStrategyRunner,
            IGenerationTemplatePromotionService templatePromotionService,
            IRouteTelemetryService? routeTelemetry = null)
        {
            _modelSelector = modelSelector;
            _researchEngine = researchEngine;
            _maintenance = maintenance;
            Forge = forge;
            _tumen = tumen;
            _reflection = reflection;
            _graph = graph;
            _tools = tools;
            _consciousness = consciousness;
            _debugger = debugger;
            _mutation = mutation;
            _expertConsultant = expertConsultant;
            _blueprints = blueprints;
            _surgery = surgery;
            _platforms = platforms;
            _observer = observer;
            _bcaster = bcaster;
            _metacognitive = metacognitive;
            _templateRouting = templateRouting;
            _metrics = metrics;
            _generationReportWriter = generationReportWriter;
            _generationHealthReporter = generationHealthReporter;
            _failureEnvelopeFactory = failureEnvelopeFactory;
            _stageTimeoutPolicy = stageTimeoutPolicy;
            _fixStrategyRunner = fixStrategyRunner;
            _templatePromotionService = templatePromotionService;
            _routeTelemetry = routeTelemetry;
        }

        public async Task<GenerationResult> GenerateProjectAsync(GenerationRequest request, bool includeTests = true, Action<string>? onProgress = null, CancellationToken ct = default)
        {
            var sw = Stopwatch.StartNew();
            var isRussian = Regex.IsMatch(request.Prompt, @"[а-яА-Я]");
            var smokeProfile = ReadFlag("HELPER_SMOKE_PROFILE", false);
            var enableSuccessReflection = ReadFlag("HELPER_ENABLE_SUCCESS_REFLECTION", true);
            var createTimeoutSeconds = ReadInt("HELPER_CREATE_TIMEOUT_SEC", 900, 30, 900);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(createTimeoutSeconds));
            var pipelineCt = timeoutCts.Token;
            var routingTimer = Stopwatch.StartNew();

            try
            {
                var route = await ExecuteStageAsync(
                    GenerationTimeoutStage.Routing,
                    stageCt => _templateRouting.RouteAsync(request.Prompt, stageCt),
                    pipelineCt);
                routingTimer.Stop();
                _metrics.RecordGoldenTemplateRoute(route.Matched);
                var expectedGoldenTemplateId = GoldenTemplateIntentClassifier.ResolveExplicitTemplateId(request.Prompt);
                var forceGoldenRouteOnly = ReadFlag("HELPER_GOLDEN_FORCE_ROUTE_ONLY", false);
                if (forceGoldenRouteOnly && !string.IsNullOrWhiteSpace(expectedGoldenTemplateId))
                {
                    if (!route.Matched || string.IsNullOrWhiteSpace(route.TemplateId))
                    {
                        var reason = $"Golden route is mandatory, but routing did not match expected template '{expectedGoldenTemplateId}'.";
                        onProgress?.Invoke(isRussian ? $"⛔ {reason}" : $"⛔ {reason}");
                        _metrics.RecordValidationFail();
                        return await BuildGoldenRouteMismatchResultAsync(request, route, expectedGoldenTemplateId, reason, sw.Elapsed, pipelineCt);
                    }

                    if (!string.Equals(route.TemplateId, expectedGoldenTemplateId, StringComparison.OrdinalIgnoreCase))
                    {
                        var reason = $"Golden route mismatch: expected '{expectedGoldenTemplateId}', got '{route.TemplateId}'.";
                        onProgress?.Invoke(isRussian ? $"⛔ {reason}" : $"⛔ {reason}");
                        _metrics.RecordValidationFail();
                        return await BuildGoldenRouteMismatchResultAsync(request, route, expectedGoldenTemplateId, reason, sw.Elapsed, pipelineCt);
                    }
                }

                if (route.Matched && !string.IsNullOrWhiteSpace(route.TemplateId))
                {
                    onProgress?.Invoke(isRussian
                        ? $"✨ Обнаружен золотой шаблон: {route.TemplateId} (confidence {route.Confidence:0.00}). Мгновенное развертывание..."
                        : $"✨ Golden Template detected: {route.TemplateId} (confidence {route.Confidence:0.00}). Rapid deployment...");
                    _metrics.RecordRun();
                    var forgeTimer = Stopwatch.StartNew();
                    var forgeResult = await ExecuteStageAsync(
                        GenerationTimeoutStage.Forge,
                        stageCt => Forge.ForgeProjectAsync(request.Prompt, route.TemplateId, onProgress, stageCt),
                        pipelineCt);
                    forgeTimer.Stop();
                    if (!forgeResult.Success)
                    {
                        _metrics.RecordValidationFail();
                        if (HasCompileErrors(forgeResult.Errors))
                        {
                            _metrics.RecordCompileFail();
                        }
                    }

                    var promotedResult = await TryPromoteTemplateAsync(request, forgeResult, onProgress, pipelineCt);
                    await WriteGoldenRouteRunReportAsync(request, route, promotedResult, routingTimer.Elapsed, forgeTimer.Elapsed, pipelineCt);
                    return promotedResult;
                }

                var result = await ExecuteStageAsync(
                    GenerationTimeoutStage.Synthesis,
                    async stageCt =>
                    {
                        await _bcaster.BroadcastIntentAsync("Capture System State", "Ensure system is aware of current environment and platform constraints.", onProgress, stageCt);
                        var snapshot = await _observer.CaptureSnapshotAsync(request.OutputPath, stageCt);

                        await _bcaster.BroadcastIntentAsync("Design Architecture", $"Creating project structure for {snapshot.Platform.OS}", onProgress, stageCt);
                        var blueprint = await _blueprints.DesignBlueprintAsync(request.Prompt, snapshot.Platform.OS, stageCt);

                        onProgress?.Invoke(isRussian ? "⚖️ Валидация манифеста..." : "⚖️ Validating blueprint...");
                        var isValid = await _blueprints.ValidateBlueprintAsync(blueprint, stageCt);
                        if (!isValid)
                        {
                            throw new Exception("Architecture rejected by Shadow Roundtable.");
                        }

                        onProgress?.Invoke(isRussian ? "🎯 Анализ намерений..." : "🎯 Analyzing intent...");
                        var analysis = await _modelSelector.AnalyzeIntentAsync(request.Prompt, stageCt);
                        if (analysis.Intent == IntentType.Research)
                        {
                            return await _researchEngine.HandleResearchModeAsync(request, analysis, onProgress, stageCt);
                        }

                        return await _tumen.ForgeWithTumenAsync(
                            request.Prompt,
                            request.OutputPath,
                            onProgress,
                            stageCt,
                            skipTemplateRouting: true,
                            routeHint: route);
                    },
                    pipelineCt);

                if (result.Errors.Count > 0 && !smokeProfile)
                {
                    var fixRunId = string.IsNullOrWhiteSpace(request.SessionId)
                        ? $"autofix_{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}"
                        : request.SessionId!;

                    var fixLoop = await ExecuteStageAsync(
                        GenerationTimeoutStage.Autofix,
                        stageCt => _fixStrategyRunner.RunAsync(
                            fixRunId,
                            request,
                            result,
                            regenCt => _tumen.ForgeWithTumenAsync(
                                request.Prompt,
                                request.OutputPath,
                                onProgress,
                                regenCt,
                                skipTemplateRouting: true,
                                routeHint: route),
                            onProgress,
                            stageCt),
                        pipelineCt);
                    result = fixLoop.Result;
                }

                if (result.Errors.Count > 0)
                {
                    var enableMetacognitiveDebug = !smokeProfile && ReadFlag("HELPER_ENABLE_METACOGNITIVE_DEBUG", false);
                    if (enableMetacognitiveDebug)
                    {
                        await _bcaster.BroadcastIntentAsync("Metacognitive Debugging", "Standard healing failed. Analyzing Helper core source code for systemic issues.", onProgress, pipelineCt);
                        var selfFixed = await _metacognitive.DebugSelfAsync(result.Errors.First().Message, onProgress, pipelineCt);
                        if (selfFixed)
                        {
                            onProgress?.Invoke(isRussian ? "🧩 [System] Предложен системный патч. Требуется рестарт." : "🧩 [System] Systemic patch proposed. Restart required.");
                        }
                    }
                    else
                    {
                        onProgress?.Invoke(isRussian
                            ? "⏭️ [System] Метакогнитивная отладка отключена (HELPER_ENABLE_METACOGNITIVE_DEBUG=false)."
                            : "⏭️ [System] Metacognitive debugging skipped (HELPER_ENABLE_METACOGNITIVE_DEBUG=false).");
                    }

                    onProgress?.Invoke(isRussian ? "🧠 Проведение анализа ошибок (Self-Reflection)..." : "🧠 Conducting post-mortem (Self-Reflection)...");
                    var lesson = await _reflection.ConductPostMortemAsync(request.Prompt, result.Errors, result.Files.FirstOrDefault()?.Content ?? string.Empty, pipelineCt);
                    if (lesson != null)
                    {
                        await _reflection.IngestLessonAsync(lesson, pipelineCt);
                        onProgress?.Invoke(isRussian ? $"✅ Извлечен урок: {lesson.ErrorPattern}" : $"✅ Lesson learned: {lesson.ErrorPattern}");
                    }
                }
                else if (enableSuccessReflection)
                {
                    onProgress?.Invoke(isRussian ? "🌟 Анализ факторов успеха..." : "🌟 Analyzing success factors...");
                    var successPattern = await _reflection.ConductSuccessReviewAsync(request.Prompt, result.Files.FirstOrDefault()?.Content ?? string.Empty, pipelineCt);
                    if (successPattern != null)
                    {
                        await _reflection.IngestLessonAsync(successPattern, pipelineCt);
                        onProgress?.Invoke(isRussian ? $"🏆 Зафиксирован паттерн успеха: {successPattern.ErrorPattern}" : $"🏆 Success pattern recorded: {successPattern.ErrorPattern}");
                    }
                }
                else
                {
                    onProgress?.Invoke(isRussian
                        ? "⏭️ [System] Success reflection отключен (HELPER_ENABLE_SUCCESS_REFLECTION=false)."
                        : "⏭️ [System] Success reflection skipped (HELPER_ENABLE_SUCCESS_REFLECTION=false).");
                }

                if (result.Errors.Count > 0 && (result.FailureEnvelopes is null || result.FailureEnvelopes.Count == 0))
                {
                    var envelopes = _failureEnvelopeFactory.FromBuildErrors(FailureStage.Synthesis, "Helper", result.Errors);
                    result = result with { FailureEnvelopes = envelopes };
                }

                var finalResult = await TryPromoteTemplateAsync(request, result, onProgress, pipelineCt);
                RecordGenerationRouteTelemetry(
                    request,
                    route,
                    finalResult,
                    modelRoute: finalResult.IsResearch ? "research/research-engine" : "helper/synthesis",
                    degradationReason: finalResult.Success ? null : "generation_failed");
                return finalResult;
            }
            catch (GenerationStageTimeoutException ex)
            {
                _metrics.RecordTimeout(ex.TimeoutStage);
                var timeoutMessage = $"Generation timed out at stage {ex.TimeoutStage} ({ex.StageTimeout.TotalSeconds:0}s).";
                onProgress?.Invoke(isRussian
                    ? $"⏱️ Генерация прервана по таймауту этапа {ex.TimeoutStage} ({ex.StageTimeout.TotalSeconds:0}s)."
                    : $"⏱️ {timeoutMessage}");
                var timeoutError = new List<BuildError>
                {
                    new("Helper", 0, "GENERATION_STAGE_TIMEOUT", ex.Message)
                };
                var envelopes = _failureEnvelopeFactory.FromBuildErrors(MapFailureStage(ex.TimeoutStage), "Helper", timeoutError);
                await PersistTimeoutRunReportAsync(request, "GENERATION_STAGE_TIMEOUT", timeoutMessage, ex.TimeoutStage, sw.Elapsed, CancellationToken.None);
                return new GenerationResult(false, new List<GeneratedFile>(), request.OutputPath, timeoutError, sw.Elapsed, FailureEnvelopes: envelopes);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                _metrics.RecordTimeout(GenerationTimeoutStage.Unknown);
                var timeoutMessage = $"Generation exceeded global timeout of {createTimeoutSeconds} seconds.";
                onProgress?.Invoke(isRussian
                    ? $"⏱️ Генерация прервана по таймауту ({createTimeoutSeconds}s)."
                    : $"⏱️ Generation cancelled by timeout ({createTimeoutSeconds}s).");
                var timeoutError = new List<BuildError>
                {
                    new("Helper", 0, "GENERATION_TIMEOUT", timeoutMessage)
                };
                var envelopes = _failureEnvelopeFactory.FromBuildErrors(FailureStage.Unknown, "Helper", timeoutError);
                await PersistTimeoutRunReportAsync(request, "GENERATION_TIMEOUT", timeoutMessage, GenerationTimeoutStage.Unknown, sw.Elapsed, CancellationToken.None);
                return new GenerationResult(false, new List<GeneratedFile>(), request.OutputPath, timeoutError, sw.Elapsed, FailureEnvelopes: envelopes);
            }
        }

        private static bool ReadFlag(string envName, bool fallback)
        {
            var raw = Environment.GetEnvironmentVariable(envName);
            return bool.TryParse(raw, out var parsed) ? parsed : fallback;
        }

        private static int ReadInt(string envName, int fallback, int min, int max)
        {
            var raw = Environment.GetEnvironmentVariable(envName);
            if (!int.TryParse(raw, out var parsed))
            {
                parsed = fallback;
            }

            return Math.Clamp(parsed, min, max);
        }

        private static FailureStage MapFailureStage(GenerationTimeoutStage stage)
        {
            return stage switch
            {
                GenerationTimeoutStage.Routing => FailureStage.Routing,
                GenerationTimeoutStage.Forge => FailureStage.Forge,
                GenerationTimeoutStage.Synthesis => FailureStage.Synthesis,
                GenerationTimeoutStage.Autofix => FailureStage.Autofix,
                _ => FailureStage.Unknown
            };
        }

        private async Task<T> ExecuteStageAsync<T>(GenerationTimeoutStage stage, Func<CancellationToken, Task<T>> action, CancellationToken pipelineCt)
        {
            var stageTimeout = _stageTimeoutPolicy.Resolve(stage);
            using var stageCts = CancellationTokenSource.CreateLinkedTokenSource(pipelineCt);
            stageCts.CancelAfter(stageTimeout);

            try
            {
                return await action(stageCts.Token);
            }
            catch (OperationCanceledException) when (!pipelineCt.IsCancellationRequested && stageCts.IsCancellationRequested)
            {
                throw new GenerationStageTimeoutException(stage, stageTimeout);
            }
        }

        private async Task<GenerationResult> TryPromoteTemplateAsync(GenerationRequest request, GenerationResult result, Action<string>? onProgress, CancellationToken ct)
        {
            if (!result.Success)
            {
                return result;
            }

            var promotion = await _templatePromotionService.TryPromoteAsync(request, result, onProgress, ct);
            if (promotion.Attempted && !promotion.Success)
            {
                onProgress?.Invoke($"⚠️ [TemplatePromotion] {promotion.Message} {string.Join(" | ", promotion.Errors)}");
            }

            return result;
        }

        public Task<ResearchResult> ConductResearchAsync(string topic, int depth = 1, Action<string>? onProgress = null, CancellationToken ct = default)
            => _researchEngine.ConductResearchAsync(topic, depth, onProgress, ct);

        public Task<string> DeployProjectAsync(string projectPath, string platform, Action<string>? onProgress = null, CancellationToken ct = default)
            => Task.FromResult("Ready at " + projectPath);

        public Task RunPrometheusLoopAsync(CancellationToken ct, Func<string, Task>? onThought = null)
            => _maintenance.RunPrometheusLoopAsync(ct, onThought);

        public Task ConsolidateMemoryAsync(CancellationToken ct = default)
            => _maintenance.ConsolidateMemoryAsync(ct);
    }
}

