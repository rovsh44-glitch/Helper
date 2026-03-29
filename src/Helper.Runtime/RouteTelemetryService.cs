using System.Collections.Concurrent;
using Helper.Runtime.Core;

namespace Helper.Runtime.Infrastructure;

public sealed class RouteTelemetryService : IRouteTelemetryService
{
    private const int MaxRecentEvents = 48;

    private readonly ConcurrentDictionary<string, long> _channels = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, long> _operationKinds = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, long> _routes = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, long> _qualities = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, long> _modelRoutes = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<RouteTelemetryEvent> _recent = new();
    private long _totalEvents;

    public void Record(RouteTelemetryEvent entry)
    {
        var normalized = entry with
        {
            Channel = Normalize(entry.Channel, "unknown"),
            OperationKind = Normalize(entry.OperationKind, "unknown"),
            RouteKey = Normalize(entry.RouteKey, "unknown"),
            Quality = Normalize(entry.Quality, RouteTelemetryQualities.Unknown),
            Outcome = Normalize(entry.Outcome, RouteTelemetryOutcomes.Completed),
            ModelRoute = NormalizeNullable(entry.ModelRoute),
            CorrelationId = NormalizeNullable(entry.CorrelationId),
            IntentSource = NormalizeNullable(entry.IntentSource),
            ExecutionMode = NormalizeNullable(entry.ExecutionMode),
            BudgetProfile = NormalizeNullable(entry.BudgetProfile),
            WorkloadClass = NormalizeNullable(entry.WorkloadClass),
            DegradationReason = NormalizeNullable(entry.DegradationReason),
            Signals = entry.Signals?
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Select(static value => value.Trim().ToLowerInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };

        Interlocked.Increment(ref _totalEvents);
        _channels.AddOrUpdate(normalized.Channel, 1, static (_, current) => current + 1);
        _operationKinds.AddOrUpdate(normalized.OperationKind, 1, static (_, current) => current + 1);
        _routes.AddOrUpdate(normalized.RouteKey, 1, static (_, current) => current + 1);
        _qualities.AddOrUpdate(normalized.Quality, 1, static (_, current) => current + 1);
        if (!string.IsNullOrWhiteSpace(normalized.ModelRoute))
        {
            _modelRoutes.AddOrUpdate(normalized.ModelRoute, 1, static (_, current) => current + 1);
        }

        _recent.Enqueue(normalized);
        while (_recent.Count > MaxRecentEvents && _recent.TryDequeue(out _))
        {
        }
    }

    public RouteTelemetrySnapshot GetSnapshot()
    {
        var recent = _recent
            .OrderByDescending(static entry => entry.RecordedAtUtc)
            .Take(MaxRecentEvents)
            .ToArray();

        var degradedOrFailed = recent.Count(entry =>
            string.Equals(entry.Quality, RouteTelemetryQualities.Degraded, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(entry.Quality, RouteTelemetryQualities.Failed, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(entry.Quality, RouteTelemetryQualities.Blocked, StringComparison.OrdinalIgnoreCase));

        var alerts = new List<string>();
        if (recent.Length > 0 && degradedOrFailed >= Math.Max(3, recent.Length / 2))
        {
            alerts.Add("Route telemetry shows sustained degraded or failed routing outcomes in recent events.");
        }

        return new RouteTelemetrySnapshot(
            SchemaVersion: 2,
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            TotalEvents: Volatile.Read(ref _totalEvents),
            Channels: ToBuckets(_channels),
            OperationKinds: ToBuckets(_operationKinds),
            Routes: ToBuckets(_routes),
            Qualities: ToBuckets(_qualities),
            ModelRoutes: ToBuckets(_modelRoutes),
            Recent: recent,
            Alerts: alerts);
    }

    private static IReadOnlyList<RouteTelemetryBucket> ToBuckets(ConcurrentDictionary<string, long> dictionary)
    {
        return dictionary
            .OrderByDescending(static pair => pair.Value)
            .ThenBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(static pair => new RouteTelemetryBucket(pair.Key, checked((int)Math.Min(int.MaxValue, pair.Value))))
            .ToArray();
    }

    private static string Normalize(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value)
            ? fallback
            : value.Trim().ToLowerInvariant();
    }

    private static string? NormalizeNullable(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();
    }
}

