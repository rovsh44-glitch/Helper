using System.Diagnostics;
using System.Text;

namespace Helper.Runtime.Infrastructure;

internal static class DotnetProcessRunner
{
    public static async Task<DotnetCommandResult> RunAsync(
        DotnetOperationKind operation,
        string arguments,
        string workingDirectory,
        string? target,
        CancellationToken ct)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        var output = new StringBuilder();
        var error = new StringBuilder();
        var sync = new object();
        process.OutputDataReceived += (_, args) =>
        {
            if (args.Data is null)
            {
                return;
            }

            lock (sync)
            {
                output.AppendLine(args.Data);
            }
        };
        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data is null)
            {
                return;
            }

            lock (sync)
            {
                error.AppendLine(args.Data);
            }
        };

        var timeoutBudget = DotnetTimeoutPolicy.ResolveTimeoutBudget(operation);
        var heartbeatInterval = DotnetTimeoutPolicy.ResolveHeartbeatInterval();
        var killConfirmationTimeout = DotnetTimeoutPolicy.ResolveKillConfirmationTimeout();
        var startedAt = DateTimeOffset.UtcNow;
        var timedOut = false;
        var canceledByCaller = false;
        var killAttempted = false;
        bool? killConfirmed = null;
        var orphanRisk = false;

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            DotnetProcessTraceWriter.Append(CreateTraceRecord(
                eventType: "started",
                operation,
                workingDirectory,
                target,
                arguments,
                startedAt,
                process.Id,
                exitCode: null,
                timeoutBudget,
                elapsed: TimeSpan.Zero,
                timedOut: false,
                killAttempted: false,
                killConfirmed: null,
                orphanRisk: false,
                details: null));

            var exitTask = process.WaitForExitAsync();
            var nextHeartbeatAt = startedAt + heartbeatInterval;

            while (!exitTask.IsCompleted)
            {
                if (ct.IsCancellationRequested)
                {
                    canceledByCaller = true;
                    break;
                }

                var elapsed = DateTimeOffset.UtcNow - startedAt;
                if (elapsed >= timeoutBudget)
                {
                    timedOut = true;
                    break;
                }

                var remainingBudget = timeoutBudget - elapsed;
                var delay = remainingBudget < heartbeatInterval ? remainingBudget : heartbeatInterval;
                if (delay <= TimeSpan.Zero)
                {
                    timedOut = true;
                    break;
                }

                try
                {
                    await Task.WhenAny(exitTask, Task.Delay(delay, ct)).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    canceledByCaller = true;
                    break;
                }

                if (exitTask.IsCompleted)
                {
                    break;
                }

                if (DateTimeOffset.UtcNow >= nextHeartbeatAt)
                {
                    DotnetProcessTraceWriter.Append(CreateTraceRecord(
                        eventType: "heartbeat",
                        operation,
                        workingDirectory,
                        target,
                        arguments,
                        startedAt,
                        process.Id,
                        exitCode: null,
                        timeoutBudget,
                        elapsed: DateTimeOffset.UtcNow - startedAt,
                        timedOut: false,
                        killAttempted: false,
                        killConfirmed: null,
                        orphanRisk: false,
                        details: "process still running"));
                    nextHeartbeatAt = DateTimeOffset.UtcNow + heartbeatInterval;
                }
            }

            if (exitTask.IsCompleted)
            {
                await exitTask.ConfigureAwait(false);
                process.WaitForExit();
                var completedAt = DateTimeOffset.UtcNow;
                DotnetProcessTraceWriter.Append(CreateTraceRecord(
                    eventType: "exited",
                    operation,
                    workingDirectory,
                    target,
                    arguments,
                    startedAt,
                    process.Id,
                    process.ExitCode,
                    timeoutBudget,
                    completedAt - startedAt,
                    timedOut: false,
                    killAttempted: false,
                    killConfirmed: null,
                    orphanRisk: false,
                    details: null));
                return new DotnetCommandResult(
                    CaptureCombinedOutput(output, error, sync),
                    process.ExitCode,
                    TimedOut: false,
                    CanceledByCaller: false,
                    KillAttempted: false,
                    KillConfirmed: null,
                    OrphanRisk: false,
                    TimeoutBudget: timeoutBudget,
                    Elapsed: completedAt - startedAt);
            }

            DotnetProcessTraceWriter.Append(CreateTraceRecord(
                eventType: timedOut ? "timeout_started" : "cancellation_started",
                operation,
                workingDirectory,
                target,
                arguments,
                startedAt,
                process.Id,
                exitCode: null,
                timeoutBudget,
                DateTimeOffset.UtcNow - startedAt,
                timedOut,
                killAttempted: false,
                killConfirmed: null,
                orphanRisk: false,
                details: timedOut ? "timeout budget exhausted" : "caller cancellation requested"));

            killAttempted = true;
            DotnetProcessTraceWriter.Append(CreateTraceRecord(
                eventType: "kill_attempted",
                operation,
                workingDirectory,
                target,
                arguments,
                startedAt,
                process.Id,
                exitCode: null,
                timeoutBudget,
                DateTimeOffset.UtcNow - startedAt,
                timedOut,
                killAttempted: true,
                killConfirmed: null,
                orphanRisk: false,
                details: null));

            try
            {
                process.Kill(true);
            }
            catch (Exception ex)
            {
                DotnetProcessTraceWriter.Append(CreateTraceRecord(
                    eventType: "kill_failed",
                    operation,
                    workingDirectory,
                    target,
                    arguments,
                    startedAt,
                    process.Id,
                    exitCode: null,
                    timeoutBudget,
                    DateTimeOffset.UtcNow - startedAt,
                    timedOut,
                    killAttempted: true,
                    killConfirmed: false,
                    orphanRisk: true,
                    details: ex.Message));
            }

            var completion = await Task.WhenAny(exitTask, Task.Delay(killConfirmationTimeout)).ConfigureAwait(false);
            if (completion == exitTask)
            {
                await exitTask.ConfigureAwait(false);
                process.WaitForExit();
                killConfirmed = true;
                DotnetProcessTraceWriter.Append(CreateTraceRecord(
                    eventType: "kill_confirmed",
                    operation,
                    workingDirectory,
                    target,
                    arguments,
                    startedAt,
                    process.Id,
                    process.ExitCode,
                    timeoutBudget,
                    DateTimeOffset.UtcNow - startedAt,
                    timedOut,
                    killAttempted: true,
                    killConfirmed: true,
                    orphanRisk: false,
                    details: null));
            }
            else
            {
                killConfirmed = false;
                orphanRisk = true;
                DotnetProcessTraceWriter.Append(CreateTraceRecord(
                    eventType: "orphan_risk",
                    operation,
                    workingDirectory,
                    target,
                    arguments,
                    startedAt,
                    process.Id,
                    exitCode: null,
                    timeoutBudget,
                    DateTimeOffset.UtcNow - startedAt,
                    timedOut,
                    killAttempted: true,
                    killConfirmed: false,
                    orphanRisk: true,
                    details: $"process did not exit within {killConfirmationTimeout.TotalSeconds:0}s after Kill(true)"));
                TryCancelOutputReads(process);
            }

            return new DotnetCommandResult(
                CaptureCombinedOutput(output, error, sync),
                process.HasExited ? process.ExitCode : null,
                timedOut,
                canceledByCaller,
                killAttempted,
                killConfirmed,
                orphanRisk,
                timeoutBudget,
                DateTimeOffset.UtcNow - startedAt);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            DotnetProcessTraceWriter.Append(CreateTraceRecord(
                eventType: "exception",
                operation,
                workingDirectory,
                target,
                arguments,
                startedAt,
                TryGetProcessId(process),
                TryGetExitCode(process),
                timeoutBudget,
                DateTimeOffset.UtcNow - startedAt,
                timedOut,
                killAttempted,
                killConfirmed,
                orphanRisk,
                ex.Message));
            throw;
        }
    }

    private static DotnetProcessTraceRecord CreateTraceRecord(
        string eventType,
        DotnetOperationKind operation,
        string workingDirectory,
        string? target,
        string arguments,
        DateTimeOffset startedAt,
        int? processId,
        int? exitCode,
        TimeSpan timeoutBudget,
        TimeSpan elapsed,
        bool timedOut,
        bool killAttempted,
        bool? killConfirmed,
        bool orphanRisk,
        string? details)
    {
        return new DotnetProcessTraceRecord(
            OccurredAtUtc: DateTimeOffset.UtcNow,
            EventType: eventType,
            Operation: operation.ToString().ToLowerInvariant(),
            WorkingDirectory: Path.GetFullPath(workingDirectory),
            Target: string.IsNullOrWhiteSpace(target) ? null : Path.GetFullPath(target),
            Arguments: arguments,
            ProcessId: processId,
            ExitCode: exitCode,
            TimeoutBudgetSeconds: timeoutBudget.TotalSeconds,
            ElapsedSeconds: elapsed.TotalSeconds,
            TimedOut: timedOut,
            KillAttempted: killAttempted,
            KillConfirmed: killConfirmed,
            OrphanRisk: orphanRisk,
            Details: details);
    }

    private static void TryCancelOutputReads(Process process)
    {
        try
        {
            process.CancelOutputRead();
        }
        catch
        {
        }

        try
        {
            process.CancelErrorRead();
        }
        catch
        {
        }
    }

    private static int? TryGetProcessId(Process process)
    {
        try
        {
            return process.Id;
        }
        catch
        {
            return null;
        }
    }

    private static int? TryGetExitCode(Process process)
    {
        try
        {
            return process.HasExited ? process.ExitCode : null;
        }
        catch
        {
            return null;
        }
    }

    private static string CaptureCombinedOutput(StringBuilder output, StringBuilder error, object sync)
    {
        lock (sync)
        {
            if (error.Length == 0)
            {
                return output.ToString();
            }

            if (output.Length == 0)
            {
                return error.ToString();
            }

            return output.ToString() + Environment.NewLine + error;
        }
    }
}

internal enum DotnetOperationKind
{
    Restore,
    Build,
    Test
}

internal sealed record DotnetCommandResult(
    string Output,
    int? ExitCode,
    bool TimedOut,
    bool CanceledByCaller,
    bool KillAttempted,
    bool? KillConfirmed,
    bool OrphanRisk,
    TimeSpan TimeoutBudget,
    TimeSpan Elapsed);
