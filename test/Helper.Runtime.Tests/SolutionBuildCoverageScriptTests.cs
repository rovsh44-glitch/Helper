using System.Diagnostics;
using System.Text.Json;

namespace Helper.Runtime.Tests;

[Trait("Lane", "Fast")]
public sealed class SolutionBuildCoverageScriptTests
{
    [Fact]
    public void Fails_When_Project_Is_Missing_BuildCoverage()
    {
        using var temp = new TempDirectoryScope("helper_solution_coverage_");
        var solutionPath = CreateSolution(temp.Path, includeBuildCoverage: false);
        var reportPath = Path.Combine(temp.Path, "solution-coverage.json");

        var result = RunScript(solutionPath, reportPath);

        Assert.Equal(1, result.ExitCode);
        using var report = JsonDocument.Parse(File.ReadAllText(reportPath));
        Assert.Equal(2, report.RootElement.GetProperty("missingCoverageCount").GetInt32());
    }

    [Fact]
    public void Passes_When_All_Projects_Have_BuildCoverage()
    {
        using var temp = new TempDirectoryScope("helper_solution_coverage_");
        var solutionPath = CreateSolution(temp.Path, includeBuildCoverage: true);
        var reportPath = Path.Combine(temp.Path, "solution-coverage.json");

        var result = RunScript(solutionPath, reportPath);

        Assert.Equal(0, result.ExitCode);
        using var report = JsonDocument.Parse(File.ReadAllText(reportPath));
        Assert.Equal(0, report.RootElement.GetProperty("missingCoverageCount").GetInt32());
    }

    private static string CreateSolution(string root, bool includeBuildCoverage)
    {
        var projectDirectory = Path.Combine(root, "src", "Sample.Project");
        Directory.CreateDirectory(projectDirectory);
        File.WriteAllText(Path.Combine(projectDirectory, "Sample.Project.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");

        var lines = new List<string>
        {
            "Microsoft Visual Studio Solution File, Format Version 12.00",
            "# Visual Studio Version 17",
            "Project(\"{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}\") = \"Sample.Project\", \"src\\Sample.Project\\Sample.Project.csproj\", \"{11111111-1111-1111-1111-111111111111}\"",
            "EndProject",
            "Global",
            "\tGlobalSection(ProjectConfigurationPlatforms) = postSolution",
            "\t\t{11111111-1111-1111-1111-111111111111}.Debug|Any CPU.ActiveCfg = Debug|Any CPU",
            "\t\t{11111111-1111-1111-1111-111111111111}.Release|Any CPU.ActiveCfg = Release|Any CPU"
        };

        if (includeBuildCoverage)
        {
            lines.Add("\t\t{11111111-1111-1111-1111-111111111111}.Debug|Any CPU.Build.0 = Debug|Any CPU");
            lines.Add("\t\t{11111111-1111-1111-1111-111111111111}.Release|Any CPU.Build.0 = Release|Any CPU");
        }

        lines.Add("\tEndGlobalSection");
        lines.Add("EndGlobal");

        var solutionPath = Path.Combine(root, "Sample.sln");
        File.WriteAllLines(solutionPath, lines);
        return solutionPath;
    }

    private static (int ExitCode, string StdOut, string StdErr) RunScript(string solutionPath, string reportPath)
    {
        var scriptPath = TestWorkspaceRoot.ResolveFile("scripts", "check_solution_build_coverage.ps1");
        var arguments =
            $"-ExecutionPolicy Bypass -File \"{scriptPath}\" -SolutionPath \"{solutionPath}\" -ReportPath \"{reportPath}\"";
        return RunProcess(ResolvePowerShellExecutable(), arguments, Path.GetDirectoryName(solutionPath)!);
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
