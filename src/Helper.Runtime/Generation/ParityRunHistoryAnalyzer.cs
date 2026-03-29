using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Helper.Runtime.Generation;

internal static class ParityRunHistoryAnalyzer
{
    private static readonly Regex ErrorCodeRegex = new(
        @"\b(?:CS\d{4}|DUPLICATE_SIGNATURE|GENERATION_STAGE_TIMEOUT|GENERATION_TIMEOUT|TIMEOUT|VALIDATION|BLUEPRINT_CONTRACT_FAIL|FORMAT|PROJECT_TYPE_UNSUPPORTED)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    internal static async Task<ParityRunHistorySnapshot> AnalyzeAsync(
        string workspaceRoot,
        int lookbackHours,
        GenerationMetricsSnapshot generationSnapshot,
        GenerationArtifactDiscoveryMode discoveryMode,
        CancellationToken ct)
        => await AnalyzeAsync(
            GenerationArtifactDiscoveryOptions.Resolve(workspaceRoot, discoveryMode),
            lookbackHours,
            generationSnapshot,
            ct);

    internal static async Task<ParityRunHistorySnapshot> AnalyzeAsync(
        GenerationArtifactDiscoveryOptions discoveryOptions,
        int lookbackHours,
        GenerationMetricsSnapshot generationSnapshot,
        CancellationToken ct)
    {
        var entries = await ReadEntriesAsync(discoveryOptions, ct);
        var now = DateTimeOffset.UtcNow;
        var windowStart = now.AddHours(-lookbackHours);
        var windowAllWorkloads = entries
            .Where(entry => ResolveTimelineAnchor(entry) >= windowStart)
            .ToList();
        var workloadFilters = ReadWorkloadFilter("HELPER_PARITY_WORKLOAD_CLASSES");
        var window = windowAllWorkloads
            .Where(entry => MatchesWorkload(entry.WorkloadClass, workloadFilters))
            .ToList();

        var stats = ComputeStats(window);
        var minGoldenAttempts = ReadInt("HELPER_PARITY_MIN_GOLDEN_ATTEMPTS", 20, 1, 10_000);
        var hasEligibilitySignal = window.Any(x => x.GoldenTemplateEligible.HasValue);
        var persistedEligibleAttempts = window.Count(x => x.GoldenTemplateEligible == true);
        var persistedEligibleHits = window.Count(x => x.GoldenTemplateEligible == true && x.GoldenTemplateMatched == true);
        long goldenAttempts = persistedEligibleAttempts;
        long goldenHits = persistedEligibleHits;
        var goldenSource = "run_history_eligible";

        if (goldenAttempts == 0 && !hasEligibilitySignal)
        {
            var legacyAttempts = window.Count(x => x.GoldenTemplateMatched.HasValue);
            var legacyHits = window.Count(x => x.GoldenTemplateMatched == true);
            if (legacyAttempts > 0)
            {
                goldenAttempts = legacyAttempts;
                goldenHits = legacyHits;
                goldenSource = "run_history_legacy";
            }
            else
            {
                goldenAttempts = generationSnapshot.GenerationGoldenTemplateHitTotal + generationSnapshot.GenerationGoldenTemplateMissTotal;
                goldenHits = generationSnapshot.GenerationGoldenTemplateHitTotal;
                goldenSource = "runtime_fallback";
            }
        }

        var goldenSampleInsufficient = goldenAttempts < minGoldenAttempts;
        var goldenHitRate = goldenAttempts == 0 ? 0 : goldenHits / (double)goldenAttempts;
        return new ParityRunHistorySnapshot(
            stats.TotalRuns,
            stats.SuccessfulRuns,
            stats.FailedRuns,
            stats.SuccessRate,
            stats.P95ReadySeconds,
            stats.UnknownErrorRate,
            goldenHitRate,
            goldenAttempts,
            goldenHits,
            goldenSource,
            minGoldenAttempts,
            goldenSampleInsufficient,
            stats.TopErrorCodeTotals,
            entries.Count,
            window.Count,
            lookbackHours,
            workloadFilters.Count == 0 ? "all" : string.Join(",", workloadFilters),
            windowAllWorkloads.Count,
            discoveryOptions.Mode,
            discoveryOptions.DirectRoots,
            discoveryOptions.RecursiveRoots,
            stats.FailureCategoryTotals,
            stats.FailureSamples,
            stats.CleanSuccessRuns,
            stats.DegradedSuccessRuns,
            stats.HardFailedRuns);
    }

