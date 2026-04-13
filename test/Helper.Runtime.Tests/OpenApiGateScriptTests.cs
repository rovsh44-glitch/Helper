using System.Diagnostics;

namespace Helper.Runtime.Tests;

[Trait("Lane", "Fast")]
public sealed class OpenApiGateScriptTests
{
    [Fact]
    public void Fails_WhenFilterMatchesNoTests()
    {
        using var temp = new TempDirectoryScope("helper_openapi_gate_");
        var outputPath = Path.Combine(temp.Path, "dotnet-test.log");
        File.WriteAllText(outputPath, "No test matches the given testcase filter `Category=Contract`.");

        var result = RunOpenApiGate(temp.Path, outputPath, simulatedExitCode: 0);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("matched no tests", CombineOutput(result), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Fails_WhenTestSourceWasNotBuilt()
    {
        using var temp = new TempDirectoryScope("helper_openapi_gate_");
        var outputPath = Path.Combine(temp.Path, "dotnet-test.log");
        File.WriteAllText(outputPath, "The test source file C:\\temp\\missing.dll was not found.");

        var result = RunOpenApiGate(temp.Path, outputPath, simulatedExitCode: 1);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Test source was not built or was missing", CombineOutput(result), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Passes_WhenContractRunIsClean()
    {
        using var temp = new TempDirectoryScope("helper_openapi_gate_");
        var outputPath = Path.Combine(temp.Path, "dotnet-test.log");
        File.WriteAllText(outputPath, "Passed! 3 tests executed.");

        var result = RunOpenApiGate(temp.Path, outputPath, simulatedExitCode: 0);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("[OpenApiGate] Passed.", result.StdOut, StringComparison.Ordinal);
    }

    private static (int ExitCode, string StdOut, string StdErr) RunOpenApiGate(
        string workspaceRoot,
        string simulatedOutputPath,
        int simulatedExitCode)
    {
        var scriptPath = TestWorkspaceRoot.ResolveFile("scripts", "openapi_gate.ps1");
        var arguments =
            $"-ExecutionPolicy Bypass -File \"{scriptPath}\" -Target \"test/Helper.Runtime.Api.Tests/Helper.Runtime.Api.Tests.csproj\" -SimulatedOutputPath \"{simulatedOutputPath}\" -SimulatedExitCode {simulatedExitCode}";

        return RunProcess(ResolvePowerShellExecutable(), arguments, workspaceRoot);
    }

    private static (int ExitCode, string StdOut, string StdErr) RunProcess(
        string fileName,
        string arguments,
        string workingDirectory)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, stdout, stderr);
    }

    private static string CombineOutput((int ExitCode, string StdOut, string StdErr) result)
    {
        return string.Join(
            Environment.NewLine,
            new[] { result.StdOut, result.StdErr }.Where(text => !string.IsNullOrWhiteSpace(text)));
    }

    private static string ResolvePowerShellExecutable()
    {
        if (OperatingSystem.IsWindows())
        {
            var systemPowerShell = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "WindowsPowerShell",
                "v1.0",
                "powershell.exe");
            if (File.Exists(systemPowerShell))
            {
                return systemPowerShell;
            }

            return "powershell";
        }

        return "pwsh";
    }
}
