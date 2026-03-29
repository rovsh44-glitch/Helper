using System.Collections.Concurrent;

namespace Helper.Api.Backend.ModelGateway;

public interface IModelGatewayTelemetry
{
    IDisposable Begin(ModelExecutionPool pool);
    void Record(ModelExecutionPool pool, long elapsedMs, bool success, bool timeout = false);
    void RecordCatalogRefresh();
    void RecordWarmup();
    void RecordAlert(string alert);
    ModelGatewaySnapshot CreateSnapshot(IReadOnlyList<string> availableModels, string currentModel);
}

public sealed class ModelGatewayTelemetry : IModelGatewayTelemetry
{
    private readonly ConcurrentDictionary<ModelExecutionPool, PoolAccumulator> _pools = new();
    private readonly ConcurrentQueue<string> _alerts = new();
    private long _lastCatalogRefreshUnixMs;
    private long _lastWarmupUnixMs;

    public IDisposable Begin(ModelExecutionPool pool)
    {
        var accumulator = _pools.GetOrAdd(pool, static _ => new PoolAccumulator());
        accumulator.IncrementInFlight();
        return new ReleaseScope(accumulator);
    }

    public void Record(ModelExecutionPool pool, long elapsedMs, bool success, bool timeout = false)
    {
        var accumulator = _pools.GetOrAdd(pool, static _ => new PoolAccumulator());
        accumulator.Record(Math.Max(0, elapsedMs), success, timeout);
    }

    public void RecordCatalogRefresh()
    {
        Interlocked.Exchange(ref _lastCatalogRefreshUnixMs, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    public void RecordWarmup()
    {
        Interlocked.Exchange(ref _lastWarmupUnixMs, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    public void RecordAlert(string alert)
    {
        if (string.IsNullOrWhiteSpace(alert))
        {
            return;
        }

        _alerts.Enqueue(alert.Trim());
        while (_alerts.Count > 20 && _alerts.TryDequeue(out _))
        {
        }
    }

    public ModelGatewaySnapshot CreateSnapshot(IReadOnlyList<string> availableModels, string currentModel)
    {
        var pools = Enum.GetValues<ModelExecutionPool>()
            .Select(pool => _pools.TryGetValue(pool, out var accumulator)
                ? accumulator.ToSnapshot(pool)
                : new ModelPoolSnapshot(pool.ToString().ToLowerInvariant(), 0, 0, 0, 0, 0))
            .ToList();

        return new ModelGatewaySnapshot(
            AvailableModels: availableModels.ToArray(),
            CurrentModel: currentModel,
            Pools: pools,
            LastCatalogRefreshAtUtc: ReadTimestamp(ref _lastCatalogRefreshUnixMs),
            LastWarmupAtUtc: ReadTimestamp(ref _lastWarmupUnixMs),
            Alerts: _alerts.ToArray());
    }

    private static DateTimeOffset? ReadTimestamp(ref long unixMs)
    {
        var value = Volatile.Read(ref unixMs);
        return value <= 0 ? null : DateTimeOffset.FromUnixTimeMilliseconds(value);
    }

    private sealed class PoolAccumulator
    {
        private long _inFlight;
        private long _totalCalls;
        private long _failedCalls;
        private long _timeoutCalls;
        private long _elapsedMsSum;

        public void IncrementInFlight()
        {
            Interlocked.Increment(ref _inFlight);
        }

        public void DecrementInFlight()
        {
            Interlocked.Decrement(ref _inFlight);
        }

        public void Record(long elapsedMs, bool success, bool timeout)
        {
            Interlocked.Increment(ref _totalCalls);
            Interlocked.Add(ref _elapsedMsSum, elapsedMs);
            if (!success)
            {
                Interlocked.Increment(ref _failedCalls);
            }

            if (timeout)
            {
                Interlocked.Increment(ref _timeoutCalls);
            }
        }

        public ModelPoolSnapshot ToSnapshot(ModelExecutionPool pool)
        {
            var totalCalls = Volatile.Read(ref _totalCalls);
            var avgLatency = totalCalls == 0
                ? 0
                : (double)Volatile.Read(ref _elapsedMsSum) / totalCalls;

            return new ModelPoolSnapshot(
                Pool: pool.ToString().ToLowerInvariant(),
                InFlight: (int)Math.Max(0, Volatile.Read(ref _inFlight)),
                TotalCalls: totalCalls,
                FailedCalls: Volatile.Read(ref _failedCalls),
                TimeoutCalls: Volatile.Read(ref _timeoutCalls),
                AvgLatencyMs: avgLatency);
        }
    }

    private sealed class ReleaseScope : IDisposable
    {
        private readonly PoolAccumulator _accumulator;
        private int _disposed;

        public ReleaseScope(PoolAccumulator accumulator)
        {
            _accumulator = accumulator;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            _accumulator.DecrementInFlight();
        }
    }
}