    private static async Task<IReadOnlyList<RunLogEntry>> ReadEntriesAsync(
        GenerationArtifactDiscoveryOptions discoveryOptions,
        CancellationToken ct)
    {
        var candidateFiles = GenerationArtifactLocator.EnumerateRunHistoryFiles(discoveryOptions);

        if (candidateFiles.Count == 0)
        {
            return Array.Empty<RunLogEntry>();
        }

        var dedup = new Dictionary<string, RunLogEntry>(StringComparer.Ordinal);
        foreach (var file in candidateFiles.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();
            string[] lines;
            try
            {
                lines = await File.ReadAllLinesAsync(file, ct);
            }
            catch
            {
                continue;
            }

            var fileTimestamp = File.GetLastWriteTimeUtc(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                if (!TryParseEntry(line, file, i + 1, fileTimestamp, out var entry))
                {
                    continue;
                }

                var key = string.IsNullOrWhiteSpace(entry.RunId)
                    ? $"{Path.GetFullPath(file)}::{i + 1}"
                    : entry.RunId;
                if (!dedup.TryGetValue(key, out var existing) || ResolveTimelineAnchor(entry) >= ResolveTimelineAnchor(existing))
                {
                    dedup[key] = entry;
                }
            }
        }

        return dedup.Values
            .OrderByDescending(ResolveTimelineAnchor)
            .ThenBy(x => x.RunId, StringComparer.Ordinal)
            .ToList();
    }

