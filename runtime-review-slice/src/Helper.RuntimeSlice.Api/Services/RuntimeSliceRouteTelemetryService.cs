using System.Collections.Concurrent;
using Helper.RuntimeSlice.Contracts;

namespace Helper.RuntimeSlice.Api.Services;

public interface IRuntimeSliceRouteTelemetryService
{
    RouteTelemetrySnapshot GetSnapshot();
}

internal sealed class RuntimeSliceRouteTelemetryService : IRuntimeSliceRouteTelemetryService
{
    private const int MaxRecentEvents = 48;

    private readonly FixtureFileStore _fixtures;

    public RuntimeSliceRouteTelemetryService(RuntimeSliceOptions options)
    {
        _fixtures = new FixtureFileStore(options);
    }

    public RouteTelemetrySnapshot GetSnapshot()
    {
        var events = _fixtures.ReadJsonLines<RouteTelemetryEvent>("route_telemetry.jsonl")
            .Select(Normalize)
            .OrderByDescending(entry => entry.RecordedAtUtc)
            .ToArray();

        var channels = new ConcurrentDictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var operationKinds = new ConcurrentDictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var routes = new ConcurrentDictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var qualities = new ConcurrentDictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var modelRoutes = new ConcurrentDictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in events)
        {
            channels.AddOrUpdate(entry.Channel, 1, (_, current) => current + 1);
            operationKinds.AddOrUpdate(entry.OperationKind, 1, (_, current) => current + 1);
            routes.AddOrUpdate(entry.RouteKey, 1, (_, current) => current + 1);
            qualities.AddOrUpdate(entry.Quality, 1, (_, current) => current + 1);
            if (!string.IsNullOrWhiteSpace(entry.ModelRoute))
            {
                modelRoutes.AddOrUpdate(entry.ModelRoute, 1, (_, current) => current + 1);
            }
        }

        var recent = events.Take(MaxRecentEvents).ToArray();
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
            TotalEvents: events.LongLength,
            Channels: ToBuckets(channels),
            OperationKinds: ToBuckets(operationKinds),
            Routes: ToBuckets(routes),
            Qualities: ToBuckets(qualities),
            ModelRoutes: ToBuckets(modelRoutes),
            Recent: recent,
            Alerts: alerts);
    }

    private static RouteTelemetryEvent Normalize(RouteTelemetryEvent entry)
    {
        return entry with
        {
            Channel = NormalizeValue(entry.Channel, "unknown"),
            OperationKind = NormalizeValue(entry.OperationKind, "unknown"),
            RouteKey = NormalizeValue(entry.RouteKey, "unknown"),
            Quality = NormalizeValue(entry.Quality, RouteTelemetryQualities.Unknown),
            Outcome = NormalizeValue(entry.Outcome, RouteTelemetryOutcomes.Completed),
            ModelRoute = NormalizeNullable(entry.ModelRoute),
            CorrelationId = NormalizeNullable(entry.CorrelationId),
            IntentSource = NormalizeNullable(entry.IntentSource),
            ExecutionMode = NormalizeNullable(entry.ExecutionMode),
            BudgetProfile = NormalizeNullable(entry.BudgetProfile),
            WorkloadClass = NormalizeNullable(entry.WorkloadClass),
            DegradationReason = NormalizeNullable(entry.DegradationReason),
            Signals = entry.Signals?
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim().ToLowerInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };
    }

    private static string NormalizeValue(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim().ToLowerInvariant();
    }

    private static string? NormalizeNullable(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();
    }

    private static IReadOnlyList<RouteTelemetryBucket> ToBuckets(ConcurrentDictionary<string, long> dictionary)
    {
        return dictionary
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => new RouteTelemetryBucket(pair.Key, checked((int)Math.Min(int.MaxValue, pair.Value))))
            .ToArray();
    }
}
