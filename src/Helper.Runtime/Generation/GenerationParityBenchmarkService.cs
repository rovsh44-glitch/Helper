using System.Text;
using System.Text.Json;
using Helper.Runtime.Core;
using Helper.Runtime.Infrastructure;

namespace Helper.Runtime.Generation;

public sealed record GoldenTemplateBenchmarkCase(
    string Id,
    string Prompt,
    string ExpectedTemplateId,
    string? Family = null);

public sealed record IncidentBenchmarkCase(
    string Id,
    string ErrorCode,
    string ErrorMessage,
    string Stage,
    string ExpectedRootCauseClass);

public sealed record GenerationParityBenchmarkReport(
    DateTimeOffset GeneratedAtUtc,
    string ReportPath,
    int GoldenCaseCount,
    int GoldenFamilyCount,
    double GoldenHitRate,
    int IncidentCaseCount,
    int IncidentErrorCodeCount,
    int IncidentRootCauseClassCount,
    double RootCausePrecision,
    double UnknownErrorRate,
    double DeterministicAutofixCoverageRate,
    bool Passed,
    IReadOnlyList<string> Violations);

public interface IGenerationParityBenchmarkService
{
    Task<GenerationParityBenchmarkReport> RunAsync(
        string goldenCorpusPath,
        string incidentCorpusPath,
        string? reportPath = null,
        CancellationToken ct = default);
}