    private static bool TryParseEntry(
        string line,
        string sourceFile,
        int sourceLineNumber,
        DateTime sourceFileTimestampUtc,
        out RunLogEntry entry)
    {
        entry = default!;
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;

            var runId = ReadString(root, "RunId");
            var started = ReadDate(root, "StartedAtUtc");
            var completed = ReadDate(root, "CompletedAtUtc");
            var compileGatePassed = ReadBool(root, "CompileGatePassed") ?? false;
            var errors = ReadErrors(root);
            var warnings = ReadStringArray(root, "Warnings");
            var placeholderFindings = ReadStringArray(root, "PlaceholderFindings");
            var artifactValidationPassed = ReadBool(root, "ArtifactValidationPassed");
            var smokePassed = ReadBool(root, "SmokePassed");
            var outcome = ParityRunOutcomeClassifier.Evaluate(
                compileGatePassed,
                errors,
                warnings,
                placeholderFindings,
                artifactValidationPassed,
                smokePassed);

            bool? goldenMatched = null;
            if (ReadBool(root, "GoldenTemplateMatched") is { } explicitGolden)
            {
                goldenMatched = explicitGolden;
            }
            else if (ReadBool(root, "RouteMatched") is { } routeMatched)
            {
                goldenMatched = routeMatched;
            }

            bool? goldenEligible = ReadBool(root, "GoldenTemplateEligible");
            if (!goldenEligible.HasValue)
            {
                var routedTemplateId = ReadString(root, "RoutedTemplateId");
                if (GoldenTemplateIntentClassifier.IsGoldenTemplateId(routedTemplateId) &&
                    ReadBool(root, "RouteMatched") == true)
                {
                    goldenEligible = true;
                }
                else if (goldenMatched == true)
                {
                    // Backward compatibility for older reports that persisted only the matched bit.
                    goldenEligible = true;
                }
            }

            var workloadClass = ResolveWorkloadClass(root, goldenEligible, goldenMatched);

            entry = new RunLogEntry(
                runId,
                sourceFile,
                sourceLineNumber,
                sourceFileTimestampUtc,
                started,
                completed,
                compileGatePassed,
                outcome,
                errors,
                warnings,
                placeholderFindings,
                goldenMatched,
                goldenEligible,
                workloadClass,
                ReadString(root, "Prompt"),
                ReadString(root, "RoutedTemplateId"),
                ReadBool(root, "RouteMatched"),
                artifactValidationPassed,
                smokePassed);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static InternalStats ComputeStats(IReadOnlyList<RunLogEntry> entries)
    {
        if (entries.Count == 0)
        {
            return new InternalStats(
                0,
                0,
                0,
                0,
                0,
                0,
                new Dictionary<string, long>(StringComparer.Ordinal),
                new Dictionary<string, long>(StringComparer.Ordinal),
                Array.Empty<ParityFailureSample>(),
                0,
                0,
                0);
        }

        var durations = new List<double>();
        var cleanSuccessCount = 0L;
        var degradedSuccessCount = 0L;
        var hardFailedCount = 0L;
        var unknownFailures = 0L;
        var errorCodeTotals = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            switch (entry.Outcome.Disposition)
            {
                case ParityRunDisposition.CleanSuccess:
                    cleanSuccessCount++;
                    break;
                case ParityRunDisposition.DegradedSuccess:
                    degradedSuccessCount++;
                    break;
                default:
                    hardFailedCount++;
                    if (ErrorsLookUnknown(entry.Errors))
                    {
                        unknownFailures++;
                    }

                    foreach (var code in ExtractErrorCodes(entry.Errors))
                    {
                        if (errorCodeTotals.TryGetValue(code, out var current))
                        {
                            errorCodeTotals[code] = current + 1;
                        }
                        else
                        {
                            errorCodeTotals[code] = 1;
                        }
                    }
                    break;
            }

            if (entry.Outcome.DegradesParityMetrics && entry.Outcome.Disposition != ParityRunDisposition.Failure)
            {
                if (entry.Outcome.PrimaryFailureEvidence is { Length: > 0 } evidence)
                {
                    foreach (var code in ExtractErrorCodes(new[] { evidence }))
                    {
                        if (errorCodeTotals.TryGetValue(code, out var current))
                        {
                            errorCodeTotals[code] = current + 1;
                        }
                        else
                        {
                            errorCodeTotals[code] = 1;
                        }
                    }
                }
            }

            if (entry.StartedAtUtc.HasValue && entry.CompletedAtUtc.HasValue)
            {
                var duration = (entry.CompletedAtUtc.Value - entry.StartedAtUtc.Value).TotalSeconds;
                if (duration >= 0)
                {
                    durations.Add(duration);
                }
            }
        }

        var total = entries.Count;
        var successCount = cleanSuccessCount;
        var failedCount = total - cleanSuccessCount;
        var successRate = total == 0 ? 0 : successCount / (double)total;
        var unknownErrorRate = hardFailedCount == 0 ? 0 : unknownFailures / (double)hardFailedCount;
        var p95 = ComputePercentile(durations, 0.95);
        var topCodes = errorCodeTotals
            .OrderByDescending(x => x.Value)
            .ThenBy(x => x.Key, StringComparer.Ordinal)
            .Take(10)
            .ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
        var failureCategoryTotals = entries
            .Where(x => x.Outcome.DegradesParityMetrics && x.Outcome.FailureCategory.HasValue)
            .GroupBy(x => x.Outcome.FailureCategory!.Value)
            .OrderByDescending(x => x.Count())
            .ThenBy(x => x.Key.ToString(), StringComparer.Ordinal)
            .ToDictionary(x => ToFailureCategoryLabel(x.Key), x => (long)x.Count(), StringComparer.OrdinalIgnoreCase);
        var failureSamples = entries
            .Where(x => x.Outcome.DegradesParityMetrics)
            .OrderByDescending(ResolveTimelineAnchor)
            .ThenBy(x => x.RunId, StringComparer.Ordinal)
            .Take(10)
            .Select(x => new ParityFailureSample(
                RunId: string.IsNullOrWhiteSpace(x.RunId) ? $"{Path.GetFileName(x.SourceFile)}:{x.SourceLineNumber}" : x.RunId!,
                Category: ToFailureCategoryLabel(x.Outcome.FailureCategory ?? ParityFailureCategory.Unknown),
                Disposition: x.Outcome.Disposition.ToString(),
                RoutedTemplateId: x.RoutedTemplateId,
                Prompt: x.Prompt,
                PrimaryEvidence: x.Outcome.PrimaryFailureEvidence))
            .ToList();

        return new InternalStats(total, successCount, failedCount, successRate, p95, unknownErrorRate, topCodes, failureCategoryTotals, failureSamples, cleanSuccessCount, degradedSuccessCount, hardFailedCount);
    }

