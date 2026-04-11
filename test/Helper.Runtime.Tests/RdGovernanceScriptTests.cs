using System.Diagnostics;

namespace Helper.Runtime.Tests;

[Trait("Lane", "Fast")]
public sealed class RdGovernanceScriptTests
{
    [Fact]
    public void RelativeResearchReadmeLink_InDocIndex_IsAccepted()
    {
        using var temp = new TempDirectoryScope("helper_rd_governance_");
        CreateMinimalGovernanceWorkspace(temp.Path);

        var scriptPath = TestWorkspaceRoot.ResolveFile("scripts", "check_rd_governance.ps1");
        var arguments =
            $"-ExecutionPolicy Bypass -File \"{scriptPath}\" -RepoRoot \"{temp.Path}\"";

        var result = RunProcess(ResolvePowerShellExecutable(), arguments, temp.Path);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("governed and separated", result.StdOut, StringComparison.OrdinalIgnoreCase);
    }

    private static void CreateMinimalGovernanceWorkspace(string root)
    {
        Directory.CreateDirectory(Path.Combine(root, "doc", "research", "active"));
        Directory.CreateDirectory(Path.Combine(root, "doc", "research", "notes"));
        Directory.CreateDirectory(Path.Combine(root, "doc", "archive", "comparative"));
        Directory.CreateDirectory(Path.Combine(root, "scripts"));

        File.WriteAllText(
            Path.Combine(root, "README.md"),
            """
# Root

- `doc/research/README.md`
""");
        File.WriteAllText(
            Path.Combine(root, "doc", "README.md"),
            """
# Docs

- [Research](research/README.md)
""");
        File.WriteAllText(Path.Combine(root, "doc", "research", "README.md"), "# Research");
        File.WriteAllText(Path.Combine(root, "doc", "research", "MODEL_EXPERIMENT_TRACK_POLICY.md"), "# Policy");
        File.WriteAllText(Path.Combine(root, "doc", "research", "active", "CURRENT_MODEL_EXPERIMENT_TRACK.md"), "# Active");
        File.WriteAllText(Path.Combine(root, "doc", "research", "notes", "README.md"), "# Notes");
        File.WriteAllText(
            Path.Combine(root, "doc", "archive", "comparative", "HELPER_EXECUTION_ORDER_TABLE_LFL300_2026-03-22.md"),
            "STEP-001..STEP-016 implemented.");
        File.WriteAllText(
            Path.Combine(root, "doc", "archive", "comparative", "HELPER_EXECUTION_DASHBOARD_LFL300_2026-03-22.md"),
            "| `Wave 5` | `STEP-016` R&D Governance | `completed` |");
        File.WriteAllText(
            Path.Combine(root, "scripts", "run_eval_runner_v2.ps1"),
            "# Scope=product_quality_closure_only");
        File.WriteAllText(
            Path.Combine(root, "doc", "research", "MODEL_EXPERIMENT_TRACK_REGISTRY.json"),
            """
{
  "trackId": "model_experiment_track",
  "status": "TRACK_DEFINED_RESEARCH_ONLY",
  "productExecutionTrack": {
    "scope": "product_quality_closure_only"
  },
  "modelExperimentTrack": {
    "allowedThemes": [
      { "id": "selective_residual_memory" },
      { "id": "evidence_aware_decoding" },
      { "id": "retrieval_conditioned_latent_routing" }
    ],
    "nonAuthoritativeByDefault": true,
    "promotionRule": {
      "requiresSeparateRfc": true,
      "requiresReproductionOnProductTrack": true
    }
  }
}
""");
        File.WriteAllText(
            Path.Combine(root, "doc", "research", "active", "CURRENT_MODEL_EXPERIMENT_TRACK.json"),
            """
{
  "status": "TRACK_DEFINED_NO_ACTIVE_EXPERIMENTS",
  "productionRoadmapSeparated": true,
  "productRunnerScope": "product_quality_closure_only"
}
""");
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
