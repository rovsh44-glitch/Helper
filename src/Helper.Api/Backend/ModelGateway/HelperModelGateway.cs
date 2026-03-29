using System.Diagnostics;
using Helper.Api.Backend.Configuration;
using Helper.Runtime.Infrastructure;

namespace Helper.Api.Backend.ModelGateway;

public sealed class HelperModelGateway : IModelGateway
{
    private readonly AILink _ai;
    private readonly IBackendOptionsCatalog _options;
    private readonly IModelGatewayTelemetry _telemetry;
    private readonly SemaphoreSlim _interactivePool;
    private readonly SemaphoreSlim _backgroundPool;
    private readonly SemaphoreSlim _maintenancePool;

    public HelperModelGateway(
        AILink ai,
        IBackendOptionsCatalog options,
        IModelGatewayTelemetry telemetry)
    {
        _ai = ai;
        _options = options;
        _telemetry = telemetry;
        _interactivePool = new SemaphoreSlim(options.ModelGateway.InteractiveConcurrency, options.ModelGateway.InteractiveConcurrency);
        _backgroundPool = new SemaphoreSlim(options.ModelGateway.BackgroundConcurrency, options.ModelGateway.BackgroundConcurrency);
        _maintenancePool = new SemaphoreSlim(options.ModelGateway.MaintenanceConcurrency, options.ModelGateway.MaintenanceConcurrency);
    }

    public async Task DiscoverAsync(CancellationToken ct)
    {
        await _ai.DiscoverModelsAsync(ct);
        _telemetry.RecordCatalogRefresh();
    }

    public IReadOnlyList<string> GetAvailableModelsSnapshot() => _ai.GetAvailableModelsSnapshot();

    public string GetCurrentModel() => _ai.GetCurrentModel();

    public string ResolveModel(HelperModelClass modelClass)
    {
        var category = ResolveCategory(modelClass);
        var preferred = ResolveConfiguredFallback(modelClass);
        if (!string.IsNullOrWhiteSpace(preferred))
        {
            return preferred!;
        }

        return _ai.GetBestModel(category);
    }

    public async Task WarmAsync(HelperModelClass modelClass, CancellationToken ct)
    {
        await _ai.PreloadModelAsync(ResolveCategory(modelClass), ct);
        _telemetry.RecordWarmup();
    }