    private static IEnumerable<string> ExtractErrorCodes(IReadOnlyList<string> errors)
    {
        foreach (var error in errors)
        {
            if (string.IsNullOrWhiteSpace(error))
            {
                continue;
            }

            var matched = false;
            foreach (Match match in ErrorCodeRegex.Matches(error))
            {
                matched = true;
                yield return match.Value.ToUpperInvariant();
            }

            if (!matched && (error.Contains("unknown", StringComparison.OrdinalIgnoreCase) ||
                             error.Contains("unhandled", StringComparison.OrdinalIgnoreCase)))
            {
                yield return "UNKNOWN";
            }
        }
    }

    private static bool ErrorsLookUnknown(IReadOnlyList<string> errors)
    {
        if (errors.Count == 0)
        {
            return true;
        }

        foreach (var error in errors)
        {
            if (string.IsNullOrWhiteSpace(error))
            {
                continue;
            }

            if (!error.Contains("unknown", StringComparison.OrdinalIgnoreCase) &&
                !error.Contains("unhandled", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static string? ReadString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var node) || node.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return node.GetString();
    }

    private static bool? ReadBool(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var node))
        {
            return null;
        }

        return node.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(node.GetString(), out var parsed) => parsed,
            _ => null
        };
    }

    private static DateTimeOffset? ReadDate(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var node) || node.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return DateTimeOffset.TryParse(node.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
    }

    private static IReadOnlyList<string> ReadErrors(JsonElement root)
    {
        return ReadStringArray(root, "Errors");
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var node) || node.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        var values = new List<string>();
        foreach (var item in node.EnumerateArray())
        {
            var text = item.ToString();
            if (!string.IsNullOrWhiteSpace(text))
            {
                values.Add(text);
            }
        }

        return values;
    }

    private static DateTimeOffset ResolveTimelineAnchor(RunLogEntry entry)
    {
        if (entry.CompletedAtUtc.HasValue)
        {
            return entry.CompletedAtUtc.Value;
        }

        if (entry.StartedAtUtc.HasValue)
        {
            return entry.StartedAtUtc.Value;
        }

        return new DateTimeOffset(DateTime.SpecifyKind(entry.SourceFileTimestampUtc, DateTimeKind.Utc));
    }

    private static double ComputePercentile(IReadOnlyList<double> values, double percentile)
    {
        if (values.Count == 0)
        {
            return 0;
        }

        var ordered = values.OrderBy(x => x).ToList();
        var index = Math.Clamp((int)Math.Ceiling(percentile * ordered.Count) - 1, 0, ordered.Count - 1);
        return ordered[index];
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

    private static IReadOnlySet<string> ReadWorkloadFilter(string envName)
    {
        var raw = Environment.GetEnvironmentVariable(envName);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        var values = raw
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(GenerationWorkloadClassifier.Normalize)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Cast<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return values;
    }

    private static bool MatchesWorkload(string workloadClass, IReadOnlySet<string> workloadFilters)
    {
        if (workloadFilters.Count == 0)
        {
            return true;
        }

        return workloadFilters.Contains(workloadClass);
    }

    private static string ResolveWorkloadClass(JsonElement root, bool? goldenEligible, bool? goldenMatched)
    {
        var explicitClass = GenerationWorkloadClassifier.Normalize(ReadString(root, "WorkloadClass"));
        if (!string.IsNullOrWhiteSpace(explicitClass))
        {
            return explicitClass;
        }

        if (goldenEligible == true || goldenMatched == true)
        {
            return GenerationWorkloadClassifier.Parity;
        }

        if (goldenEligible == false || goldenMatched == false)
        {
            return GenerationWorkloadClassifier.General;
        }

        return GenerationWorkloadClassifier.Legacy;
    }

    private sealed record InternalStats(
        long TotalRuns,
        long SuccessfulRuns,
        long FailedRuns,
        double SuccessRate,
        double P95ReadySeconds,
        double UnknownErrorRate,
        IReadOnlyDictionary<string, long> TopErrorCodeTotals,
        IReadOnlyDictionary<string, long> FailureCategoryTotals,
        IReadOnlyList<ParityFailureSample> FailureSamples,
        long CleanSuccessRuns,
        long DegradedSuccessRuns,
        long HardFailedRuns);

    private sealed record RunLogEntry(
        string? RunId,
        string SourceFile,
        int SourceLineNumber,
        DateTime SourceFileTimestampUtc,
        DateTimeOffset? StartedAtUtc,
        DateTimeOffset? CompletedAtUtc,
        bool CompileGatePassed,
        ParityRunOutcome Outcome,
        IReadOnlyList<string> Errors,
        IReadOnlyList<string> Warnings,
        IReadOnlyList<string> PlaceholderFindings,
        bool? GoldenTemplateMatched,
        bool? GoldenTemplateEligible,
        string WorkloadClass,
        string? Prompt,
        string? RoutedTemplateId,
        bool? RouteMatched,
        bool? ArtifactValidationPassed,
        bool? SmokePassed);

    private static string ToFailureCategoryLabel(ParityFailureCategory category)
    {
        return category switch
        {
            ParityFailureCategory.RoutingMismatch => "routing_mismatch",
            ParityFailureCategory.BlueprintWeakness => "blueprint_weakness",
            ParityFailureCategory.CompileGateFail => "compile_gate_fail",
            ParityFailureCategory.SemanticFallbackOveruse => "semantic_fallback_overuse",
            ParityFailureCategory.ReportPersistenceMismatch => "report_persistence_mismatch",
            _ => "unknown"
        };
    }
}

