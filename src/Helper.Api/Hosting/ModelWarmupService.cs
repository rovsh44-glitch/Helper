using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Helper.Api.Backend.Configuration;
using Helper.Api.Backend.ModelGateway;

namespace Helper.Api.Hosting;

public sealed class ModelWarmupService : BackgroundService
{
    private readonly IModelGateway _modelGateway;
    private readonly IBackendOptionsCatalog _options;
    private readonly ILogger<ModelWarmupService> _logger;
    private readonly IStartupReadinessService _readiness;
    private readonly IStartupTrafficMonitor _trafficMonitor;
    private readonly IReadOnlyList<string> _warmupCategories;
    private readonly string _warmupMode;
    private readonly bool _probeEnabled;
    private readonly int _probeTimeoutSec;
    private readonly int _probeWarnMs;

    public ModelWarmupService(
        IModelGateway modelGateway,
        IBackendOptionsCatalog options,
        IStartupReadinessService readiness,
        IStartupTrafficMonitor trafficMonitor,
        ILogger<ModelWarmupService> logger)
    {
        _modelGateway = modelGateway;
        _options = options;
        _readiness = readiness;
        _trafficMonitor = trafficMonitor;
        _logger = logger;
        _warmupMode = options.Warmup.Mode;
        _warmupCategories = ResolveWarmupCategories(options.Warmup.Mode, options.Warmup.Categories);
        _probeEnabled = options.Warmup.ProbeEnabled || string.Equals(_warmupMode, "full", StringComparison.OrdinalIgnoreCase);
        _probeTimeoutSec = options.Warmup.ProbeTimeoutSeconds;
        _probeWarnMs = options.Warmup.ProbeWarnMs;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(500, stoppingToken);
            await _modelGateway.DiscoverAsync(stoppingToken);
            _readiness.MarkDependenciesReady();
            _readiness.MarkMinimalReady("minimal_ready");
            var discovered = _modelGateway.GetAvailableModelsSnapshot();
            _logger.LogInformation(
                "Model catalog discovered. Count={Count}. Current={CurrentModel}. WarmupMode={WarmupMode}.",
                discovered.Count,
                _modelGateway.GetCurrentModel(),
                _warmupMode);

            if (_warmupCategories.Count == 0)
            {
                _readiness.MarkWarmReady();
                return;
            }

            var warmedModels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var primaryCategory = _warmupCategories[0];
            if (await TryWarmCategoryAsync(primaryCategory, warmedModels, stoppingToken))
            {
                _readiness.MarkMinimalReady(_warmupCategories.Count > 1 ? "warming_background" : "minimal_ready");
            }
            else
            {
                _readiness.MarkDegraded($"Primary model warmup failed for '{primaryCategory}'. Chat falls back to lazy model load.", true);
                _readiness.MarkMinimalReady("warming_background");
            }

            foreach (var category in _warmupCategories.Skip(1))
            {
                await _trafficMonitor.WaitForInteractiveIdleAsync(stoppingToken);
                await TryWarmCategoryAsync(category, warmedModels, stoppingToken);
            }

            if (_probeEnabled)
            {
                foreach (var model in warmedModels)
                {
                    await _trafficMonitor.WaitForInteractiveIdleAsync(stoppingToken);
                    await ProbeModelAsync(model, stoppingToken);
                }
            }

            _readiness.MarkWarmReady();
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
        catch (Exception ex)
        {
            _readiness.MarkDegraded($"Warmup bootstrap failed: {ex.Message}", false);
            _logger.LogError(ex, "Warmup bootstrap failed.");
        }
    }

    private async Task ProbeModelAsync(string model, CancellationToken ct)
    {
        using var probeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        probeCts.CancelAfter(TimeSpan.FromSeconds(_probeTimeoutSec));

        var sw = Stopwatch.StartNew();
        try
        {
            _ = await _modelGateway.AskAsync(
                new ModelGatewayRequest(
                    "Ready-check. Respond with: ready",
                    HelperModelClass.Fast,
                    ModelExecutionPool.Maintenance,
                    PreferredModel: model,
                    KeepAliveSeconds: 120),
                probeCts.Token);
            sw.Stop();

            if (sw.ElapsedMilliseconds > _probeWarnMs)
            {
                _readiness.MarkDegraded(
                    $"Model preflight latency above threshold for '{model}' ({sw.ElapsedMilliseconds} ms).",
                    true);
                _logger.LogWarning(
                    "Model preflight latency above threshold. Model={Model}. ElapsedMs={ElapsedMs}. WarnMs={WarnMs}.",
                    model,
                    sw.ElapsedMilliseconds,
                    _probeWarnMs);
                return;
            }

            _logger.LogInformation(
                "Model preflight passed. Model={Model}. ElapsedMs={ElapsedMs}.",
                model,
                sw.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _readiness.MarkDegraded($"Model preflight timeout for '{model}' after {_probeTimeoutSec} sec.", true);
            _logger.LogWarning(
                "Model preflight timeout. Model={Model}. TimeoutSec={TimeoutSec}.",
                model,
                _probeTimeoutSec);
        }
        catch (Exception ex)
        {
            _readiness.MarkDegraded($"Model preflight failed for '{model}': {ex.Message}", true);
            _logger.LogWarning(
                ex,
                "Model preflight failed. Model={Model}. Message={Message}.",
                model,
                ex.Message);
        }
    }

    private async Task<bool> TryWarmCategoryAsync(string category, HashSet<string> warmedModels, CancellationToken ct)
    {
        var model = ResolveWarmModel(category);
        var sw = Stopwatch.StartNew();
        try
        {
            await _modelGateway.WarmAsync(MapCategory(category), ct);
            sw.Stop();
            warmedModels.Add(model);
            _logger.LogInformation(
                "Model warmup completed for {Category}. Model={Model}. ElapsedMs={ElapsedMs}.",
                category,
                model,
                sw.ElapsedMilliseconds);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Model warmup skipped for {Category}. Message={Message}.",
                category,
                ex.Message);
            return false;
        }
    }

    private string ResolveWarmModel(string category)
    {
        return _modelGateway.ResolveModel(MapCategory(category));
    }

    private static HelperModelClass MapCategory(string category)
    {
        return category.Trim().ToLowerInvariant() switch
        {
            "fast" => HelperModelClass.Fast,
            "coder" => HelperModelClass.Coder,
            "vision" => HelperModelClass.Vision,
            "critic" => HelperModelClass.Critic,
            "background" => HelperModelClass.Background,
            _ => HelperModelClass.Reasoning
        };
    }

    private static IReadOnlyList<string> ResolveWarmupCategories(string warmupMode, IReadOnlyList<string> configured)
    {
        return warmupMode switch
        {
            "disabled" => Array.Empty<string>(),
            "minimal" => configured.Contains("fast", StringComparer.OrdinalIgnoreCase)
                ? new[] { "fast" }
                : new[] { configured[0] },
            _ => configured
        };
    }

    private static string ReadWarmupMode()
    {
        var raw = Environment.GetEnvironmentVariable("HELPER_MODEL_WARMUP_MODE");
        return raw?.Trim().ToLowerInvariant() switch
        {
            "disabled" => "disabled",
            "full" => "full",
            _ => "minimal"
        };
    }

    private static bool ReadBool(string envName, bool fallback)
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
}