    public async Task<string> AskAsync(ModelGatewayRequest request, CancellationToken ct)
    {
        var semaphore = ResolveSemaphore(request.Pool);
        await semaphore.WaitAsync(ct);
        using var requestScope = _telemetry.Begin(request.Pool);
        var startedAt = Stopwatch.StartNew();

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(ResolveTimeout(request.Pool));
            var targetModel = ResolveRequestModel(request);
            var response = await _ai.AskAsync(
                request.Prompt,
                timeoutCts.Token,
                overrideModel: targetModel,
                systemInstruction: request.SystemInstruction,
                keepAliveSeconds: request.KeepAliveSeconds);
            _telemetry.Record(request.Pool, startedAt.ElapsedMilliseconds, success: true);
            return response;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _telemetry.Record(request.Pool, startedAt.ElapsedMilliseconds, success: false, timeout: true);
            _telemetry.RecordAlert($"Model gateway timeout in pool '{request.Pool.ToString().ToLowerInvariant()}'.");
            var fallback = ResolveSafeFallback(request);
            if (!string.IsNullOrWhiteSpace(fallback) &&
                !string.Equals(fallback, ResolveRequestModel(request), StringComparison.OrdinalIgnoreCase))
            {
                return await _ai.AskAsync(
                    request.Prompt,
                    ct,
                    overrideModel: fallback,
                    systemInstruction: request.SystemInstruction,
                    keepAliveSeconds: request.KeepAliveSeconds);
            }

            throw;
        }
        catch (Exception ex)
        {
            _telemetry.Record(request.Pool, startedAt.ElapsedMilliseconds, success: false);
            _telemetry.RecordAlert($"Model gateway failure in pool '{request.Pool.ToString().ToLowerInvariant()}': {ex.Message}");
            throw;
        }
        finally
        {
            semaphore.Release();
        }
    }

    public async IAsyncEnumerable<ModelGatewayStreamChunk> StreamAsync(
        ModelGatewayRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var semaphore = ResolveSemaphore(request.Pool);
        await semaphore.WaitAsync(ct);
        using var requestScope = _telemetry.Begin(request.Pool);
        var startedAt = Stopwatch.StartNew();
        var streamStartedAt = DateTimeOffset.UtcNow;
        var success = false;
        var timeout = false;

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(ResolveTimeout(request.Pool));
            await foreach (var token in _ai.StreamAsync(
                               request.Prompt,
                               overrideModel: ResolveRequestModel(request),
                               systemInstruction: request.SystemInstruction,
                               keepAliveSeconds: request.KeepAliveSeconds,
                               ct: timeoutCts.Token)
                               .WithCancellation(timeoutCts.Token))
            {
                yield return new ModelGatewayStreamChunk(
                    token,
                    DateTimeOffset.UtcNow,
                    streamStartedAt);
            }

            success = true;
        }
        finally
        {
            if (!success && !ct.IsCancellationRequested)
            {
                timeout = true;
                _telemetry.RecordAlert($"Model gateway stream terminated before success in pool '{request.Pool.ToString().ToLowerInvariant()}'.");
            }

            _telemetry.Record(request.Pool, startedAt.ElapsedMilliseconds, success, timeout);
            semaphore.Release();
        }
    }

    public ModelGatewaySnapshot GetSnapshot()
    {
        return _telemetry.CreateSnapshot(_ai.GetAvailableModelsSnapshot(), _ai.GetCurrentModel());
    }

    private string ResolveRequestModel(ModelGatewayRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.PreferredModel))
        {
            return request.PreferredModel!;
        }

        return ResolveModel(request.ModelClass);
    }

    private string? ResolveSafeFallback(ModelGatewayRequest request)
    {
        if (!string.IsNullOrWhiteSpace(_options.ModelGateway.SafeFallbackModel))
        {
            return _options.ModelGateway.SafeFallbackModel;
        }

        return ResolveConfiguredFallback(request.ModelClass);
    }

    private string? ResolveConfiguredFallback(HelperModelClass modelClass)
    {
        return modelClass switch
        {
            HelperModelClass.Fast => _options.ModelGateway.FastFallbackModel,
            HelperModelClass.Reasoning => _options.ModelGateway.ReasoningFallbackModel,
            HelperModelClass.Critic => _options.ModelGateway.CriticFallbackModel,
            _ => null
        };
    }

    private TimeSpan ResolveTimeout(ModelExecutionPool pool)
    {
        var seconds = pool switch
        {
            ModelExecutionPool.Background => _options.ModelGateway.BackgroundTimeoutSeconds,
            ModelExecutionPool.Maintenance => _options.ModelGateway.MaintenanceTimeoutSeconds,
            _ => _options.ModelGateway.InteractiveTimeoutSeconds
        };
        return TimeSpan.FromSeconds(seconds);
    }

    private SemaphoreSlim ResolveSemaphore(ModelExecutionPool pool)
    {
        return pool switch
        {
            ModelExecutionPool.Background => _backgroundPool,
            ModelExecutionPool.Maintenance => _maintenancePool,
            _ => _interactivePool
        };
    }

    private static string ResolveCategory(HelperModelClass modelClass)
    {
        return modelClass switch
        {
            HelperModelClass.Fast => "fast",
            HelperModelClass.Coder => "coder",
            HelperModelClass.Vision => "vision",
            HelperModelClass.Critic => "critic",
            HelperModelClass.Background => "background",
            _ => "reasoning"
        };
    }
}

