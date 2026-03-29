using System.Collections.Concurrent;

namespace Helper.Runtime.Infrastructure
{
public interface IToolAuditService
{
    void Record(ToolAuditEntry entry);
    ToolAuditSnapshot GetSnapshot();
}

public sealed record ToolAuditEntry(
    DateTimeOffset Timestamp,
    string ToolName,
    string Operation,
    bool Success,
    string? Error = null,
    string? Details = null,
    string Source = "unknown",
    string? CorrelationId = null,
    string? TurnId = null);

public sealed record ToolMetricSnapshot(
    string ToolName,
    int TotalCalls,
    int FailedCalls,
    DateTimeOffset LastSeenAt);

public sealed record ToolSourceMetricSnapshot(
    string Source,
    int TotalCalls,
    int FailedCalls,
    double SuccessRatio,
    DateTimeOffset LastSeenAt);

public sealed record ToolAuditSnapshot(
    int TotalCalls,
    int FailedCalls,
    double SuccessRatio,
    IReadOnlyList<ToolMetricSnapshot> Tools,
    IReadOnlyList<ToolSourceMetricSnapshot> Sources,
    IReadOnlyList<string> Alerts);

public sealed class ToolAuditService : IToolAuditService
{
    private readonly ConcurrentDictionary<string, ToolAuditCounter> _byTool = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ToolAuditCounter> _bySource = new(StringComparer.OrdinalIgnoreCase);

    public void Record(ToolAuditEntry entry)
    {
        var counter = _byTool.GetOrAdd(entry.ToolName, _ => new ToolAuditCounter(entry.ToolName));
        counter.Record(entry.Success, entry.Timestamp);

        var source = ResolveSource(entry);
        var sourceCounter = _bySource.GetOrAdd(source, _ => new ToolAuditCounter(source));
        sourceCounter.Record(entry.Success, entry.Timestamp);
    }

    public ToolAuditSnapshot GetSnapshot()
    {
        var tools = _byTool.Values
            .Select(x => x.ToSnapshot())
            .OrderByDescending(x => x.TotalCalls)
            .ToList();
        var sources = _bySource.Values
            .Select(x =>
            {
                var snapshot = x.ToSnapshot();
                var ratio = snapshot.TotalCalls == 0
                    ? 1.0
                    : (double)(snapshot.TotalCalls - snapshot.FailedCalls) / snapshot.TotalCalls;
                return new ToolSourceMetricSnapshot(
                    snapshot.ToolName,
                    snapshot.TotalCalls,
                    snapshot.FailedCalls,
                    ratio,
                    snapshot.LastSeenAt);
            })
            .OrderByDescending(x => x.TotalCalls)
            .ToList();

        var totalCalls = tools.Sum(x => x.TotalCalls);
        var failedCalls = tools.Sum(x => x.FailedCalls);
        var successRatio = totalCalls == 0 ? 1.0 : (double)(totalCalls - failedCalls) / totalCalls;
        var alerts = new List<string>();
            if (totalCalls > 0 && successRatio < 0.90)
        {
            alerts.Add("Tool-call success ratio is below 90%.");
        }

        var chatSource = sources.FirstOrDefault(x => string.Equals(x.Source, "chat_execute", StringComparison.OrdinalIgnoreCase));
        if (chatSource is not null && chatSource.TotalCalls >= 3 && chatSource.SuccessRatio < 0.90)
        {
            alerts.Add($"Tool-call success ratio for source '{chatSource.Source}' is below 90%.");
        }

        return new ToolAuditSnapshot(totalCalls, failedCalls, successRatio, tools, sources, alerts);
    }

    private static string ResolveSource(ToolAuditEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.Source) &&
            !string.Equals(entry.Source, "unknown", StringComparison.OrdinalIgnoreCase))
        {
            return entry.Source.Trim();
        }

        if (!string.IsNullOrWhiteSpace(entry.Operation))
        {
            return entry.Operation.Trim().ToLowerInvariant();
        }

        return "unknown";
    }
}

    internal sealed class ToolAuditCounter
    {
        private long _totalCalls;
        private long _failedCalls;
        private DateTimeOffset _lastSeenAt;

        public ToolAuditCounter(string toolName)
        {
            ToolName = toolName;
        }

        public string ToolName { get; }

        public void Record(bool success, DateTimeOffset seenAt)
        {
            Interlocked.Increment(ref _totalCalls);
            if (!success)
            {
                Interlocked.Increment(ref _failedCalls);
            }

            _lastSeenAt = seenAt;
        }

        public ToolMetricSnapshot ToSnapshot()
        {
            return new ToolMetricSnapshot(
                ToolName,
                (int)Volatile.Read(ref _totalCalls),
                (int)Volatile.Read(ref _failedCalls),
                _lastSeenAt);
        }
    }
}

