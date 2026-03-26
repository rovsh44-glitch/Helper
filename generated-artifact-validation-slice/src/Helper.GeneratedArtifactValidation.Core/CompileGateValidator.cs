using System.Diagnostics;
using System.Text.RegularExpressions;
using Helper.GeneratedArtifactValidation.Contracts;

namespace Helper.GeneratedArtifactValidation.Core;

public sealed class CompileGateValidator
{
    private static readonly Regex BuildErrorRegex = new(
        @"^(?<file>.+?)\((?<line>\d+),\d+\):\s+error\s+(?<code>[A-Z]{2}\d{4}):\s+(?<message>.+?)\s+\[.+\]$",
        RegexOptions.Compiled);

    public async Task<CompileGateResult> ValidateAsync(string sourceRoot, CancellationToken ct = default)
    {
        if (!Directory.Exists(sourceRoot))
        {
            return new CompileGateResult(false, [new BuildError("CompileGate", 0, "PROJECT_ROOT_MISSING", $"Project root '{sourceRoot}' does not exist.")], string.Empty);
        }

        var compileWorkspace = Path.Combine(Path.GetTempPath(), "helper_stage2_compile_gate_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(compileWorkspace);
        CopyDirectorySkippingBuildArtifacts(sourceRoot, compileWorkspace);

        var target = ResolveBuildTarget(compileWorkspace);
        if (target is null)
        {
            return new CompileGateResult(false, [new BuildError("CompileGate", 0, "PROJECT_NOT_FOUND", "No .sln or .csproj file was found in the sample project.")], compileWorkspace);
        }

        var (exitCode, output, timedOut) = await RunDotnetBuildAsync(compileWorkspace, target, ct);
        var errors = ParseBuildErrors(output, compileWorkspace);

        if (timedOut)
        {
            errors.Add(new BuildError("CompileGate", 0, "BUILD_TIMEOUT", "dotnet build timed out while validating the sample project."));
        }
        else if (exitCode != 0 && errors.Count == 0)
        {
            errors.Add(new BuildError("CompileGate", 0, "BUILD_FAILED", "dotnet build failed but did not emit a parseable compiler diagnostic."));
        }

        return new CompileGateResult(exitCode == 0 && errors.Count == 0, errors, compileWorkspace);
    }

    private static string? ResolveBuildTarget(string compileWorkspace)
    {
        var topLevelSolution = Directory.GetFiles(compileWorkspace, "*.sln", SearchOption.TopDirectoryOnly).FirstOrDefault();
        if (topLevelSolution is not null)
        {
            return topLevelSolution;
        }

        var topLevelProject = Directory.GetFiles(compileWorkspace, "*.csproj", SearchOption.TopDirectoryOnly).FirstOrDefault();
        if (topLevelProject is not null)
        {
            return topLevelProject;
        }

        return Directory.GetFiles(compileWorkspace, "*.csproj", SearchOption.AllDirectories).FirstOrDefault();
    }

    private static async Task<(int ExitCode, string Output, bool TimedOut)> RunDotnetBuildAsync(
        string workingDirectory,
        string targetPath,
        CancellationToken ct)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"""build "{targetPath}" -c Debug --nologo --verbosity minimal -p:RestoreAudit=false""",
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(120));

        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(true);
            }
            catch
            {
            }

            var timeoutOutput = await stdoutTask + Environment.NewLine + await stderrTask;
            return (-1, timeoutOutput, true);
        }

        var output = await stdoutTask + Environment.NewLine + await stderrTask;
        return (process.ExitCode, output, false);
    }

    private static List<BuildError> ParseBuildErrors(string output, string compileWorkspace)
    {
        var errors = new List<BuildError>();
        foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
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
                if (file.StartsWith(compileWorkspace, StringComparison.OrdinalIgnoreCase))
                {
                    file = Path.GetRelativePath(compileWorkspace, file).Replace(Path.DirectorySeparatorChar, '/');
                }

                var lineNumber = int.TryParse(match.Groups["line"].Value, out var parsedLine) ? parsedLine : 0;
                errors.Add(new BuildError(file, lineNumber, match.Groups["code"].Value, match.Groups["message"].Value));
                continue;
            }

            var codeMatch = Regex.Match(trimmed, @"\b(?<code>[A-Z]{2}\d{4})\b");
            var fallbackCode = codeMatch.Success ? codeMatch.Groups["code"].Value : "BUILD";
            errors.Add(new BuildError("Build", 0, fallbackCode, trimmed));
        }

        return errors;
    }

    private static void CopyDirectorySkippingBuildArtifacts(string sourceRoot, string targetRoot)
    {
        foreach (var directory in Directory.GetDirectories(sourceRoot, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceRoot, directory);
            if (ShouldSkipRelativePath(relative))
            {
                continue;
            }

            Directory.CreateDirectory(Path.Combine(targetRoot, relative));
        }

        foreach (var file in Directory.GetFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceRoot, file);
            if (ShouldSkipRelativePath(relative))
            {
                continue;
            }

            var destination = Path.Combine(targetRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, true);
        }
    }

    private static bool ShouldSkipRelativePath(string relativePath)
    {
        var parts = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Any(part =>
            string.Equals(part, "bin", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(part, "obj", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(part, ".compile_gate", StringComparison.OrdinalIgnoreCase));
    }
}
