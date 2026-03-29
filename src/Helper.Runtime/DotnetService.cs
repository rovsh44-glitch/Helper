using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Helper.Runtime.Core;

namespace Helper.Runtime.Infrastructure
{
    public class DotnetService : IDotnetService
    {
        private static readonly Regex BuildErrorRegex = new(
            @"^(?<file>.+?)\((?<line>\d+),\d+\):\s+error\s+(?<code>[A-Z]{2}\d{4}):\s+(?<message>.+?)\s+\[.+\]$",
            RegexOptions.Compiled);

        public async Task RestoreAsync(string workingDirectory, CancellationToken ct = default)
        {
            var arguments = DotnetSandboxSupport.AppendSandboxIntermediateProperties("restore", workingDirectory);
            await RunCommandAsync(arguments, workingDirectory, ct);
        }

        public async Task<List<BuildError>> BuildAsync(string workingDirectory, CancellationToken ct = default)
        {
            var sln = Directory.GetFiles(workingDirectory, "*.sln", SearchOption.AllDirectories).FirstOrDefault();
            var csproj = Directory.GetFiles(workingDirectory, "*.csproj", SearchOption.AllDirectories).FirstOrDefault();
            var target = sln ?? csproj;

            if (target == null) return new List<BuildError>();

            var arguments = DotnetSandboxSupport.AppendSandboxIntermediateProperties(
                $@"build ""{target}"" -c Debug --no-incremental -p:RestoreAudit=false",
                workingDirectory);
            var output = await RunCommandAsync(arguments, workingDirectory, ct);
            return ParseBuildErrors(output);
        }

        public async Task<TestReport> TestAsync(string workingDirectory, CancellationToken ct = default)
        {
            var arguments = DotnetSandboxSupport.AppendSandboxIntermediateProperties(
                @"test --logger ""console;verbosity=normal""",
                workingDirectory);
            var output = await RunCommandAsync(arguments, workingDirectory, ct);
            
            bool success = output.Contains("Passed!") && !output.Contains("Failed!");
            int passed = output.Contains("Passed!") ? 1 : 0; 
            int failed = output.Contains("Failed!") ? 1 : 0;

            return new TestReport(success, passed, failed, new List<string> { output });
        }

        private async Task<string> RunCommandAsync(string arguments, string workingDirectory, CancellationToken ct)
        {
            var processStart = new Process
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

            processStart.Start();
            
            var outputTask = processStart.StandardOutput.ReadToEndAsync();
            var errorTask = processStart.StandardError.ReadToEndAsync();

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(180));

            try 
            {
                await processStart.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                try { processStart.Kill(true); }
                catch (Exception ex) { Console.WriteLine($"[DotnetService] Failed to terminate timed out process: {ex.Message}"); }
                return "Error: Timeout";
            }
            
            var outText = await outputTask;
            var errText = await errorTask;
            return outText + "\n" + errText;
        }

        private List<BuildError> ParseBuildErrors(string output)
        {
            var errors = new List<BuildError>();
            foreach (var line in output.Split('\n'))
            {
                if (line.Contains(": error "))
                {
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
            }
            return errors;
        }
    }
}

