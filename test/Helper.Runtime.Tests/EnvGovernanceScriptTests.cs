using System.Diagnostics;

namespace Helper.Runtime.Tests;

[Trait("Lane", "Fast")]
public sealed class EnvGovernanceScriptTests
{
    [Fact]
    public void MissingLocalEnvPath_DoesNotCrash_OrThrow_PropertyAccessErrors()
    {
        var missingLocalEnvPath = Path.Combine(
            Path.GetTempPath(),
            "helper_missing_env",
            Guid.NewGuid().ToString("N"),
            ".env.local");

        var scriptPath = TestWorkspaceRoot.ResolveFile("scripts", "check_env_governance.ps1");
        var arguments =
            $"-ExecutionPolicy Bypass -File \"{scriptPath}\" -LocalEnvPath \"{missingLocalEnvPath}\"";

        var result = RunProcess(ResolvePowerShellExecutable(), arguments, TestWorkspaceRoot.ResolveRoot());

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("The property 'Count' cannot be found", result.StdOut + result.StdErr, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Environment inventory and governance checks passed", result.StdOut, StringComparison.OrdinalIgnoreCase);
    }

    private static (int ExitCode, string StdOut, string StdErr) RunProcess(string fileName, string arguments, string workingDirectory)
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