public sealed class GenerationParityBenchmarkService : IGenerationParityBenchmarkService
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

    private readonly ITemplateRoutingService _templateRoutingService;
    private readonly IFailureEnvelopeFactory _failureEnvelopeFactory;
    private readonly string _workspaceRoot;

    public GenerationParityBenchmarkService(
        ITemplateRoutingService templateRoutingService,
        IFailureEnvelopeFactory failureEnvelopeFactory,
        string? workspaceRoot = null)
    {
        _templateRoutingService = templateRoutingService;
        _failureEnvelopeFactory = failureEnvelopeFactory;
        _workspaceRoot = string.IsNullOrWhiteSpace(workspaceRoot)
            ? HelperWorkspacePathResolver.ResolveHelperRoot()
            : Path.GetFullPath(workspaceRoot);
    }

    public async Task<GenerationParityBenchmarkReport> RunAsync(
        string goldenCorpusPath,
        string incidentCorpusPath,
        string? reportPath = null,
        CancellationToken ct = default)
    {
        var goldenPath = ResolveCorpusPath(goldenCorpusPath);
        var incidentPath = ResolveCorpusPath(incidentCorpusPath);
        var goldenCases = await LoadJsonLinesAsync<GoldenTemplateBenchmarkCase>(goldenPath, ct);
        var incidentCases = await LoadJsonLinesAsync<IncidentBenchmarkCase>(incidentPath, ct);

        if (goldenCases.Count == 0)
        {
            throw new InvalidOperationException($"Golden corpus is empty: {goldenPath}");
        }

        if (incidentCases.Count == 0)
        {
            throw new InvalidOperationException($"Incident corpus is empty: {incidentPath}");
        }

        var goldenHits = 0;
        var goldenFamilies = goldenCases
            .Select(x => string.IsNullOrWhiteSpace(x.Family) ? x.ExpectedTemplateId : x.Family!.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        foreach (var benchmarkCase in goldenCases)
        {
            ct.ThrowIfCancellationRequested();
            var route = await _templateRoutingService.RouteAsync(benchmarkCase.Prompt, ct);
            if (route.Matched &&
                string.Equals(route.TemplateId, benchmarkCase.ExpectedTemplateId, StringComparison.OrdinalIgnoreCase))
            {
                goldenHits++;
            }
        }

        var rootCauseMatches = 0;
        var unknownRootCauses = 0;
        var deterministicCoverage = 0;
        foreach (var incident in incidentCases)
        {
            ct.ThrowIfCancellationRequested();
            var stage = ParseStage(incident.Stage);
            var envelope = _failureEnvelopeFactory.FromBuildErrors(
                stage,
                subsystem: "benchmark",
                errors: new[]
                {
                    new BuildError("Benchmark", 0, incident.ErrorCode, incident.ErrorMessage)
                })[0];

            if (Enum.TryParse<RootCauseClass>(incident.ExpectedRootCauseClass, true, out var expectedClass) &&
                envelope.RootCauseClass == expectedClass)
            {
                rootCauseMatches++;
            }

            if (envelope.RootCauseClass == RootCauseClass.Unknown)
            {
                unknownRootCauses++;
            }

            if (DeterministicFixCodes.Contains(incident.ErrorCode))
            {
                deterministicCoverage++;
            }
        }

        var goldenHitRate = goldenHits / (double)goldenCases.Count;
        var incidentErrorCodeCount = incidentCases
            .Select(x => x.ErrorCode)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var incidentRootCauseClassCount = incidentCases
            .Select(x => x.ExpectedRootCauseClass)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var rootCausePrecision = rootCauseMatches / (double)incidentCases.Count;
        var unknownErrorRate = unknownRootCauses / (double)incidentCases.Count;
        var deterministicAutofixCoverageRate = deterministicCoverage / (double)incidentCases.Count;

        var minGoldenHitRate = ReadDouble("HELPER_BENCHMARK_MIN_GOLDEN_HIT_RATE", 0.90, 0, 1);
        var minGoldenFamilyCount = ReadInt("HELPER_BENCHMARK_MIN_GOLDEN_FAMILY_COUNT", 20, 1, 5000);
        var minRootCausePrecision = ReadDouble("HELPER_BENCHMARK_MIN_ROOT_CAUSE_PRECISION", 0.85, 0, 1);
        var minIncidentErrorCodeCount = ReadInt("HELPER_BENCHMARK_MIN_INCIDENT_ERROR_CODE_COUNT", 40, 1, 5000);
        var minIncidentRootCauseClassCount = ReadInt("HELPER_BENCHMARK_MIN_INCIDENT_ROOTCAUSE_CLASS_COUNT", 8, 1, 64);
        var maxUnknownErrorRate = ReadDouble("HELPER_BENCHMARK_MAX_UNKNOWN_ERROR_RATE", 0.05, 0, 1);
        var minAutofixCoverage = ReadDouble("HELPER_BENCHMARK_MIN_AUTOFIX_COVERAGE_RATE", 0.70, 0, 1);

        var violations = new List<string>();
        if (goldenHitRate < minGoldenHitRate)
        {
            violations.Add($"GoldenHitRate {goldenHitRate:P2} < {minGoldenHitRate:P2}");
        }

        if (goldenFamilies.Count < minGoldenFamilyCount)
        {
            violations.Add($"GoldenFamilyCount {goldenFamilies.Count} < {minGoldenFamilyCount}");
        }

        if (rootCausePrecision < minRootCausePrecision)
        {
            violations.Add($"RootCausePrecision {rootCausePrecision:P2} < {minRootCausePrecision:P2}");
        }

        if (incidentErrorCodeCount < minIncidentErrorCodeCount)
        {
            violations.Add($"IncidentErrorCodeCount {incidentErrorCodeCount} < {minIncidentErrorCodeCount}");
        }

        if (incidentRootCauseClassCount < minIncidentRootCauseClassCount)
        {
            violations.Add($"IncidentRootCauseClassCount {incidentRootCauseClassCount} < {minIncidentRootCauseClassCount}");
        }

        if (unknownErrorRate > maxUnknownErrorRate)
        {
            violations.Add($"UnknownErrorRate {unknownErrorRate:P2} > {maxUnknownErrorRate:P2}");
        }

        if (deterministicAutofixCoverageRate < minAutofixCoverage)
        {
            violations.Add($"DeterministicAutofixCoverageRate {deterministicAutofixCoverageRate:P2} < {minAutofixCoverage:P2}");
        }

        var resolvedReportPath = ResolveReportPath(reportPath);
        await WriteReportsAsync(
            resolvedReportPath,
            goldenCases.Count,
            goldenFamilies.Count,
            goldenHitRate,
            incidentCases.Count,
            incidentErrorCodeCount,
            incidentRootCauseClassCount,
            rootCausePrecision,
            unknownErrorRate,
            deterministicAutofixCoverageRate,
            violations,
            ct);

        return new GenerationParityBenchmarkReport(
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            ReportPath: resolvedReportPath,
            GoldenCaseCount: goldenCases.Count,
            GoldenFamilyCount: goldenFamilies.Count,
            GoldenHitRate: goldenHitRate,
            IncidentCaseCount: incidentCases.Count,
            IncidentErrorCodeCount: incidentErrorCodeCount,
            IncidentRootCauseClassCount: incidentRootCauseClassCount,
            RootCausePrecision: rootCausePrecision,
            UnknownErrorRate: unknownErrorRate,
            DeterministicAutofixCoverageRate: deterministicAutofixCoverageRate,
            Passed: violations.Count == 0,
            Violations: violations);
    }

    private static async Task<IReadOnlyList<T>> LoadJsonLinesAsync<T>(string path, CancellationToken ct)
    {
        var list = new List<T>();
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

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
                // malformed jsonl rows are ignored to keep benchmark robust
            }
        }

        return list;
    }

    private async Task WriteReportsAsync(
        string reportPath,
        int goldenCaseCount,
        int goldenFamilyCount,
        double goldenHitRate,
        int incidentCaseCount,
        int incidentErrorCodeCount,
        int incidentRootCauseClassCount,
        double rootCausePrecision,
        double unknownErrorRate,
        double deterministicAutofixCoverageRate,
        IReadOnlyList<string> violations,
        CancellationToken ct)
    {
        var directory = Path.GetDirectoryName(reportPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var sb = new StringBuilder();
        sb.AppendLine($"# Helper Generation Parity Benchmark ({DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC)");
        sb.AppendLine();
        sb.AppendLine("## KPI");
        sb.AppendLine($"- GoldenCaseCount: {goldenCaseCount}");
        sb.AppendLine($"- GoldenFamilyCount: {goldenFamilyCount}");
        sb.AppendLine($"- GoldenHitRate: {goldenHitRate:P2}");
        sb.AppendLine($"- IncidentCaseCount: {incidentCaseCount}");
        sb.AppendLine($"- IncidentErrorCodeCount: {incidentErrorCodeCount}");
        sb.AppendLine($"- IncidentRootCauseClassCount: {incidentRootCauseClassCount}");
        sb.AppendLine($"- RootCausePrecision: {rootCausePrecision:P2}");
        sb.AppendLine($"- UnknownErrorRate: {unknownErrorRate:P2}");
        sb.AppendLine($"- DeterministicAutofixCoverageRate: {deterministicAutofixCoverageRate:P2}");
        sb.AppendLine($"- Passed: {violations.Count == 0}");
        sb.AppendLine();
        sb.AppendLine("## Scope Note");
        sb.AppendLine("- This benchmark validates synthetic regression corpora only.");
        sb.AppendLine("- Production readiness must be decided by parity certification/window gates on real generation runs.");
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

        var sidecarPath = Path.ChangeExtension(reportPath, ".json");
        var payload = new
        {
            generatedAtUtc = DateTimeOffset.UtcNow,
            goldenCaseCount,
            goldenFamilyCount,
            goldenHitRate,
            incidentCaseCount,
            incidentErrorCodeCount,
            incidentRootCauseClassCount,
            rootCausePrecision,
            unknownErrorRate,
            deterministicAutofixCoverageRate,
            passed = violations.Count == 0,
            violations
        };
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(sidecarPath, json, Encoding.UTF8, ct);
    }

    private string ResolveCorpusPath(string path)
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

        return Path.Combine(_workspaceRoot, "doc", $"HELPER_GENERATION_PARITY_BENCHMARK_{DateTime.UtcNow:yyyy-MM-dd_HH-mm-ss}.md");
    }

    private static FailureStage ParseStage(string? raw)
    {
        if (Enum.TryParse<FailureStage>(raw, true, out var parsed))
        {
            return parsed;
        }

        return FailureStage.Unknown;
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

    private static double ReadDouble(string envName, double fallback, double min, double max)
    {
        var raw = Environment.GetEnvironmentVariable(envName);
        if (!double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
        {
            parsed = fallback;
        }

        return Math.Clamp(parsed, min, max);
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
}

