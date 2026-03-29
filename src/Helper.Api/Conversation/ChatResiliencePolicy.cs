using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Helper.Api.Hosting;
using Microsoft.Extensions.Logging;

namespace Helper.Api.Conversation;

public interface IChatResiliencePolicy
{
    Task<T> ExecuteAsync<T>(string operationName, Func<CancellationToken, Task<T>> action, CancellationToken ct);
    IAsyncEnumerable<string> ExecuteStreamingAsync(
        string operationName,
        Func<CancellationToken, IAsyncEnumerable<string>> streamFactory,
        CancellationToken ct);
}

public sealed class ChatResiliencePolicy : IChatResiliencePolicy
{
    private readonly ILogger<ChatResiliencePolicy> _logger;
    private readonly IChatResilienceTelemetryService _telemetry;
    private readonly ConcurrentDictionary<string, CircuitState> _circuits = new(StringComparer.OrdinalIgnoreCase);
    private readonly int _maxRetries;
    private readonly TimeSpan _baseDelay;
    private readonly int _failureThreshold;
    private readonly TimeSpan _openDuration;

    public ChatResiliencePolicy(
        ILogger<ChatResiliencePolicy> logger,
        IChatResilienceTelemetryService telemetry)
    {
        _logger = logger;
        _telemetry = telemetry;
        _maxRetries = ReadInt("HELPER_CHAT_RETRY_COUNT", 2, min: 0, max: 6);
        _baseDelay = TimeSpan.FromMilliseconds(ReadInt("HELPER_CHAT_RETRY_BASE_DELAY_MS", 200, min: 50, max: 3000));
        _failureThreshold = ReadInt("HELPER_CHAT_CIRCUIT_FAILURE_THRESHOLD", 4, min: 2, max: 20);
        _openDuration = TimeSpan.FromSeconds(ReadInt("HELPER_CHAT_CIRCUIT_OPEN_SECONDS", 20, min: 5, max: 300));
    }

    public async Task<T> ExecuteAsync<T>(string operationName, Func<CancellationToken, Task<T>> action, CancellationToken ct)
    {
        var normalizedOperation = string.IsNullOrWhiteSpace(operationName) ? "unknown" : operationName.Trim().ToLowerInvariant();
        var circuit = _circuits.GetOrAdd(normalizedOperation, _ => new CircuitState());
        var maxAttempts = _maxRetries + 1;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var now = DateTimeOffset.UtcNow;
            if (circuit.TryGetOpenUntil(now, out var openUntil))
            {
                _telemetry.RecordFailure(normalizedOperation, openedCircuit: false);
                throw new InvalidOperationException(
                    $"Circuit is open for '{normalizedOperation}' until {openUntil:O}.");
            }

            _telemetry.RecordAttempt(normalizedOperation);
            try
            {
                var result = await action(ct);
                circuit.RecordSuccess();
                _telemetry.RecordSuccess(normalizedOperation);
                return result;
            }
            catch (Exception ex)
            {
                var isTransient = IsTransient(ex);
                var opened = circuit.RecordFailure(DateTimeOffset.UtcNow, _failureThreshold, _openDuration, out var openedUntil);
                _telemetry.RecordFailure(normalizedOperation, opened);

                if (opened)
                {
                    _logger.LogWarning(
                        ex,
                        "Circuit opened for {Operation}. OpenUntil={OpenUntil}.",
                        normalizedOperation,
                        openedUntil);
                }

                if (!isTransient || attempt >= maxAttempts)
                {
                    throw;
                }

                _telemetry.RecordRetry(normalizedOperation);
                var delay = ComputeDelay(attempt);
                _logger.LogWarning(
                    ex,
                    "Transient failure during {Operation}. Attempt {Attempt}/{MaxAttempts}. Retrying after {DelayMs}ms.",
                    normalizedOperation,
                    attempt,
                    maxAttempts,
                    (int)delay.TotalMilliseconds);

                await Task.Delay(delay, ct);
            }
        }

