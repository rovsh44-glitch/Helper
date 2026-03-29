using System.Collections.Concurrent;

namespace Helper.Api.Hosting;

public interface IChatResilienceTelemetryService
{
    void RecordAttempt(string operation);
    void RecordSuccess(string operation);
    void RecordRetry(string operation);
    void RecordFailure(string operation, bool openedCircuit);
    void RecordFallback(string reason);
    ChatResilienceSnapshot GetSnapshot();
}

public sealed record ChatResilienceOperationSnapshot(
    string Operation,
    int Attempts,
    int Successes,
    int Retries,
    int Failures,
    int CircuitOpenEvents);

public sealed record ChatResilienceSnapshot(
    int TotalAttempts,
    int TotalSuccesses,
    int TotalRetries,
    int TotalFailures,
    int TotalCircuitOpenEvents,
    int TotalFallbacks,
    double RetryRate,
    IReadOnlyList<ChatResilienceOperationSnapshot> Operations,
    IReadOnlyList<string> Alerts);

public sealed class ChatResilienceTelemetryService : IChatResilienceTelemetryService
{
    private readonly ConcurrentDictionary<string, ResilienceOperationCounter> _operations = new(StringComparer.OrdinalIgnoreCase);
    private long _fallbacks;

    public void RecordAttempt(string operation)
    {
        GetCounter(operation).IncrementAttempts();
    }

    public void RecordSuccess(string operation)
    {
        GetCounter(operation).IncrementSuccesses();
    }

    public void RecordRetry(string operation)
    {
        GetCounter(operation).IncrementRetries();
    }

    public void RecordFailure(string operation, bool openedCircuit)
    {
        var counter = GetCounter(operation);
        counter.IncrementFailures();
        if (openedCircuit)
        {
            counter.IncrementCircuitOpenEvents();
        }
    }

    public void RecordFallback(string reason)
    {
        Interlocked.Increment(ref _fallbacks);
    }

    public ChatResilienceSnapshot GetSnapshot()
    {
        var operations = _operations.Values
            .Select(x => x.ToSnapshot())
            .OrderByDescending(x => x.Attempts)
            .ThenBy(x => x.Operation, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var totalAttempts = operations.Sum(x => x.Attempts);
        var totalSuccesses = operations.Sum(x => x.Successes);
        var totalRetries = operations.Sum(x => x.Retries);
        var totalFailures = operations.Sum(x => x.Failures);
        var totalCircuitOpen = operations.Sum(x => x.CircuitOpenEvents);
        var totalFallbacks = (int)Volatile.Read(ref _fallbacks);
        var retryRate = totalAttempts == 0 ? 0.0 : (double)totalRetries / totalAttempts;

        var alerts = new List<string>();
        if (totalCircuitOpen > 0)
        {
            alerts.Add("Circuit breaker opened for chat operations.");
        }
        if (retryRate > 0.15)
        {
            alerts.Add("Retry rate above 15% for chat operations.");
        }
        if (totalFallbacks > 0)
        {
            alerts.Add("Fail-open fallback was used for at least one turn.");
        }

        return new ChatResilienceSnapshot(
            totalAttempts,
            totalSuccesses,
            totalRetries,
            totalFailures,
            totalCircuitOpen,
            totalFallbacks,
            retryRate,
            operations,
            alerts);
    }

    private ResilienceOperationCounter GetCounter(string operation)
    {
        var normalized = string.IsNullOrWhiteSpace(operation) ? "unknown" : operation.Trim().ToLowerInvariant();
        return _operations.GetOrAdd(normalized, key => new ResilienceOperationCounter(key));
    }

    private sealed class ResilienceOperationCounter
    {
        private long _attempts;
        private long _successes;
        private long _retries;
        private long _failures;
        private long _circuitOpenEvents;

        public ResilienceOperationCounter(string operation)
        {
            Operation = operation;
        }

        public string Operation { get; }

        public void IncrementAttempts() => Interlocked.Increment(ref _attempts);
        public void IncrementSuccesses() => Interlocked.Increment(ref _successes);
        public void IncrementRetries() => Interlocked.Increment(ref _retries);
        public void IncrementFailures() => Interlocked.Increment(ref _failures);
        public void IncrementCircuitOpenEvents() => Interlocked.Increment(ref _circuitOpenEvents);

        public ChatResilienceOperationSnapshot ToSnapshot()
        {
            return new ChatResilienceOperationSnapshot(
                Operation,
                (int)Volatile.Read(ref _attempts),
                (int)Volatile.Read(ref _successes),
                (int)Volatile.Read(ref _retries),
                (int)Volatile.Read(ref _failures),
                (int)Volatile.Read(ref _circuitOpenEvents));
        }
    }
}

