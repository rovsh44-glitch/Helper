using System.Globalization;

namespace Helper.Runtime.Generation;

internal sealed class ParityDailyBackfillMetricsCalculator
{
    public ParityDailyBackfillDayMetrics Compute(
        IReadOnlyList<ParityBackfillRunEntry> entries,
        int minGoldenAttempts,
        ParityGateThresholds thresholds)
    {
        var totalRuns = entries.Count;
        var successfulRuns = entries.LongCount(x => x.Outcome.Disposition == ParityRunDisposition.CleanSuccess);
        var failedRuns = totalRuns - successfulRuns;
        var successRate = totalRuns == 0 ? 0 : successfulRuns / (double)totalRuns;
        var hardFailures = entries.LongCount(x => x.Outcome.Disposition == ParityRunDisposition.Failure);
        var unknownFailures = entries.LongCount(x => x.Outcome.Disposition == ParityRunDisposition.Failure && ErrorsLookUnknown(x.Errors));
        var unknownErrorRate = hardFailures == 0 ? 0 : unknownFailures / (double)hardFailures;

        var durations = entries
            .Where(x => x.StartedAtUtc.HasValue && x.CompletedAtUtc.HasValue)
            .Select(x => (x.CompletedAtUtc!.Value - x.StartedAtUtc!.Value).TotalSeconds)
            .Where(x => x >= 0)
            .ToList();
        var p95ReadySeconds = ComputePercentile(durations, 0.95);

        var hasEligibilitySignal = entries.Any(x => x.GoldenTemplateEligible.HasValue);
        var goldenAttempts = entries.LongCount(x => x.GoldenTemplateEligible == true);
        var goldenHits = entries.LongCount(x => x.GoldenTemplateEligible == true && x.GoldenTemplateMatched == true);
        if (goldenAttempts == 0 && !hasEligibilitySignal)
        {
            var legacyAttempts = entries.LongCount(x => x.GoldenTemplateMatched.HasValue);
            if (legacyAttempts > 0)
            {
                goldenAttempts = legacyAttempts;
                goldenHits = entries.LongCount(x => x.GoldenTemplateMatched == true);
            }
        }

        var goldenSampleInsufficient = goldenAttempts < minGoldenAttempts;
        var goldenHitRate = goldenAttempts == 0 ? 0 : goldenHits / (double)goldenAttempts;
        var generatedAtUtc = entries.Count == 0
            ? DateTimeOffset.UtcNow
            : entries.Max(x => x.TimelineAnchorUtc);
        var toolSuccessRatio = successRate;
        var alerts = BuildAlerts(
            goldenHitRate,
            successRate,
            p95ReadySeconds,
            unknownErrorRate,
            toolSuccessRatio,
            goldenAttempts,
            minGoldenAttempts,
            goldenSampleInsufficient,
            thresholds);

        return new ParityDailyBackfillDayMetrics(
            Snapshot: new ParityDailySnapshot(
                DateUtc: DateOnly.FromDateTime(generatedAtUtc.UtcDateTime).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                GeneratedAtUtc: generatedAtUtc,
                TotalRuns: totalRuns,
                GoldenHitRate: goldenHitRate,
                GenerationSuccessRate: successRate,
                P95ReadySeconds: p95ReadySeconds,
                UnknownErrorRate: unknownErrorRate,
                ToolSuccessRatio: toolSuccessRatio,
                Alerts: alerts,
                GoldenAttempts: goldenAttempts,
                GoldenHits: goldenHits,
                MinGoldenAttemptsRequired: minGoldenAttempts,
                GoldenSampleInsufficient: goldenSampleInsufficient),
            TotalRuns: totalRuns,
            SuccessfulRuns: successfulRuns,
            FailedRuns: failedRuns);
    }

    private static List<string> BuildAlerts(
        double goldenHitRate,
        double successRate,
        double p95ReadySeconds,
        double unknownErrorRate,
        double toolSuccessRatio,
        long goldenAttempts,
        int minGoldenAttempts,
        bool goldenSampleInsufficient,
        ParityGateThresholds thresholds)
    {
        var alerts = new List<string>();
        if (goldenSampleInsufficient)
        {
            alerts.Add($"insufficient_golden_sample: {goldenAttempts} < {minGoldenAttempts}.");
        }
        else if (goldenHitRate < thresholds.MinGoldenHitRate)
        {
            alerts.Add($"Golden template hit rate below target: {goldenHitRate:P1} < {thresholds.MinGoldenHitRate:P0}.");
        }

        if (successRate < thresholds.MinGenerationSuccessRate)
        {
            alerts.Add($"Generation success rate below target: {successRate:P1} < {thresholds.MinGenerationSuccessRate:P0}.");
        }

        if (p95ReadySeconds > thresholds.MaxP95ReadySeconds)
        {
            alerts.Add($"P95 ready latency above target: {p95ReadySeconds:0.0}s > {thresholds.MaxP95ReadySeconds:0.#}s.");
        }

        if (unknownErrorRate > thresholds.MaxUnknownErrorRate)
        {
            alerts.Add($"Unknown error rate above target: {unknownErrorRate:P1} > {thresholds.MaxUnknownErrorRate:P0}.");
        }

        if (toolSuccessRatio < thresholds.MinToolSuccessRatio)
        {
            alerts.Add($"Tool success ratio below target: {toolSuccessRatio:P1} < {thresholds.MinToolSuccessRatio:P0}.");
        }

        return alerts;
    }

    private static bool ErrorsLookUnknown(IReadOnlyList<string> errors)
    {
        return errors.Count == 0 || errors.All(error =>
            error.Contains("UNKNOWN", StringComparison.OrdinalIgnoreCase) ||
            error.Contains("unexpected", StringComparison.OrdinalIgnoreCase));
    }

    private static double ComputePercentile(IReadOnlyList<double> values, double percentile)
    {
        if (values.Count == 0)
        {
            return 0;
        }

        var sorted = values.OrderBy(x => x).ToArray();
        var index = Math.Clamp((int)Math.Ceiling(sorted.Length * percentile) - 1, 0, sorted.Length - 1);
        return sorted[index];
    }
}

internal sealed record ParityDailyBackfillDayMetrics(
    ParityDailySnapshot Snapshot,
    long TotalRuns,
    long SuccessfulRuns,
    long FailedRuns);