        throw new InvalidOperationException($"Resilience policy reached an unexpected state for operation '{normalizedOperation}'.");
    }

    public async IAsyncEnumerable<string> ExecuteStreamingAsync(
        string operationName,
        Func<CancellationToken, IAsyncEnumerable<string>> streamFactory,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var normalizedOperation = string.IsNullOrWhiteSpace(operationName) ? "unknown" : operationName.Trim().ToLowerInvariant();
        var circuit = _circuits.GetOrAdd(normalizedOperation, _ => new CircuitState());
        var maxAttempts = _maxRetries + 1;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var now = DateTimeOffset.UtcNow;
            if (circuit.TryGetOpenUntil(now, out var openUntil))
            {
                _telemetry.RecordFailure(normalizedOperation, openedCircuit: false);
                throw new InvalidOperationException(
                    $"Circuit is open for '{normalizedOperation}' until {openUntil:O}.");
            }

            _telemetry.RecordAttempt(normalizedOperation);
            var producedAnyToken = false;
            var shouldRetry = false;

            await using var enumerator = streamFactory(ct).GetAsyncEnumerator(ct);
            while (true)
            {
                string? token = null;
                var hasNext = false;
                try
                {
                    hasNext = await enumerator.MoveNextAsync();
                    if (hasNext)
                    {
                        token = enumerator.Current;
                    }
                }
                catch (Exception ex)
                {
                    var isTransient = IsTransient(ex);
                    var opened = circuit.RecordFailure(DateTimeOffset.UtcNow, _failureThreshold, _openDuration, out var openedUntil);
                    _telemetry.RecordFailure(normalizedOperation, opened);

                    if (opened)
                    {
                        _logger.LogWarning(
                            ex,
                            "Circuit opened for streaming {Operation}. OpenUntil={OpenUntil}.",
                            normalizedOperation,
                            openedUntil);
                    }

                    if (producedAnyToken || !isTransient || attempt >= maxAttempts)
                    {
                        throw;
                    }

                    _telemetry.RecordRetry(normalizedOperation);
                    var delay = ComputeDelay(attempt);
                    _logger.LogWarning(
                        ex,
                        "Transient streaming failure during {Operation}. Attempt {Attempt}/{MaxAttempts}. Retrying after {DelayMs}ms.",
                        normalizedOperation,
                        attempt,
                        maxAttempts,
                        (int)delay.TotalMilliseconds);

                    await Task.Delay(delay, ct);
                    shouldRetry = true;
                    break;
                }

                if (!hasNext)
                {
                    circuit.RecordSuccess();
                    _telemetry.RecordSuccess(normalizedOperation);
                    yield break;
                }

                if (string.IsNullOrEmpty(token))
                {
                    continue;
                }

                producedAnyToken = true;
                yield return token;
            }

            if (shouldRetry)
            {
                continue;
            }
        }

        throw new InvalidOperationException($"Streaming resilience policy reached an unexpected state for operation '{normalizedOperation}'.");
    }

    private static bool IsTransient(Exception ex)
    {
        return ex is HttpRequestException
            or TimeoutException
            or TaskCanceledException
            or IOException;
    }

    private TimeSpan ComputeDelay(int attempt)
    {
        var multiplier = Math.Pow(2, Math.Max(0, attempt - 1));
        var jitter = 0.75 + Random.Shared.NextDouble() * 0.5;
        var delayMs = _baseDelay.TotalMilliseconds * multiplier * jitter;
        return TimeSpan.FromMilliseconds(Math.Min(delayMs, 5000));
    }

    private static int ReadInt(string envName, int fallback, int min, int max)
    {
        var raw = Environment.GetEnvironmentVariable(envName);
        if (!int.TryParse(raw, out var parsed))
        {
            return fallback;
        }

        return Math.Clamp(parsed, min, max);
    }

    private sealed class CircuitState
    {
        private readonly object _sync = new();
        private int _consecutiveFailures;
        private DateTimeOffset _openUntil = DateTimeOffset.MinValue;

        public bool TryGetOpenUntil(DateTimeOffset now, out DateTimeOffset openUntil)
        {
            lock (_sync)
            {
                openUntil = _openUntil;
                return now < _openUntil;
            }
        }

        public bool RecordFailure(DateTimeOffset now, int failureThreshold, TimeSpan openDuration, out DateTimeOffset openUntil)
        {
            lock (_sync)
            {
                _consecutiveFailures++;
                if (_consecutiveFailures < failureThreshold)
                {
                    openUntil = _openUntil;
                    return false;
                }

                _openUntil = now.Add(openDuration);
                _consecutiveFailures = 0;
                openUntil = _openUntil;
                return true;
            }
        }

        public void RecordSuccess()
        {
            lock (_sync)
            {
                _consecutiveFailures = 0;
                _openUntil = DateTimeOffset.MinValue;
            }
        }
    }
}

