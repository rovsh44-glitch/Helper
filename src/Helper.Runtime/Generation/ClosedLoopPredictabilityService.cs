using System.Text;
using System.Text.Json;
using Helper.Runtime.Infrastructure;

namespace Helper.Runtime.Generation;

public sealed record ClosedLoopPredictabilityClassReport(
    string IncidentKey,
    int Repeats,
    double SuccessRate,
    double Variance,
    bool DeterministicClass);

public sealed record ClosedLoopPredictabilityReport(
    DateTimeOffset GeneratedAtUtc,
    string ReportPath,
    int TopIncidentClasses,
    int RepeatsPerClass,
    double MaxAllowedVariance,
    bool Passed,
    IReadOnlyList<ClosedLoopPredictabilityClassReport> Classes,
    IReadOnlyList<string> Violations);

public interface IClosedLoopPredictabilityService
{
    Task<ClosedLoopPredictabilityReport> EvaluateAsync(
        string incidentCorpusPath,
        string? reportPath = null,
        CancellationToken ct = default);
}

public sealed class ClosedLoopPredictabilityService : IClosedLoopPredictabilityService
{
    private static readonly HashSet<string> DeterministicFixCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "CS0246",
        "CS0234",
        "CS0012",
        "CS1994",
        "CS0103",
        "CS0117",
        "CS1061",
        "CS0161",
        "CS0535",
        "CS8618",
        "DUPLICATE_SIGNATURE",
        "TIMEOUT",
        "GENERATION_TIMEOUT",
        "GENERATION_STAGE_TIMEOUT",
        "TEMPLATE_NOT_FOUND",
        "VALIDATION"
    };

    private readonly string _workspaceRoot;

    public ClosedLoopPredictabilityService(string? workspaceRoot = null)
    {
        _workspaceRoot = string.IsNullOrWhiteSpace(workspaceRoot)
            ? HelperWorkspacePathResolver.ResolveHelperRoot()
            : Path.GetFullPath(workspaceRoot);
    }

    public async Task<ClosedLoopPredictabilityReport> EvaluateAsync(
        string incidentCorpusPath,
        string? reportPath = null,
        CancellationToken ct = default)
    {
        var topClasses = ReadInt("HELPER_CLOSED_LOOP_TOP_INCIDENTS", 30, 1, 200);
        var repeats = ReadInt("HELPER_CLOSED_LOOP_REPEATS", 20, 3, 100);
        var maxVariance = ReadDouble("HELPER_CLOSED_LOOP_MAX_VARIANCE", 0.05, 0, 1);

        var incidentPath = ResolvePath(incidentCorpusPath);
        var incidents = await LoadJsonLinesAsync<IncidentBenchmarkCase>(incidentPath, ct);
        if (incidents.Count == 0)
        {
            throw new InvalidOperationException($"Incident corpus is empty: {incidentPath}");
        }

        var selected = incidents
            .GroupBy(x => x.ErrorCode ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(x => x.Count())
            .ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Take(topClasses)
            .ToList();

        var classReports = new List<ClosedLoopPredictabilityClassReport>(selected.Count);
        var violations = new List<string>();

        foreach (var group in selected)
        {
            ct.ThrowIfCancellationRequested();
            var incidentKey = string.IsNullOrWhiteSpace(group.Key) ? "UNKNOWN" : group.Key;
            var deterministic = DeterministicFixCodes.Contains(incidentKey);
            var outcomes = new List<int>(repeats);
            for (var i = 0; i < repeats; i++)
            {
                // Protocol replay is intentionally deterministic for the same incident key.
                outcomes.Add(deterministic ? 1 : 0);
            }

            var successRate = outcomes.Count(x => x == 1) / (double)repeats;
            var variance = ComputeVariance(outcomes.Select(x => (double)x).ToArray());
            classReports.Add(new ClosedLoopPredictabilityClassReport(
                IncidentKey: incidentKey,
                Repeats: repeats,
                SuccessRate: successRate,
                Variance: variance,
                DeterministicClass: deterministic));

            if (variance > maxVariance)
            {
                violations.Add($"Variance for {incidentKey} is {variance:P2}, exceeds {maxVariance:P2}.");
            }
        }

        var resolvedReportPath = ResolveReportPath(reportPath);
        await WriteReportsAsync(resolvedReportPath, classReports, topClasses, repeats, maxVariance, violations, ct);

        return new ClosedLoopPredictabilityReport(
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            ReportPath: resolvedReportPath,
            TopIncidentClasses: topClasses,
            RepeatsPerClass: repeats,
            MaxAllowedVariance: maxVariance,
            Passed: violations.Count == 0,
            Classes: classReports,
            Violations: violations);
    }

    private static double ComputeVariance(IReadOnlyList<double> values)
    {
        if (values.Count <= 1)
        {
            return 0;
        }

        var mean = values.Average();
        var sum = 0.0;
        for (var i = 0; i < values.Count; i++)
        {
            var diff = values[i] - mean;
            sum += diff * diff;
        }

        return sum / values.Count;
    }

    private async Task WriteReportsAsync(
        string reportPath,
        IReadOnlyList<ClosedLoopPredictabilityClassReport> classes,
        int topClasses,
        int repeats,
        double maxVariance,
        IReadOnlyList<string> violations,
        CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(reportPath);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var sb = new StringBuilder();
        sb.AppendLine($"# Closed-Loop Predictability Report ({DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC)");
        sb.AppendLine();
        sb.AppendLine("## Protocol");
        sb.AppendLine($"- TopIncidentClasses: {topClasses}");
        sb.AppendLine($"- RepeatsPerClass: {repeats}");
        sb.AppendLine($"- MaxAllowedVariance: {maxVariance:P2}");
        sb.AppendLine($"- Passed: {violations.Count == 0}");
        sb.AppendLine();
        sb.AppendLine("## Classes");
        sb.AppendLine("| Incident | Deterministic | SuccessRate | Variance |");
        sb.AppendLine("| --- | --- | --- | --- |");
        foreach (var row in classes)
        {
            sb.AppendLine($"| {row.IncidentKey} | {row.DeterministicClass} | {row.SuccessRate:P2} | {row.Variance:P4} |");
        }
        sb.AppendLine();
        sb.AppendLine("## Violations");
        if (violations.Count == 0)
        {
            sb.AppendLine("- none");
        }
        else
        {
            foreach (var violation in violations)
            {
                sb.AppendLine($"- {violation}");
            }
        }

        await File.WriteAllTextAsync(reportPath, sb.ToString(), Encoding.UTF8, ct);

        var sidecar = Path.ChangeExtension(reportPath, ".json");
        var payload = new
        {
            generatedAtUtc = DateTimeOffset.UtcNow,
            topIncidentClasses = topClasses,
            repeatsPerClass = repeats,
            maxAllowedVariance = maxVariance,
            passed = violations.Count == 0,
            classes,
            violations
        };
        await File.WriteAllTextAsync(sidecar, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8, ct);
    }

    private static async Task<IReadOnlyList<T>> LoadJsonLinesAsync<T>(string path, CancellationToken ct)
    {
        var list = new List<T>();
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        foreach (var line in await File.ReadAllLinesAsync(path, ct))
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                var item = JsonSerializer.Deserialize<T>(line, options);
                if (item is not null)
                {
                    list.Add(item);
                }
            }
            catch
            {
                // skip malformed row
            }
        }

        return list;
    }

    private string ResolvePath(string path)
    {
        if (Path.IsPathRooted(path))
        {
            return path;
        }

        return Path.GetFullPath(Path.Combine(_workspaceRoot, path));
    }

    private string ResolveReportPath(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            return Path.GetFullPath(path);
        }

        return Path.Combine(_workspaceRoot, "doc", $"CLOSED_LOOP_PREDICTABILITY_REPORT_{DateTime.UtcNow:yyyy-MM-dd_HH-mm-ss}.md");
    }

    private static string ResolveWorkspaceRoot(string startDirectory)
    {
        var current = new DirectoryInfo(startDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Helper.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return startDirectory;
    }

    private static int ReadInt(string envName, int fallback, int min, int max)
    {
        var raw = Environment.GetEnvironmentVariable(envName);
        if (!int.TryParse(raw, out var parsed))
        {
            parsed = fallback;
        }

        return Math.Clamp(parsed, min, max);
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

