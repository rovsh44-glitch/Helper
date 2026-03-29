using System.Collections.Concurrent;
using Microsoft.AspNetCore.Http;

namespace Helper.Api.Hosting;

public interface IRequestMetricsService
{
    void Record(string path, int statusCode, long elapsedMs);
    void Record(string path, int statusCode, long elapsedMs, bool isCanceled);
    MetricsSnapshot GetSnapshot();
}

public sealed class RequestMetricsService : IRequestMetricsService
{
    private readonly ConcurrentDictionary<string, EndpointMetric> _metrics = new(StringComparer.OrdinalIgnoreCase);

    public void Record(string path, int statusCode, long elapsedMs)
    {
        Record(path, statusCode, elapsedMs, isCanceled: false);
    }

    public void Record(string path, int statusCode, long elapsedMs, bool isCanceled)
    {
        var normalizedPath = string.IsNullOrWhiteSpace(path) ? "/" : path;
        var metric = _metrics.GetOrAdd(normalizedPath, _ => new EndpointMetric(normalizedPath));
        metric.Record(statusCode, elapsedMs, isCanceled);
    }

    public MetricsSnapshot GetSnapshot()
    {
        var endpoints = _metrics.Values
            .Select(m => m.ToSnapshot())
            .OrderByDescending(m => m.RequestCount)
            .ToList();

        var totalRequests = endpoints.Sum(x => x.RequestCount);
        var totalErrors = endpoints.Sum(x => x.ErrorCount);
        var totalCanceled = endpoints.Sum(x => x.CanceledCount);
        var totalTimeouts = endpoints.Sum(x => x.TimeoutCount);
        var total5xx = endpoints.Sum(x => x.ServerErrorCount);
        var errorRate = totalRequests == 0 ? 0 : (double)totalErrors / totalRequests;
        var p95Latency = endpoints.Count == 0 ? 0 : endpoints.Max(x => x.MaxLatencyMs);

        var alerts = new List<string>();
        if (errorRate > 0.05) alerts.Add("High error rate detected (>5%).");
        if (p95Latency > 2000) alerts.Add("High latency detected (>2000ms max endpoint latency).");

        return new MetricsSnapshot(totalRequests, totalErrors, totalCanceled, totalTimeouts, total5xx, errorRate, p95Latency, alerts, endpoints);
    }
}

public sealed record MetricsSnapshot(
    int TotalRequests,
    int TotalErrors,
    int TotalCanceled,
    int TotalTimeouts,
    int Total5xx,
    double ErrorRate,
    long PeakLatencyMs,
    IReadOnlyList<string> Alerts,
    IReadOnlyList<EndpointMetricSnapshot> Endpoints);

public sealed record EndpointMetricSnapshot(
    string Path,
    int RequestCount,
    int ErrorCount,
    int CanceledCount,
    int TimeoutCount,
    int ServerErrorCount,
    long MaxLatencyMs,
    double AvgLatencyMs,
    int LastStatusCode,
    DateTimeOffset LastSeenAt);

internal sealed class EndpointMetric
{
    private long _requestCount;
    private long _errorCount;
    private long _canceledCount;
    private long _timeoutCount;
    private long _serverErrorCount;
    private long _totalLatency;
    private long _maxLatency;
    private int _lastStatusCode;
    private DateTimeOffset _lastSeenAt;

    public EndpointMetric(string path)
    {
        Path = path;
    }

    public string Path { get; }

    public void Record(int statusCode, long elapsedMs, bool isCanceled)
    {
        Interlocked.Increment(ref _requestCount);
        if (isCanceled)
        {
            Interlocked.Increment(ref _canceledCount);
        }
        else if (IsTimeoutStatus(statusCode))
        {
            Interlocked.Increment(ref _timeoutCount);
        }
        else if (statusCode >= 500)
        {
            Interlocked.Increment(ref _serverErrorCount);
            Interlocked.Increment(ref _errorCount);
        }
        else if (statusCode >= 400)
        {
            Interlocked.Increment(ref _errorCount);
        }

        Interlocked.Add(ref _totalLatency, elapsedMs);
        InterlockedExtensions.Max(ref _maxLatency, elapsedMs);
        Volatile.Write(ref _lastStatusCode, statusCode);
        _lastSeenAt = DateTimeOffset.UtcNow;
    }

    public EndpointMetricSnapshot ToSnapshot()
    {
        var requests = (int)Volatile.Read(ref _requestCount);
        var errors = (int)Volatile.Read(ref _errorCount);
        var totalLatency = Volatile.Read(ref _totalLatency);
        var maxLatency = Volatile.Read(ref _maxLatency);
        var avgLatency = requests == 0 ? 0 : (double)totalLatency / requests;

        return new EndpointMetricSnapshot(
            Path,
            requests,
            errors,
            (int)Volatile.Read(ref _canceledCount),
            (int)Volatile.Read(ref _timeoutCount),
            (int)Volatile.Read(ref _serverErrorCount),
            maxLatency,
            avgLatency,
            Volatile.Read(ref _lastStatusCode),
            _lastSeenAt);
    }

    private static bool IsTimeoutStatus(int statusCode)
    {
        return statusCode is StatusCodes.Status408RequestTimeout or StatusCodes.Status499ClientClosedRequest or StatusCodes.Status504GatewayTimeout;
    }
}

internal static class InterlockedExtensions
{
    public static void Max(ref long target, long value)
    {
        long current;
        do
        {
            current = Volatile.Read(ref target);
            if (current >= value) return;
        } while (Interlocked.CompareExchange(ref target, value, current) != current);
    }
}

