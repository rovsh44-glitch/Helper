using System.Text.RegularExpressions;

namespace Helper.Api.Hosting;

public interface IRuntimeLogService
{
    RuntimeLogsSnapshotDto GetSnapshot(int tailLinesPerSource = 60, int maxSources = 4);
}

public sealed class RuntimeLogService : IRuntimeLogService
{
    private static readonly string[] LogPatterns = { "*.log", "*.jsonl", "*.txt" };
    private static readonly HashSet<string> IgnoredFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "API_PORT.txt"
    };
    private const int MaxRenderedLineLength = 280;
    private static readonly Regex TimestampRegex = new(
        @"^\[?(?<ts>\d{2}:\d{2}:\d{2}(?:\.\d+)?|\d{4}-\d{2}-\d{2}[T ][^ ]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly ApiRuntimeConfig _runtimeConfig;

    public RuntimeLogService(ApiRuntimeConfig runtimeConfig)
    {
        _runtimeConfig = runtimeConfig;
    }

    public RuntimeLogsSnapshotDto GetSnapshot(int tailLinesPerSource = 60, int maxSources = 4)
    {
        tailLinesPerSource = Math.Clamp(tailLinesPerSource, 10, 240);
        maxSources = Math.Clamp(maxSources, 1, 8);

        var alerts = new List<string>();
        var sources = new List<RuntimeLogSourceDto>();
        var entries = new List<RuntimeLogEntryDto>();

        foreach (var source in DiscoverSources(alerts, maxSources))
        {
            try
            {
                var tail = ReadTail(source.FullPath, tailLinesPerSource);
                var displayPath = ToDisplayPath(source.FullPath);
                var sourceId = displayPath;

                sources.Add(new RuntimeLogSourceDto(
                    Id: sourceId,
                    Label: source.Label,
                    DisplayPath: displayPath,
                    SizeBytes: source.SizeBytes,
                    LastWriteTimeUtc: source.LastWriteTimeUtc,
                    TotalLines: tail.TotalLines,
                    IsPrimary: source.IsPrimary));

                var lineNumber = tail.FirstLineNumber;
                foreach (var line in tail.Lines)
                {
                    var renderedLine = NormalizeLineForUi(line);
                    var severity = ResolveSeverity(line);
                    var isContinuation = IsContinuationLine(line);
                    entries.Add(new RuntimeLogEntryDto(
                        SourceId: sourceId,
                        LineNumber: lineNumber++,
                        Text: renderedLine,
                        Severity: severity,
                        TimestampLabel: ResolveTimestampLabel(line),
                        IsContinuation: isContinuation,
                        Semantics: RuntimeLogSemanticDeriver.Derive(line, severity, displayPath, isContinuation)));
                }
            }
            catch (Exception ex)
            {
                alerts.Add($"Runtime log source '{source.Label}' could not be read: {ex.Message}");
            }
        }

        if (sources.Count == 0)
        {
            alerts.Add("No runtime log sources were discovered.");
        }

        return new RuntimeLogsSnapshotDto(
            SchemaVersion: 2,
            SemanticsVersion: "runtime-log-dto-v2",
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            Sources: sources,
            Entries: entries,
            Alerts: alerts.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private IReadOnlyList<DiscoveredLogSource> DiscoverSources(List<string> alerts, int maxSources)
    {
        var discovered = new List<DiscoveredLogSource>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void TryAddFile(string fullPath, string label, bool isPrimary)
        {
            if (string.IsNullOrWhiteSpace(fullPath))
            {
                return;
            }

            var normalized = Path.GetFullPath(fullPath);
            if (!File.Exists(normalized) || !seen.Add(normalized))
            {
                return;
            }

            var fileInfo = new FileInfo(normalized);
            discovered.Add(new DiscoveredLogSource(
                normalized,
                label,
                fileInfo.Exists ? fileInfo.Length : 0,
                fileInfo.Exists ? new DateTimeOffset(fileInfo.LastWriteTimeUtc) : null,
                isPrimary));
        }

        void TryAddDirectory(string? directoryPath, string labelPrefix)
        {
            if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
            {
                return;
            }

            try
            {
                var files = LogPatterns
                    .SelectMany(pattern => Directory.EnumerateFiles(directoryPath, pattern, SearchOption.TopDirectoryOnly))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Select(path => new FileInfo(path))
                    .Where(file => file.Exists && file.Length > 0 && !IgnoredFileNames.Contains(file.Name))
                    .OrderByDescending(file => file.LastWriteTimeUtc)
                    .Take(maxSources);

                foreach (var file in files)
                {
                    TryAddFile(file.FullName, $"{labelPrefix}: {file.Name}", isPrimary: false);
                }
            }
            catch (Exception ex)
            {
                alerts.Add($"Runtime log directory '{directoryPath}' is unavailable: {ex.Message}");
            }
        }

        if (!Directory.Exists(_runtimeConfig.LogsRoot))
        {
            alerts.Add($"Runtime logs root is unavailable: {_runtimeConfig.LogsRoot}");
        }
        else
        {
            TryAddDirectory(_runtimeConfig.LogsRoot, "Runtime log");
        }

        return discovered
            .OrderByDescending(source => source.IsPrimary)
            .ThenByDescending(source => source.LastWriteTimeUtc ?? DateTimeOffset.MinValue)
            .Take(maxSources)
            .ToArray();
    }

    private static TailReadResult ReadTail(string fullPath, int tailLines)
    {
        var buffer = new Queue<string>(tailLines);
        var totalLines = 0;

        using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream)
        {
            var line = reader.ReadLine() ?? string.Empty;
            totalLines++;
            if (buffer.Count == tailLines)
            {
                buffer.Dequeue();
            }

            buffer.Enqueue(line);
        }

        var firstLineNumber = totalLines == 0 ? 1 : Math.Max(1, totalLines - buffer.Count + 1);
        return new TailReadResult(buffer.ToArray(), totalLines, firstLineNumber);
    }

    private string ToDisplayPath(string fullPath)
    {
        var normalized = Path.GetFullPath(fullPath);
        if (normalized.StartsWith(_runtimeConfig.RootPath, StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetRelativePath(_runtimeConfig.RootPath, normalized).Replace('\\', '/');
        }

        if (normalized.StartsWith(_runtimeConfig.DataRoot, StringComparison.OrdinalIgnoreCase))
        {
            return $"DATA/{Path.GetRelativePath(_runtimeConfig.DataRoot, normalized).Replace('\\', '/')}";
        }

        return Path.GetFileName(normalized);
    }

    private static string ResolveSeverity(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return "neutral";
        }

        var normalized = line.TrimStart().ToLowerInvariant();
        if (normalized.StartsWith("fail:") ||
            normalized.Contains(" unhandled exception") ||
            normalized.Contains("exception") ||
            normalized.Contains("error"))
        {
            return "error";
        }

        if (normalized.StartsWith("warn:") ||
            normalized.Contains(" warning") ||
            normalized.Contains(" denied") ||
            normalized.Contains(" degraded"))
        {
            return "warn";
        }

        if (normalized.StartsWith("info:") ||
            normalized.Contains("ready") ||
            normalized.Contains("started") ||
            normalized.Contains("listening"))
        {
            return "info";
        }

        if (normalized.StartsWith("debug:") || normalized.Contains("trace"))
        {
            return "debug";
        }

        return "neutral";
    }

    private static string? ResolveTimestampLabel(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        var match = TimestampRegex.Match(line.TrimStart());
        return match.Success ? match.Groups["ts"].Value : null;
    }

    private static bool IsContinuationLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        var trimmed = line.TrimStart();
        return line.StartsWith("   ", StringComparison.Ordinal) ||
               trimmed.StartsWith("at ", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("---", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("inner exception", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeLineForUi(string line)
    {
        if (string.IsNullOrEmpty(line) || line.Length <= MaxRenderedLineLength)
        {
            return line;
        }

        return line[..MaxRenderedLineLength].TrimEnd() + " ...";
    }

    private sealed record DiscoveredLogSource(
        string FullPath,
        string Label,
        long SizeBytes,
        DateTimeOffset? LastWriteTimeUtc,
        bool IsPrimary);

    private sealed record TailReadResult(
        IReadOnlyList<string> Lines,
        int TotalLines,
        int FirstLineNumber);
}

