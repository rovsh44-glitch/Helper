using System.Diagnostics;
using System.Text.Json;

namespace Helper.Runtime.Tests;

public sealed class RootLayoutScriptTests
{
    [Fact]
    public void CheckRootLayout_FallbackMode_ReportsBuildOutputDirectories_AsWarnings()
    {
        var workspaceRoot = CreateTempWorkspaceRoot();
        try
        {
            Directory.CreateDirectory(Path.Combine(workspaceRoot, "src", "Sample.Project", "bin", "Debug"));
            var reportPath = Path.Combine(workspaceRoot, "root-layout-report.json");

            var result = RunRootLayoutScript(workspaceRoot, reportPath);

            Assert.Equal(0, result.ExitCode);

            using var report = JsonDocument.Parse(File.ReadAllText(reportPath));
            Assert.False(report.RootElement.GetProperty("gitMetadataAvailable").GetBoolean());
            Assert.Equal("fallback_pattern", report.RootElement.GetProperty("artifactScanMode").GetString());
            Assert.Contains(
                report.RootElement.GetProperty("sourceWarnings").EnumerateArray(),
                warning => warning.GetProperty("category").GetString() == "source_build_artifact_directory_fallback" &&
                           warning.GetProperty("path").GetString()!.Contains($"{Path.DirectorySeparatorChar}src{Path.DirectorySeparatorChar}Sample.Project{Path.DirectorySeparatorChar}bin", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            TryDeleteDirectory(workspaceRoot);
        }
    }

    [Fact]
    public void CheckRootLayout_GitMode_FailsOnTrackedArtifactsUnderSrc()
    {
        if (!IsCommandAvailable("git"))
        {
            return;
        }

        var workspaceRoot = CreateTempWorkspaceRoot();
        try
        {
            Directory.CreateDirectory(Path.Combine(workspaceRoot, "src", "Sample.Project", "bin", "Debug"));
            var trackedArtifactPath = Path.Combine(workspaceRoot, "src", "Sample.Project", "bin", "Debug", "tracked.txt");
            File.WriteAllText(trackedArtifactPath, "tracked artifact");

            RunProcessOrThrow("git", $"-C \"{workspaceRoot}\" init");
            RunProcessOrThrow("git", $"-C \"{workspaceRoot}\" add \"src/Sample.Project/bin/Debug/tracked.txt\"");

            var reportPath = Path.Combine(workspaceRoot, "root-layout-report.json");
            var result = RunRootLayoutScript(workspaceRoot, reportPath);

            Assert.Equal(1, result.ExitCode);

            using var report = JsonDocument.Parse(File.ReadAllText(reportPath));
            Assert.True(report.RootElement.GetProperty("gitMetadataAvailable").GetBoolean());
            Assert.Equal("git_tracked", report.RootElement.GetProperty("artifactScanMode").GetString());
            Assert.Contains(
                report.RootElement.GetProperty("sourceFatalViolations").EnumerateArray(),
                violation => violation.GetProperty("category").GetString() == "source_tracked_build_artifact" &&
                             violation.GetProperty("path").GetString()!.EndsWith(Path.Combine("src", "Sample.Project", "bin", "Debug", "tracked.txt"), StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            TryDeleteDirectory(workspaceRoot);
        }
    }

    private static (int ExitCode, string StdOut, string StdErr) RunRootLayoutScript(string workspaceRoot, string reportPath)
    {
        var scriptPath = ResolveWorkspaceFile("scripts", "check_root_layout.ps1");
        var budgetsPath = ResolveWorkspaceFile("scripts", "performance_budgets.json");
        var arguments =
            $"-ExecutionPolicy Bypass -File \"{scriptPath}\" -WorkspaceRoot \"{workspaceRoot}\" -BudgetsPath \"{budgetsPath}\" -ReportPath \"{reportPath}\"";
        return RunProcess("powershell", arguments);
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

    private static string CreateTempWorkspaceRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "helper-root-layout-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
        }
    }

    private static string ResolveWorkspaceFile(params string[] segments)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var marker = Path.Combine(current.FullName, "Helper.sln");
            if (File.Exists(marker))
            {
                return Path.Combine(new[] { current.FullName }.Concat(segments).ToArray());
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Workspace root was not found.");
    }
}

