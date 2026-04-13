using System.Diagnostics;
using System.Linq;
using System.Text.Json;

namespace Helper.Runtime.Tests;

[Trait("Lane", "Fast")]
public sealed class NugetSecurityGateScriptTests
{
    [Fact]
    public void StrictOnline_Fails_WhenAuditDataIsUnavailable()
    {
        using var temp = new TempDirectoryScope("helper_nuget_gate_");
        var restoreOutputPath = Path.Combine(temp.Path, "restore.log");
        File.WriteAllText(restoreOutputPath, "warning NU1900: Package vulnerability data could not be retrieved.");
        var reportPath = Path.Combine(temp.Path, "report.json");

        var result = RunNugetGate(
            temp.Path,
            "strict-online",
            reportPath,
            restoreOutputPath,
            simulatedRestoreExitCode: 0,
            listOutputPath: null,
            simulatedListExitCode: null,
            additionalArguments: "-ClearProxyEnvironment");

        Assert.Equal(1, result.ExitCode);
        using var report = JsonDocument.Parse(File.ReadAllText(reportPath));
        Assert.Equal("audit_failed_infrastructure_unavailable", report.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public void BestEffortLocal_Degrades_WhenAuditDataIsUnavailable()
    {
        using var temp = new TempDirectoryScope("helper_nuget_gate_");
        var restoreOutputPath = Path.Combine(temp.Path, "restore.log");
        File.WriteAllText(restoreOutputPath, "warning NU1900: Package vulnerability data could not be retrieved.");
        var reportPath = Path.Combine(temp.Path, "report.json");

        var result = RunNugetGate(
            temp.Path,
            "best-effort-local",
            reportPath,
            restoreOutputPath,
            simulatedRestoreExitCode: 0,
            listOutputPath: null,
            simulatedListExitCode: null,
            additionalArguments: "-ClearProxyEnvironment");

        Assert.Equal(0, result.ExitCode);
        using var report = JsonDocument.Parse(File.ReadAllText(reportPath));
        Assert.Equal("audit_degraded_infrastructure_unavailable", report.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public void BestEffortLocal_ShortCircuits_When_AllConfiguredProxies_AreDeadLoopback()
    {
        using var temp = new TempDirectoryScope("helper_nuget_gate_");
        var reportPath = Path.Combine(temp.Path, "report.json");

        var result = RunNugetGate(
            temp.Path,
            "best-effort-local",
            reportPath,
            restoreOutputPath: string.Empty,
            simulatedRestoreExitCode: 0,
            listOutputPath: null,
            simulatedListExitCode: null,
            environmentOverrides: new Dictionary<string, string?>
            {
                ["ALL_PROXY"] = "http://127.0.0.1:9",
                ["HTTP_PROXY"] = "http://127.0.0.1:9",
                ["HTTPS_PROXY"] = "http://127.0.0.1:9",
                ["GIT_HTTP_PROXY"] = "http://127.0.0.1:9",
                ["GIT_HTTPS_PROXY"] = "http://127.0.0.1:9"
            });

        Assert.Equal(0, result.ExitCode);
        using var report = JsonDocument.Parse(File.ReadAllText(reportPath));
        Assert.Equal("audit_skipped_local_offline_proxy_unavailable", report.RootElement.GetProperty("status").GetString());
        Assert.True(report.RootElement.GetProperty("auditDataUnavailable").GetBoolean());
        Assert.Equal(string.Empty, report.RootElement.GetProperty("restoreSummary").GetString());
    }

    [Fact]
    public void StrictOnline_FailsFast_When_AllConfiguredProxies_AreDeadLoopback()
    {
        using var temp = new TempDirectoryScope("helper_nuget_gate_");
        var reportPath = Path.Combine(temp.Path, "report.json");

        var result = RunNugetGate(
            temp.Path,
            "strict-online",
            reportPath,
            restoreOutputPath: string.Empty,
            simulatedRestoreExitCode: 0,
            listOutputPath: null,
            simulatedListExitCode: null,
            environmentOverrides: new Dictionary<string, string?>
            {
                ["ALL_PROXY"] = "http://127.0.0.1:9",
                ["HTTP_PROXY"] = "http://127.0.0.1:9",
                ["HTTPS_PROXY"] = "http://127.0.0.1:9",
                ["GIT_HTTP_PROXY"] = "http://127.0.0.1:9",
                ["GIT_HTTPS_PROXY"] = "http://127.0.0.1:9"
            });

        Assert.Equal(1, result.ExitCode);
        using var report = JsonDocument.Parse(File.ReadAllText(reportPath));
        Assert.Equal("audit_failed_proxy_misconfigured", report.RootElement.GetProperty("status").GetString());
        Assert.True(report.RootElement.GetProperty("auditDataUnavailable").GetBoolean());
        Assert.Equal(string.Empty, report.RootElement.GetProperty("restoreSummary").GetString());
    }

    [Fact]
    public void StrictOnline_Fails_WhenAuditSourceProvidesNoData()
    {
        using var temp = new TempDirectoryScope("helper_nuget_gate_");
        var restoreOutputPath = Path.Combine(temp.Path, "restore.log");
        File.WriteAllText(restoreOutputPath, "warning NU1905: Audit source 'nuget.org' did not provide any vulnerability data.");
        var reportPath = Path.Combine(temp.Path, "report.json");

        var result = RunNugetGate(
            temp.Path,
            "strict-online",
            reportPath,
            restoreOutputPath,
            simulatedRestoreExitCode: 0,
            listOutputPath: null,
            simulatedListExitCode: null,
            additionalArguments: "-ClearProxyEnvironment");

        Assert.Equal(1, result.ExitCode);
        using var report = JsonDocument.Parse(File.ReadAllText(reportPath));
        Assert.Equal("audit_failed_audit_source_unavailable", report.RootElement.GetProperty("status").GetString());
        Assert.Contains("NU1905", report.RootElement.GetProperty("auditWarningCodes").EnumerateArray().Select(x => x.GetString()));
    }

    [Fact]
    public void BestEffortLocal_Degrades_WhenAuditSourceProvidesNoData()
    {
        using var temp = new TempDirectoryScope("helper_nuget_gate_");
        var restoreOutputPath = Path.Combine(temp.Path, "restore.log");
        File.WriteAllText(restoreOutputPath, "warning NU1905: Audit source 'nuget.org' did not provide any vulnerability data.");
        var reportPath = Path.Combine(temp.Path, "report.json");

        var result = RunNugetGate(
            temp.Path,
            "best-effort-local",
            reportPath,
            restoreOutputPath,
            simulatedRestoreExitCode: 0,
            listOutputPath: null,
            simulatedListExitCode: null,
            additionalArguments: "-ClearProxyEnvironment");

        Assert.Equal(0, result.ExitCode);
        using var report = JsonDocument.Parse(File.ReadAllText(reportPath));
        Assert.Equal("audit_degraded_audit_source_unavailable", report.RootElement.GetProperty("status").GetString());
        Assert.Contains("NU1905", report.RootElement.GetProperty("auditWarningCodes").EnumerateArray().Select(x => x.GetString()));
    }

    [Fact]
    public void Fails_WhenVulnerabilitiesAreReported()
    {
        using var temp = new TempDirectoryScope("helper_nuget_gate_");
        var restoreOutputPath = Path.Combine(temp.Path, "restore.log");
        var listOutputPath = Path.Combine(temp.Path, "list.json");
        File.WriteAllText(restoreOutputPath, "restore ok");
        File.WriteAllText(
            listOutputPath,
            """
{"projects":[{"path":"src/Helper.Api/Helper.Api.csproj","frameworks":[{"framework":"net8.0","topLevelPackages":[{"id":"Sample.Package","vulnerabilities":[{"severity":"high"}]}],"transitivePackages":[]}]}]}
""");
        var reportPath = Path.Combine(temp.Path, "report.json");

        var result = RunNugetGate(
            temp.Path,
            "strict-online",
            reportPath,
            restoreOutputPath,
            simulatedRestoreExitCode: 0,
            listOutputPath: listOutputPath,
            simulatedListExitCode: 0,
            additionalArguments: "-ClearProxyEnvironment");

        Assert.Equal(1, result.ExitCode);
        using var report = JsonDocument.Parse(File.ReadAllText(reportPath));
        Assert.Equal("audit_failed_vulnerabilities_found", report.RootElement.GetProperty("status").GetString());
        Assert.Single(report.RootElement.GetProperty("vulnerablePackages").EnumerateArray());
    }

    [Fact]
    public void Passes_WhenReportIsClean()
    {
        using var temp = new TempDirectoryScope("helper_nuget_gate_");
        var restoreOutputPath = Path.Combine(temp.Path, "restore.log");
        var listOutputPath = Path.Combine(temp.Path, "list.json");
        File.WriteAllText(restoreOutputPath, "restore ok");
        File.WriteAllText(
            listOutputPath,
            """
{"projects":[{"path":"src/Helper.Api/Helper.Api.csproj","frameworks":[{"framework":"net8.0","topLevelPackages":[],"transitivePackages":[]}]}]}
""");
        var reportPath = Path.Combine(temp.Path, "report.json");

        var result = RunNugetGate(
            temp.Path,
            "strict-online",
            reportPath,
            restoreOutputPath,
            simulatedRestoreExitCode: 0,
            listOutputPath: listOutputPath,
            simulatedListExitCode: 0,
            additionalArguments: "-ClearProxyEnvironment");

        Assert.Equal(0, result.ExitCode);
        using var report = JsonDocument.Parse(File.ReadAllText(reportPath));
        Assert.Equal("audit_passed", report.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public void ClearProxyEnvironment_RemovesConfiguredProxyVariables_FromGateProcess()
    {
        using var temp = new TempDirectoryScope("helper_nuget_gate_");
        var restoreOutputPath = Path.Combine(temp.Path, "restore.log");
        var listOutputPath = Path.Combine(temp.Path, "list.json");
        File.WriteAllText(restoreOutputPath, "restore ok");
        File.WriteAllText(
            listOutputPath,
            """
{"projects":[{"path":"src/Helper.Api/Helper.Api.csproj","frameworks":[{"framework":"net8.0","topLevelPackages":[],"transitivePackages":[]}]}]}
""");
        var reportPath = Path.Combine(temp.Path, "report.json");

        var result = RunNugetGate(
            temp.Path,
            "strict-online",
            reportPath,
            restoreOutputPath,
            simulatedRestoreExitCode: 0,
            listOutputPath: listOutputPath,
            simulatedListExitCode: 0,
            additionalArguments: "-ClearProxyEnvironment",
            environmentOverrides: new Dictionary<string, string?>
            {
                ["ALL_PROXY"] = "http://127.0.0.1:9",
                ["HTTP_PROXY"] = "http://127.0.0.1:9",
                ["HTTPS_PROXY"] = "http://127.0.0.1:9",
                ["GIT_HTTP_PROXY"] = "http://127.0.0.1:9",
                ["GIT_HTTPS_PROXY"] = "http://127.0.0.1:9"
            });

        Assert.Equal(0, result.ExitCode);
        using var report = JsonDocument.Parse(File.ReadAllText(reportPath));
        Assert.All(
            report.RootElement.GetProperty("proxyEnvironment").EnumerateArray(),
            entry => Assert.False(entry.GetProperty("configured").GetBoolean()));
    }

    private static (int ExitCode, string StdOut, string StdErr) RunNugetGate(
        string workspaceRoot,
        string executionMode,
        string reportPath,
        string restoreOutputPath,
        int simulatedRestoreExitCode,
        string? listOutputPath,
        int? simulatedListExitCode,
        string additionalArguments = "",
        IReadOnlyDictionary<string, string?>? environmentOverrides = null)
    {
        var scriptPath = TestWorkspaceRoot.ResolveFile("scripts", "nuget_security_gate.ps1");
        var arguments =
            $"-ExecutionPolicy Bypass -File \"{scriptPath}\" -ExecutionMode {executionMode} -ReportPath \"{reportPath}\" -SimulatedRestoreExitCode {simulatedRestoreExitCode}";
        if (!string.IsNullOrWhiteSpace(restoreOutputPath))
        {
            arguments += $" -SimulatedRestoreOutputPath \"{restoreOutputPath}\"";
        }

        if (!string.IsNullOrWhiteSpace(listOutputPath))
        {
            arguments += $" -SimulatedListOutputPath \"{listOutputPath}\"";
        }

        if (simulatedListExitCode.HasValue)
        {
            arguments += $" -SimulatedListExitCode {simulatedListExitCode.Value}";
        }

        if (!string.IsNullOrWhiteSpace(additionalArguments))
        {
            arguments += $" {additionalArguments}";
        }

        return RunProcess(ResolvePowerShellExecutable(), arguments, workspaceRoot, environmentOverrides);
    }

    private static (int ExitCode, string StdOut, string StdErr) RunProcess(
        string fileName,
        string arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string?>? environmentOverrides = null)
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

        if (environmentOverrides is not null)
        {
            foreach (var pair in environmentOverrides)
            {
                process.StartInfo.Environment[pair.Key] = pair.Value ?? string.Empty;
            }
        }

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
