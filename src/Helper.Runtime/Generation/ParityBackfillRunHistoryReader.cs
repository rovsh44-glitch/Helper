using System.Security.Cryptography;
using System.Text.Json;
using Helper.Runtime.Core;

namespace Helper.Runtime.Generation;

internal sealed class ParityBackfillRunHistoryReader
{
    public async Task<ParityDailyBackfillSourceSnapshot> ReadAsync(
        GenerationArtifactDiscoveryOptions discoveryOptions,
        CancellationToken ct)
    {
        var sourceFiles = GenerationArtifactLocator.EnumerateRunHistoryFiles(discoveryOptions);
        if (sourceFiles.Count == 0)
        {
            throw new InvalidOperationException("No generation_runs.jsonl files found in workspace.");
        }

        var sourceAudits = new List<ParityBackfillSourceFileAudit>(sourceFiles.Count);
        var deduplicated = new Dictionary<string, ParityBackfillRunEntry>(StringComparer.Ordinal);
        long sourceLineCount = 0;
        long parsedLineCount = 0;
        long malformedLineCount = 0;

        foreach (var file in sourceFiles)
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

            long fileParsed = 0;
            long fileMalformed = 0;
            var fileSha = ComputeSha256(file);
            var sourceFileTimestamp = File.GetLastWriteTimeUtc(file);
            for (var i = 0; i < lines.Length; i++)
            {
                sourceLineCount++;
                var line = lines[i];
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                if (!TryParseEntry(line, sourceFileTimestamp, out var entry))
                {
                    fileMalformed++;
                    malformedLineCount++;
                    continue;
                }

                fileParsed++;
                parsedLineCount++;
                var key = string.IsNullOrWhiteSpace(entry.RunId)
                    ? $"{Path.GetFullPath(file)}::{i + 1}"
                    : entry.RunId;
                if (!deduplicated.TryGetValue(key, out var existing) ||
                    entry.TimelineAnchorUtc >= existing.TimelineAnchorUtc)
                {
                    deduplicated[key] = entry;
                }
            }

            sourceAudits.Add(new ParityBackfillSourceFileAudit(
                Path: Path.GetFullPath(file),
                Sha256: fileSha,
                TotalLines: lines.Length,
                ParsedLines: fileParsed,
                MalformedLines: fileMalformed));
        }

        return new ParityDailyBackfillSourceSnapshot(
            SourceFiles: sourceAudits,
            Entries: deduplicated.Values.OrderBy(x => x.TimelineAnchorUtc).ToArray(),
            SourceLineCount: sourceLineCount,
            ParsedLineCount: parsedLineCount,
            MalformedLineCount: malformedLineCount);
    }

    private static bool TryParseEntry(string line, DateTime sourceFileTimestampUtc, out ParityBackfillRunEntry entry)
    {
        entry = default!;
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            var runId = ReadString(root, "RunId");
            var startedAt = ReadDate(root, "StartedAtUtc");
            var completedAt = ReadDate(root, "CompletedAtUtc");
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
            if (ReadBool(root, "GoldenTemplateMatched") is { } explicitMatched)
            {
                goldenMatched = explicitMatched;
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
                    goldenEligible = true;
                }
            }

            entry = new ParityBackfillRunEntry(
                RunId: runId,
                SourceFileTimestampUtc: new DateTimeOffset(sourceFileTimestampUtc, TimeSpan.Zero),
                StartedAtUtc: startedAt,
                CompletedAtUtc: completedAt,
                Outcome: outcome,
                Errors: errors,
                GoldenTemplateMatched: goldenMatched,
                GoldenTemplateEligible: goldenEligible,
                WorkloadClass: ResolveWorkloadClass(root, goldenEligible, goldenMatched));
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string ResolveWorkloadClass(JsonElement root, bool? goldenEligible, bool? goldenMatched)
    {
        var workload = ReadString(root, "WorkloadClass");
        if (!string.IsNullOrWhiteSpace(workload))
        {
            return workload.Trim();
        }

        if (goldenEligible == true || goldenMatched == true)
        {
            return GenerationWorkloadClassifier.Parity;
        }

        var prompt = ReadString(root, "Prompt");
        return GenerationWorkloadClassifier.Resolve(prompt);
    }

    private static string? ReadString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static bool? ReadBool(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(value.GetString(), out var parsed) => parsed,
            _ => null
        };
    }

    private static DateTimeOffset? ReadDate(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return DateTimeOffset.TryParse(value.GetString(), out var parsed) ? parsed : null;
    }

    private static IReadOnlyList<string> ReadErrors(JsonElement root)
    {
        return ReadStringArray(root, "Errors");
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        return value.EnumerateArray()
            .Where(x => x.ValueKind == JsonValueKind.String)
            .Select(x => x.GetString())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!.Trim())
            .ToArray();
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash);
    }
}

internal sealed record ParityBackfillRunEntry(
    string? RunId,
    DateTimeOffset SourceFileTimestampUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    ParityRunOutcome Outcome,
    IReadOnlyList<string> Errors,
    bool? GoldenTemplateMatched,
    bool? GoldenTemplateEligible,
    string WorkloadClass)
{
    public DateTimeOffset TimelineAnchorUtc => CompletedAtUtc ?? StartedAtUtc ?? SourceFileTimestampUtc;
}

internal sealed record ParityDailyBackfillSourceSnapshot(
    IReadOnlyList<ParityBackfillSourceFileAudit> SourceFiles,
    IReadOnlyList<ParityBackfillRunEntry> Entries,
    long SourceLineCount,
    long ParsedLineCount,
    long MalformedLineCount);

