using System.Diagnostics;
using System.Text.Json;

namespace Helper.Runtime.Tests;

public sealed class SecretScanScriptTests
{
    [Fact]
    public void WorkspaceMode_Fails_On_Unquoted_HelperApiKey()
    {
        using var temp = new TempDirectoryScope("helper_secret_scan_");
        File.WriteAllText(Path.Combine(temp.Path, ".env.local"), "HELPER_API_KEY=helper-real-key-1234567890");

        var reportPath = Path.Combine(temp.Path, "secret-scan-report.json");
        var result = RunSecretScan(temp.Path, "workspace", reportPath);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("mode=workspace", result.StdOut, StringComparison.OrdinalIgnoreCase);

        using var report = JsonDocument.Parse(File.ReadAllText(reportPath));
        Assert.Equal("workspace", report.RootElement.GetProperty("scanMode").GetString());
        Assert.Contains(
            report.RootElement.GetProperty("hits").EnumerateArray(),
            hit => hit.GetProperty("File").GetString()!.EndsWith(".env.local", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void WorkspaceMode_Fails_On_Unquoted_SessionSigningKey()
    {
        using var temp = new TempDirectoryScope("helper_secret_scan_");
        File.WriteAllText(Path.Combine(temp.Path, ".env.local"), "HELPER_SESSION_SIGNING_KEY=session-secret-abcdefghijklmnopqrstuvwxyz");

        var reportPath = Path.Combine(temp.Path, "secret-scan-report.json");
        var result = RunSecretScan(temp.Path, "workspace", reportPath);

        Assert.Equal(1, result.ExitCode);

        using var report = JsonDocument.Parse(File.ReadAllText(reportPath));
        Assert.Contains(
            report.RootElement.GetProperty("hits").EnumerateArray(),
            hit => hit.GetProperty("Pattern").GetString()!.Contains("HELPER_SESSION_SIGNING_KEY", StringComparison.Ordinal));
    }

    [Fact]
    public void WorkspaceMode_Allows_Placeholders_And_Tracked_Example()
    {
        using var temp = new TempDirectoryScope("helper_secret_scan_");
        File.WriteAllText(Path.Combine(temp.Path, ".env.local"), "HELPER_API_KEY=<set-me>\r\nHELPER_SESSION_SIGNING_KEY=\r\n");
        File.Copy(TestWorkspaceRoot.ResolveFile(".env.local.example"), Path.Combine(temp.Path, ".env.local.example"), overwrite: true);

        var reportPath = Path.Combine(temp.Path, "secret-scan-report.json");
        var result = RunSecretScan(temp.Path, "workspace", reportPath);

        Assert.Equal(0, result.ExitCode);

        using var report = JsonDocument.Parse(File.ReadAllText(reportPath));
        Assert.Empty(report.RootElement.GetProperty("hits").EnumerateArray());
    }

    [Fact]
    public void RepoMode_Ignores_Untracked_LocalEnv_But_Finds_Tracked_Secrets()
    {
        if (!IsCommandAvailable("git"))
        {
            return;
        }

        using var temp = new TempDirectoryScope("helper_secret_scan_repo_");
        RunProcessOrThrow("git", $"-C \"{temp.Path}\" init");

        File.WriteAllText(Path.Combine(temp.Path, ".env.local"), "HELPER_API_KEY=local-only-secret-value");
        var cleanReportPath = Path.Combine(temp.Path, "repo-clean-report.json");
        var cleanResult = RunSecretScan(temp.Path, "repo", cleanReportPath);
        Assert.Equal(0, cleanResult.ExitCode);

        var trackedPath = Path.Combine(temp.Path, "tracked.env");
        File.WriteAllText(trackedPath, "HELPER_SESSION_SIGNING_KEY=tracked-secret-value");
        RunProcessOrThrow("git", $"-C \"{temp.Path}\" add \"tracked.env\"");

        var reportPath = Path.Combine(temp.Path, "repo-secret-report.json");
        var result = RunSecretScan(temp.Path, "repo", reportPath);

        Assert.Equal(1, result.ExitCode);

        using var report = JsonDocument.Parse(File.ReadAllText(reportPath));
        var hits = report.RootElement.GetProperty("hits").EnumerateArray().ToList();
        Assert.DoesNotContain(hits, hit => hit.GetProperty("File").GetString()!.EndsWith(".env.local", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(hits, hit => hit.GetProperty("File").GetString()!.EndsWith("tracked.env", StringComparison.OrdinalIgnoreCase));
    }

    private static (int ExitCode, string StdOut, string StdErr) RunSecretScan(string workspaceRoot, string scanMode, string reportPath)
    {
        var scriptPath = TestWorkspaceRoot.ResolveFile("scripts", "secret_scan.ps1");
        var arguments =
            $"-ExecutionPolicy Bypass -File \"{scriptPath}\" -WorkspaceRoot \"{workspaceRoot}\" -ScanMode {scanMode} -ReportPath \"{reportPath}\"";
        return RunProcess(ResolvePowerShellExecutable(), arguments);
    }

    private static void RunProcessOrThrow(string fileName, string arguments)
    {
        var result = RunProcess(fileName, arguments);
        Assert.True(result.ExitCode == 0, $"{fileName} {arguments} failed with exit code {result.ExitCode}.{Environment.NewLine}{result.StdOut}{Environment.NewLine}{result.StdErr}");
    }

    private static (int ExitCode, string StdOut, string StdErr) RunProcess(string fileName, string arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
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

    private static bool IsCommandAvailable(string fileName)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = "--version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
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