internal sealed record ParityRunHistorySnapshot(
    long TotalRuns,
    long SuccessfulRuns,
    long FailedRuns,
    double SuccessRate,
    double P95ReadySeconds,
    double UnknownErrorRate,
    double GoldenHitRate,
    long GoldenAttempts,
    long GoldenHits,
    string GoldenSource,
    int MinGoldenAttemptsRequired,
    bool GoldenSampleInsufficient,
    IReadOnlyDictionary<string, long> TopErrorCodeTotals,
    int LoadedEntries,
    int WindowEntries,
    int LookbackHours,
    string WorkloadFilter,
    int WindowEntriesBeforeWorkloadFilter,
    GenerationArtifactDiscoveryMode DiscoveryMode,
    IReadOnlyList<string> DiscoveryDirectRoots,
    IReadOnlyList<string> DiscoveryRecursiveRoots,
    IReadOnlyDictionary<string, long> FailureCategoryTotals,
    IReadOnlyList<ParityFailureSample> FailureSamples,
    long CleanSuccessRuns,
    long DegradedSuccessRuns,
    long HardFailedRuns);

internal sealed record ParityFailureSample(
    string RunId,
    string Category,
    string Disposition,
    string? RoutedTemplateId,
    string? Prompt,
    string? PrimaryEvidence);

