using System.Diagnostics;
using System.Globalization;
using System.Text;
using Helper.Runtime.Core;

namespace Helper.Runtime.Infrastructure;

internal sealed class DotnetTestToolRunner
{
    public async Task<ToolExecutionResult> ExecuteAsync(DotnetTestInvocation invocation, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(invocation.Target))
        {
            return new ToolExecutionResult(false, string.Empty, "dotnet test target is missing.");
        }

        var helperRoot = HelperWorkspacePathResolver.ResolveHelperRoot();
        var artifactRoot = Path.Combine(
            helperRoot,
            "temp",
            "tooling",
            "dotnet_test",
            DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + "_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(artifactRoot);

        var powershellExecutable = HostCommandResolver.GetPowerShellExecutable();
        var scriptPath = HelperWorkspacePathResolver.ResolveWorkspaceFile(
            invocation.UseBatchedRunner ? Path.Combine("scripts", "run_dotnet_test_batched.ps1") : Path.Combine("scripts", "run_dotnet_test_with_monitor.ps1"),
            helperRoot);

        if (!File.Exists(scriptPath))
        {
            return new ToolExecutionResult(false, string.Empty, $"dotnet test runner script not found: {scriptPath}");
        }

        var resolvedLogPath = invocation.LogPath ?? Path.Combine(artifactRoot, "stdout.log");
        var resolvedErrorLogPath = invocation.ErrorLogPath ?? Path.Combine(artifactRoot, "stderr.log");
        var resolvedStatusPath = invocation.StatusPath ?? Path.Combine(artifactRoot, "status.json");
        var resolvedResultsRoot = invocation.ResultsRoot ?? Path.Combine(artifactRoot, "results");
        EnsureParentDirectory(resolvedLogPath);
        EnsureParentDirectory(resolvedErrorLogPath);
        EnsureParentDirectory(resolvedStatusPath);

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = powershellExecutable,
                WorkingDirectory = helperRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = !invocation.ShowProcessMonitor
            }
        };
        process.StartInfo.ArgumentList.Add("-NoProfile");
        process.StartInfo.ArgumentList.Add("-ExecutionPolicy");
        process.StartInfo.ArgumentList.Add("Bypass");
        process.StartInfo.ArgumentList.Add("-Command");
        process.StartInfo.ArgumentList.Add(BuildPowerShellCommand(
            scriptPath,
            invocation,
            resolvedLogPath,
            resolvedErrorLogPath,
            resolvedStatusPath,
            resolvedResultsRoot));

        using var cancellationRegistration = ct.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
            }
        });

        process.Start();
        string stdout = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
        string stderr = await process.StandardError.ReadToEndAsync().ConfigureAwait(false);
        await process.WaitForExitAsync().ConfigureAwait(false);

        var output = BuildResultOutput(invocation, resolvedLogPath, resolvedErrorLogPath, resolvedStatusPath, stdout, stderr);
        return process.ExitCode == 0
            ? new ToolExecutionResult(true, output)
            : new ToolExecutionResult(false, output, $"dotnet test runner exited with code {process.ExitCode}.");
    }

    private static string BuildPowerShellCommand(
        string scriptPath,
        DotnetTestInvocation invocation,
        string logPath,
        string errorLogPath,
        string statusPath,
        string resultsRoot)
    {
        var builder = new StringBuilder();
        builder.Append("& ").Append(ToPowerShellSingleQuotedLiteral(scriptPath));
        builder.Append(" -Target ").Append(ToPowerShellSingleQuotedLiteral(invocation.Target));

        if (invocation.UseBatchedRunner)
        {
            var baseArguments = invocation.GetBaseArgumentsWithDefaults();
            builder.Append(" -BaseArguments ").Append(ToPowerShellArrayLiteral(baseArguments));
            if (invocation.ClassNames.Count > 0)
            {
                builder.Append(" -ClassNames ").Append(ToPowerShellArrayLiteral(invocation.ClassNames));
            }

            builder.Append(" -ResultsRoot ").Append(ToPowerShellSingleQuotedLiteral(resultsRoot));
        }
        else
        {
            var effectiveArguments = invocation.GetEffectiveArguments();
            builder.Append(" -Arguments ").Append(ToPowerShellArrayLiteral(effectiveArguments));
        }

        builder.Append(" -LogPath ").Append(ToPowerShellSingleQuotedLiteral(logPath));
        builder.Append(" -ErrorLogPath ").Append(ToPowerShellSingleQuotedLiteral(errorLogPath));
        builder.Append(" -StatusPath ").Append(ToPowerShellSingleQuotedLiteral(statusPath));
        builder.Append(" -MaxDurationSec ").Append(invocation.MaxDurationSec.ToString(CultureInfo.InvariantCulture));
        if (invocation.ShowProcessMonitor)
        {
            builder.Append(" -ShowProcessMonitor");
        }

        return builder.ToString();
    }

    private static string BuildResultOutput(
        DotnetTestInvocation invocation,
        string logPath,
        string errorLogPath,
        string statusPath,
        string stdout,
        string stderr)
    {
        var builder = new StringBuilder();
        builder.AppendLine("[dotnet_test]");
        builder.AppendLine("Command: " + invocation.BuildCommandDisplay());
        builder.AppendLine("LogPath: " + logPath);
        builder.AppendLine("ErrorLogPath: " + errorLogPath);
        builder.AppendLine("StatusPath: " + statusPath);

        if (!string.IsNullOrWhiteSpace(stdout))
        {
            builder.AppendLine();
            builder.AppendLine(stdout.Trim());
        }

        if (!string.IsNullOrWhiteSpace(stderr))
        {
            builder.AppendLine();
            builder.AppendLine("stderr:");
            builder.AppendLine(stderr.Trim());
        }

        return builder.ToString().TrimEnd();
    }

    private static string ToPowerShellArrayLiteral(IEnumerable<string> values)
        => "@(" + string.Join(", ", values.Select(ToPowerShellSingleQuotedLiteral)) + ")";

    private static string ToPowerShellSingleQuotedLiteral(string value)
        => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

    private static void EnsureParentDirectory(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }
}
