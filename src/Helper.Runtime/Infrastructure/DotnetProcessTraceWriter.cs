using System.Text;
using System.Text.Json;

namespace Helper.Runtime.Infrastructure;

internal sealed record DotnetProcessTraceRecord(
    DateTimeOffset OccurredAtUtc,
    string EventType,
    string Operation,
    string WorkingDirectory,
    string? Target,
    string Arguments,
    int? ProcessId,
    int? ExitCode,
    double TimeoutBudgetSeconds,
    double ElapsedSeconds,
    bool TimedOut,
    bool KillAttempted,
    bool? KillConfirmed,
    bool OrphanRisk,
    string? Details);

internal static class DotnetProcessTraceWriter
{
    internal const string TraceFileName = "certification_process_trace.jsonl";
    internal const string TracePathEnvName = "HELPER_CERTIFICATION_PROCESS_TRACE_PATH";

    private static readonly object Sync = new();

    public static string ResolvePath()
    {
        var overridePath = Environment.GetEnvironmentVariable(TracePathEnvName);
        return string.IsNullOrWhiteSpace(overridePath)
            ? HelperWorkspacePathResolver.ResolveLogsPath(TraceFileName)
            : Path.GetFullPath(overridePath);
    }

    public static void Append(DotnetProcessTraceRecord record)
    {
        try
        {
            var path = ResolvePath();
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var payload = new
            {
                occurredAtUtc = record.OccurredAtUtc,
                eventType = record.EventType,
                operation = record.Operation,
                workingDirectory = record.WorkingDirectory,
                target = record.Target,
                arguments = record.Arguments,
                processId = record.ProcessId,
                exitCode = record.ExitCode,
                timeoutBudgetSeconds = record.TimeoutBudgetSeconds,
                elapsedSeconds = record.ElapsedSeconds,
                timedOut = record.TimedOut,
                killAttempted = record.KillAttempted,
                killConfirmed = record.KillConfirmed,
                orphanRisk = record.OrphanRisk,
                details = record.Details
            };

            var json = JsonSerializer.Serialize(payload);
            lock (Sync)
            {
                File.AppendAllText(path, json + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[DotnetProcessTraceWriter] Failed to append trace: {ex.Message}");
        }
    }
}
