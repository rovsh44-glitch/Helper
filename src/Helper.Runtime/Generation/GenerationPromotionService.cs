using System.Diagnostics;
using System.Text;
using Helper.Runtime.Infrastructure;

namespace Helper.Runtime.Generation;

public sealed class GenerationPromotionService : IGenerationPromotionService
{
    private readonly IGenerationMetricsService _metrics;
    private readonly string _workspaceRoot;

    public GenerationPromotionService(IGenerationMetricsService metrics)
    {
        _metrics = metrics;
        _workspaceRoot = HelperWorkspacePathResolver.ResolveHelperRoot();
    }

    public async Task<GenerationPromotionResult> PromoteAsync(GenerationPromotionRequest request, CancellationToken ct = default)
    {
        var steps = new List<string>();
        var errors = new List<string>();

        var source = Path.GetFullPath(request.SourceValidatedProjectPath);
        var target = Path.GetFullPath(request.TargetProjectPath);

        if (!Directory.Exists(source))
        {
            errors.Add($"Validated source does not exist: {source}");
            return new GenerationPromotionResult(false, steps, errors, null);
        }

        if (!IsPromotionEnabled())
        {
            errors.Add("Promotion to runtime is disabled. Set HELPER_ENABLE_AUTOPROMOTE_TO_RUNTIME=true for policy-based promotion.");
            return new GenerationPromotionResult(false, steps, errors, null);
        }

        if (!source.Contains($"{Path.DirectorySeparatorChar}generated_validated{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("Source path must be inside sandbox/generated_validated.");
            return new GenerationPromotionResult(false, steps, errors, null);
        }

        if (!request.TargetProjectPath.Contains($"{Path.DirectorySeparatorChar}src{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("Target path must point to a runtime project under src/**.");
            return new GenerationPromotionResult(false, steps, errors, null);
        }

        if (request.RunContractTests || request.RunUnitTests)
        {
            var testOk = await RunCommandAsync("dotnet", new[] { "test", "Helper.sln", "--no-build" }, _workspaceRoot, ct);
            steps.Add("dotnet test Helper.sln --no-build");
            if (!testOk)
            {
                errors.Add("Contract/unit tests failed.");
                return new GenerationPromotionResult(false, steps, errors, null);
            }
        }

        if (request.RunSecurityScan)
        {
            var scanOk = await RunCommandAsync(
                HostCommandResolver.GetPowerShellExecutable(),
                new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", Path.Combine(_workspaceRoot, "scripts", "secret_scan.ps1") },
                _workspaceRoot,
                ct);
            steps.Add("scripts/secret_scan.ps1");
            if (!scanOk)
            {
                errors.Add("Security scan failed.");
                return new GenerationPromotionResult(false, steps, errors, null);
            }
        }

        string? diffPath = null;
        if (request.GenerateDiff)
        {
            diffPath = await GenerateDiffAsync(source, target, ct);
            steps.Add("diff review generated");
        }

        CopyDirectory(source, target);
        steps.Add($"copied {source} -> {target}");
        _metrics.RecordPromoted();

        return new GenerationPromotionResult(true, steps, errors, diffPath);
    }

    private static bool IsPromotionEnabled()
    {
        var raw = Environment.GetEnvironmentVariable("HELPER_ENABLE_AUTOPROMOTE_TO_RUNTIME");
        return bool.TryParse(raw, out var enabled) && enabled;
    }

    private async Task<string> GenerateDiffAsync(string source, string target, CancellationToken ct)
    {
        var diffDir = Path.Combine(_workspaceRoot, "doc");
        Directory.CreateDirectory(diffDir);
        var diffPath = Path.Combine(diffDir, $"generation_promote_diff_{DateTime.UtcNow:yyyyMMdd_HHmmss}.patch");

        var args = $@"diff --no-index ""{target}"" ""{source}""";
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = args,
                WorkingDirectory = _workspaceRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        var output = await process.StandardOutput.ReadToEndAsync(ct);
        var error = await process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        var content = string.IsNullOrWhiteSpace(output) ? error : output;
        await File.WriteAllTextAsync(diffPath, content, Encoding.UTF8, ct);
        return diffPath;
    }

    private static async Task<bool> RunCommandAsync(string fileName, IReadOnlyList<string> args, string workingDirectory, CancellationToken ct)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        foreach (var arg in args)
        {
            process.StartInfo.ArgumentList.Add(arg);
        }

        process.Start();
        await process.WaitForExitAsync(ct);
        return process.ExitCode == 0;
    }

    private static void CopyDirectory(string source, string destination)
    {
        if (Directory.Exists(destination))
        {
            Directory.Delete(destination, recursive: true);
        }

        foreach (var directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, directory);
            Directory.CreateDirectory(Path.Combine(destination, relative));
        }

        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var targetPath = Path.Combine(destination, relative);
            var targetDir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrWhiteSpace(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }

            File.Copy(file, targetPath, overwrite: true);
        }
    }

    private static string ResolveWorkspaceRoot(string startDirectory)
    {
        var current = new DirectoryInfo(startDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Helper.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return startDirectory;
    }
}

