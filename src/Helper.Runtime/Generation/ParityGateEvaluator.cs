using Helper.Runtime.Core;

namespace Helper.Runtime.Generation;

public sealed record ParityGateThresholds(
    double MinGoldenHitRate = 0.90,
    double MinGenerationSuccessRate = 0.95,
    double MaxP95ReadySeconds = 25,
    double MaxUnknownErrorRate = 0.05,
    double MinToolSuccessRatio = 0.90)
{
    public static ParityGateThresholds FromEnvironment()
    {
        return new ParityGateThresholds(
            MinGoldenHitRate: ReadDouble("HELPER_PARITY_GATE_MIN_GOLDEN_HIT_RATE", 0.90, 0, 1),
            MinGenerationSuccessRate: ReadDouble("HELPER_PARITY_GATE_MIN_GENERATION_SUCCESS_RATE", 0.95, 0, 1),
            MaxP95ReadySeconds: ReadDouble("HELPER_PARITY_GATE_MAX_P95_READY_SECONDS", 25, 0, 3600),
            MaxUnknownErrorRate: ReadDouble("HELPER_PARITY_GATE_MAX_UNKNOWN_ERROR_RATE", 0.05, 0, 1),
            MinToolSuccessRatio: ReadDouble("HELPER_PARITY_GATE_MIN_TOOL_SUCCESS_RATIO", 0.90, 0, 1));
    }

    private static double ReadDouble(string envName, double fallback, double min, double max)
    {
        var raw = Environment.GetEnvironmentVariable(envName);
        if (!double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
        {
            parsed = fallback;
        }

        return Math.Clamp(parsed, min, max);
    }
}

public sealed record ParityGateDecision(
    bool Passed,
    IReadOnlyList<string> Violations,
    ParityCertificationReport Report,
    ParityGateThresholds Thresholds);

public interface IParityGateEvaluator
{
    ParityGateDecision Evaluate(ParityCertificationReport report, ParityGateThresholds thresholds);
}

public sealed class ParityGateEvaluator : IParityGateEvaluator
{
    public ParityGateDecision Evaluate(ParityCertificationReport report, ParityGateThresholds thresholds)
    {
        var violations = new List<string>();
        if (report.GoldenSampleInsufficient)
        {
            violations.Add(
                $"GoldenSampleInsufficient {report.GoldenAttempts} < {report.MinGoldenAttemptsRequired} (insufficient_golden_sample)");
        }
        else if (report.GoldenHitRate < thresholds.MinGoldenHitRate)
        {
            violations.Add($"GoldenHitRate {report.GoldenHitRate:P2} < {thresholds.MinGoldenHitRate:P2}");
        }

        if (report.GenerationSuccessRate < thresholds.MinGenerationSuccessRate)
        {
            violations.Add($"GenerationSuccessRate {report.GenerationSuccessRate:P2} < {thresholds.MinGenerationSuccessRate:P2}");
        }

        if (report.P95ReadySeconds > thresholds.MaxP95ReadySeconds)
        {
            violations.Add($"P95ReadySeconds {report.P95ReadySeconds:0.00}s > {thresholds.MaxP95ReadySeconds:0.00}s");
        }

        if (report.UnknownErrorRate > thresholds.MaxUnknownErrorRate)
        {
            violations.Add($"UnknownErrorRate {report.UnknownErrorRate:P2} > {thresholds.MaxUnknownErrorRate:P2}");
        }

        if (report.ToolSuccessRatio < thresholds.MinToolSuccessRatio)
        {
            violations.Add($"ToolSuccessRatio {report.ToolSuccessRatio:P2} < {thresholds.MinToolSuccessRatio:P2}");
        }

        if (ReadFlag("HELPER_PARITY_GATE_ENFORCE_REPORT_ALERTS", false) && report.Alerts.Count > 0)
        {
            violations.Add($"Certification report contains alerts: {string.Join("; ", report.Alerts)}");
        }

        return new ParityGateDecision(
            Passed: violations.Count == 0,
            Violations: violations,
            Report: report,
            Thresholds: thresholds);
    }

    private static bool ReadFlag(string envName, bool fallback)
    {
        var raw = Environment.GetEnvironmentVariable(envName);
        return bool.TryParse(raw, out var parsed) ? parsed : fallback;
    }
}

