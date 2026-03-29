using System.Collections.Concurrent;

namespace Helper.Api.Hosting;

public sealed record ConversationStageBucketSnapshot(
    string Stage,
    int Count,
    double AvgLatencyMs,
    double P95LatencyMs,
    long MaxLatencyMs,
    int FailureCount);

public sealed record ConversationStageMetricsSnapshot(
    IReadOnlyList<ConversationStageBucketSnapshot> Stages,
    IReadOnlyList<string> Alerts);

public interface IConversationStageMetricsService
{
    void Record(string stage, long elapsedMs, bool success = true);
    ConversationStageMetricsSnapshot GetSnapshot();
}

public sealed class ConversationStageMetricsService : IConversationStageMetricsService
{
    private const int MaxSamplesPerStage = 256;

    private readonly ConcurrentDictionary<string, StageAccumulator> _stages = new(StringComparer.OrdinalIgnoreCase);

    public void Record(string stage, long elapsedMs, bool success = true)
    {
        if (string.IsNullOrWhiteSpace(stage))
        {
            return;
        }

        var accumulator = _stages.GetOrAdd(stage.Trim(), static _ => new StageAccumulator());
        accumulator.Record(Math.Max(0, elapsedMs), success);
    }

    public ConversationStageMetricsSnapshot GetSnapshot()
    {
        var snapshots = _stages
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => pair.Value.ToSnapshot(pair.Key))
            .ToList();

        var alerts = new List<string>();
        foreach (var snapshot in snapshots)
        {
            var threshold = ResolveThresholdMs(snapshot.Stage);
            if (threshold <= 0 || snapshot.Count < 5)
            {
                continue;
            }

            if (snapshot.P95LatencyMs > threshold)
            {
                alerts.Add($"Stage '{snapshot.Stage}' p95 latency is above budget ({snapshot.P95LatencyMs:0} ms > {threshold} ms).");
            }
        }

        return new ConversationStageMetricsSnapshot(snapshots, alerts);
    }

    private static int ResolveThresholdMs(string stage)
    {
        return stage.Trim().ToLowerInvariant() switch
        {
            "classify" => 350,
            "plan" => 600,
            "execute" => 2000,
            "critic" => 900,
            "finalizer" => 500,
            "persistence" => 400,
            "audit_enqueue" => 50,
            "audit_process" => 1500,
            _ => 0
        };
    }

    private sealed class StageAccumulator
    {
        private readonly object _sync = new();
        private readonly Queue<long> _samples = new();
        private long _sum;
        private long _max;
        private int _count;
        private int _failures;

        public void Record(long elapsedMs, bool success)
        {
            lock (_sync)
            {
                _count++;
                _sum += elapsedMs;
                _max = Math.Max(_max, elapsedMs);
                if (!success)
                {
                    _failures++;
                }

                _samples.Enqueue(elapsedMs);
                while (_samples.Count > MaxSamplesPerStage)
                {
                    _samples.Dequeue();
                }
            }
        }

        public ConversationStageBucketSnapshot ToSnapshot(string stage)
        {
            lock (_sync)
            {
                if (_count == 0)
                {
                    return new ConversationStageBucketSnapshot(stage, 0, 0, 0, 0, 0);
                }

                var ordered = _samples.OrderBy(x => x).ToArray();
                var p95Index = Math.Clamp((int)Math.Ceiling(ordered.Length * 0.95) - 1, 0, ordered.Length - 1);

                return new ConversationStageBucketSnapshot(
                    stage,
                    _count,
                    (double)_sum / _count,
                    ordered[p95Index],
                    _max,
                    _failures);
            }
        }
    }
}

