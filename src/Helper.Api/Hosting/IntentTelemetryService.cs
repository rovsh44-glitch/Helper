using System.Collections.Concurrent;
using Helper.Api.Conversation;

namespace Helper.Api.Hosting;

public interface IIntentTelemetryService
{
    void Record(IntentClassification classification);
    IntentTelemetrySnapshot GetSnapshot();
}

public sealed record IntentTelemetryBucket(string Name, int Count);

public sealed record IntentTelemetrySnapshot(
    int TotalClassifications,
    double AvgConfidence,
    double LowConfidenceRate,
    IReadOnlyList<IntentTelemetryBucket> Sources,
    IReadOnlyList<IntentTelemetryBucket> Intents,
    IReadOnlyList<string> Alerts);

public sealed class IntentTelemetryService : IIntentTelemetryService
{
    private readonly ConcurrentDictionary<string, long> _sourceCounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, long> _intentCounts = new(StringComparer.OrdinalIgnoreCase);
    private long _total;
    private long _lowConfidence;
    private long _confidenceMilliSum;

    public void Record(IntentClassification classification)
    {
        Interlocked.Increment(ref _total);
        var confidenceMilli = (long)(Math.Clamp(classification.Confidence, 0, 1) * 1000);
        Interlocked.Add(ref _confidenceMilliSum, confidenceMilli);

        if (classification.Confidence < 0.5)
        {
            Interlocked.Increment(ref _lowConfidence);
        }

        _sourceCounts.AddOrUpdate(
            Normalize(classification.Source, "unknown"),
            1,
            (_, current) => current + 1);

        _intentCounts.AddOrUpdate(
            Normalize(classification.Analysis.Intent.ToString(), "Unknown"),
            1,
            (_, current) => current + 1);
    }

    public IntentTelemetrySnapshot GetSnapshot()
    {
        var total = (int)Volatile.Read(ref _total);
        if (total == 0)
        {
            return new IntentTelemetrySnapshot(
                0,
                0,
                0,
                Array.Empty<IntentTelemetryBucket>(),
                Array.Empty<IntentTelemetryBucket>(),
                Array.Empty<string>());
        }

        var avgConfidence = (double)Volatile.Read(ref _confidenceMilliSum) / (total * 1000);
        var lowConfidenceRate = (double)Volatile.Read(ref _lowConfidence) / total;

        var sources = _sourceCounts
            .OrderByDescending(x => x.Value)
            .Select(x => new IntentTelemetryBucket(x.Key, (int)x.Value))
            .ToList();

        var intents = _intentCounts
            .OrderByDescending(x => x.Value)
            .Select(x => new IntentTelemetryBucket(x.Key, (int)x.Value))
            .ToList();

        var alerts = new List<string>();
        if (lowConfidenceRate > 0.25)
        {
            alerts.Add("Intent low-confidence rate is above 25%.");
        }

        return new IntentTelemetrySnapshot(
            total,
            avgConfidence,
            lowConfidenceRate,
            sources,
            intents,
            alerts);
    }

    private static string Normalize(string? input, string fallback)
    {
        var value = string.IsNullOrWhiteSpace(input) ? fallback : input.Trim();
        return value.ToLowerInvariant();
    }
}

