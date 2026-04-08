using System.Text.RegularExpressions;
using Helper.Runtime.Core;

namespace Helper.Runtime.Infrastructure;

internal static class DotnetProcessResultMapper
{
    private static readonly Regex BuildErrorRegex = new(
        @"^(?<file>.+?)\((?<line>\d+),\d+\):\s+error\s+(?<code>[A-Z]{2}\d{4}):\s+(?<message>.+?)\s+\[.+\]$",
        RegexOptions.Compiled);

    public static void EnsureRestoreSucceeded(
        DotnetCommandResult result,
        DotnetOperationKind operation,
        string? target,
        CancellationToken ct)
    {
        if (result.CanceledByCaller)
        {
            ct.ThrowIfCancellationRequested();
        }

        if (result.TimedOut)
        {
            throw new TimeoutException(CreateTimeoutMessage(operation, target, result));
        }

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(CreateFailureMessage(operation, target, result));
        }
    }

    public static List<BuildError> MapBuildResult(DotnetCommandResult result, string? target, CancellationToken ct)
    {
        if (result.CanceledByCaller)
        {
            ct.ThrowIfCancellationRequested();
        }

        if (result.TimedOut)
        {
            return new List<BuildError>
            {
                new("Build", 0, "GENERATION_STAGE_TIMEOUT", CreateTimeoutMessage(DotnetOperationKind.Build, target, result))
            };
        }

        var parsedErrors = ParseBuildErrors(result.Output);
        if (parsedErrors.Count > 0)
        {
            return parsedErrors;
        }

        if (result.ExitCode != 0)
        {
            return new List<BuildError>
            {
                new("Build", 0, "DOTNET_BUILD_FAILED", CreateFailureMessage(DotnetOperationKind.Build, target, result))
            };
        }

        return parsedErrors;
    }

    public static TestReport MapTestResult(DotnetCommandResult result, CancellationToken ct)
    {
        if (result.CanceledByCaller)
        {
            ct.ThrowIfCancellationRequested();
        }

        var output = result.TimedOut
            ? CreateTimeoutMessage(DotnetOperationKind.Test, target: null, result) + Environment.NewLine + result.Output
            : result.Output;
        var success = !result.TimedOut &&
                      result.ExitCode == 0 &&
                      output.Contains("Passed!", StringComparison.OrdinalIgnoreCase) &&
                      !output.Contains("Failed!", StringComparison.OrdinalIgnoreCase);
        var passed = output.Contains("Passed!", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        var failed = success ? 0 : 1;

        return new TestReport(success, passed, failed, new List<string> { output });
    }

    private static string CreateTimeoutMessage(DotnetOperationKind operation, string? target, DotnetCommandResult result)
        => CreateTimeoutMessage(operation, target, result.TimeoutBudget, result.Elapsed, result.KillConfirmed, result.OrphanRisk);

    private static string CreateTimeoutMessage(
        DotnetOperationKind operation,
        string? target,
        TimeSpan timeoutBudget,
        TimeSpan elapsed,
        bool? killConfirmed,
        bool orphanRisk)
    {
        var targetLabel = string.IsNullOrWhiteSpace(target) ? "(workspace)" : Path.GetFileName(target);
        return $"GENERATION_STAGE_TIMEOUT: dotnet {operation.ToString().ToLowerInvariant()} exceeded timeout budget of {timeoutBudget.TotalSeconds:0}s after {elapsed.TotalSeconds:0.0}s. target={targetLabel}; killConfirmed={(killConfirmed.HasValue ? killConfirmed.Value.ToString().ToLowerInvariant() : "unknown")}; orphanRisk={orphanRisk.ToString().ToLowerInvariant()}; trace={DotnetProcessTraceWriter.TraceFileName}.";
    }

    private static string CreateFailureMessage(DotnetOperationKind operation, string? target, DotnetCommandResult result)
    {
        var targetLabel = string.IsNullOrWhiteSpace(target) ? "(workspace)" : Path.GetFileName(target);
        return $"dotnet {operation.ToString().ToLowerInvariant()} failed for {targetLabel} with exit code {result.ExitCode?.ToString() ?? "unknown"}. trace={DotnetProcessTraceWriter.TraceFileName}. output_tail={Tail(result.Output)}";
    }

    private static string Tail(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "(empty)";
        }

        var normalized = text.ReplaceLineEndings(" ").Trim();
        return normalized.Length <= 280 ? normalized : normalized[^280..];
    }

    private static List<BuildError> ParseBuildErrors(string output)
    {
        var errors = new List<BuildError>();
        foreach (var line in output.Split('\n'))
        {
            if (!line.Contains(": error ", StringComparison.Ordinal))
            {
                continue;
            }

            var trimmed = line.Trim();
            var match = BuildErrorRegex.Match(trimmed);
            if (match.Success)
            {
                var file = match.Groups["file"].Value;
                var lineNumber = int.TryParse(match.Groups["line"].Value, out var parsedLine) ? parsedLine : 0;
                var code = match.Groups["code"].Value;
                var message = match.Groups["message"].Value;
                errors.Add(new BuildError(file, lineNumber, code, message));
                continue;
            }

            var codeMatch = Regex.Match(trimmed, @"\b(?<code>[A-Z]{2}\d{4})\b");
            var fallbackCode = codeMatch.Success ? codeMatch.Groups["code"].Value : "FAIL";
            errors.Add(new BuildError("Build", 0, fallbackCode, trimmed));
        }

        return errors;
    }
}
