using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Helper.Runtime.Core;
using Helper.Runtime.Infrastructure;

internal static partial class HelperCliCommandDispatcher
{
    public static async Task<bool> TryHandleAsync(string command, string[] commandArgs, string query, HelperCliRuntime runtime, CancellationToken ct)
    {
        switch (command)
        {
            case "scan":
                await HandleScanAsync(runtime, ct);
                return true;
            case "extension-registry":
                HandleExtensionRegistry(runtime);
                return true;
            case "test-security":
                await HandleTestSecurityAsync(runtime, ct);
                return true;
            case "create":
                await HandleCreateAsync(query, runtime, ct);
                return true;
            case "challenge":
                await HandleChallengeAsync(query, runtime, ct);
                return true;
            default:
                return await HandleTemplateCommandAsync(command, commandArgs, query, runtime, ct)
                    || await HandleCertificationCommandAsync(command, commandArgs, query, runtime, ct);
        }
    }

    private static async Task HandleScanAsync(HelperCliRuntime runtime, CancellationToken ct)
    {
        Console.WriteLine("🖥️ Scanning System Hardware...");
        var caps = await runtime.Scanner.ScanAsync(ct);
        Console.WriteLine($"Tier: {caps.Tier}");
        Console.WriteLine($"GPU: {caps.GpuModel} ({caps.VramGb:F1} GB VRAM)");
        Console.WriteLine($"RAM: {caps.RamGb:F1} GB");
        Console.WriteLine($"CPU: {caps.CpuCores} cores");
        Console.WriteLine($"\nRecommendation:\n{runtime.Scanner.GenerateRecommendation(caps)}");
    }

    private static void HandleExtensionRegistry(HelperCliRuntime runtime)
    {
        var snapshot = runtime.ExtensionRegistry.GetSnapshot();
        Console.WriteLine("Extension registry:");
        Console.WriteLine($"- Manifests: {snapshot.Manifests.Count}");
        Console.WriteLine($"- Failures: {snapshot.Failures.Count}");
        Console.WriteLine($"- Warnings: {snapshot.Warnings.Count}");

        foreach (var manifest in snapshot.Manifests)
        {
            Console.WriteLine($"- {manifest.Id} | category={manifest.Category} | providerType={manifest.ProviderType} | trust={manifest.TrustLevel} | enabledByDefault={manifest.DefaultEnabled}");
        }

        PrintBlock("Failures", snapshot.Failures);
        PrintBlock("Warnings", snapshot.Warnings);
    }

    private static async Task HandleTestSecurityAsync(HelperCliRuntime runtime, CancellationToken ct)
    {
        Console.WriteLine("🛡️ Testing Security Protocols...");
        var result = await runtime.Tools.ExecuteToolAsync(
            "shell_execute",
            new Dictionary<string, object> { ["command"] = "rm -rf src/" },
            ct);

        if (!result.Success && result.Error is not null && result.Error.Contains("ProcessGuard", StringComparison.Ordinal))
        {
            Console.WriteLine("✅ SUCCESS: Dangerous command was BLOCKED by ProcessGuard.");
            Console.WriteLine($"Message: {result.Error}");
            return;
        }

        Console.WriteLine("❌ FAILURE: Security breach! Command was not blocked correctly.");
    }

    private static async Task HandleCreateAsync(string query, HelperCliRuntime runtime, CancellationToken ct)
    {
        var outputPath = HelperWorkspacePathResolver.ResolveProjectsPath(Guid.NewGuid().ToString("N")[..8], helperRoot: runtime.HelperRoot);
        var request = new GenerationRequest(query, outputPath, new());
        var result = await runtime.Orchestrator.GenerateProjectAsync(request, true, msg => Console.WriteLine($"[Helper] {msg}"), ct);

        if (!result.Success)
        {
            Console.WriteLine("❌ Generation failed.");
            Environment.ExitCode = 1;
            return;
        }

        var finalPath = result.ProjectPath == "MEM_EXPERT" ? outputPath : result.ProjectPath;
        Directory.CreateDirectory(finalPath);
        foreach (var file in result.Files)
        {
            var filePath = Path.Combine(finalPath, file.RelativePath);
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(filePath, file.Content);
        }

        Console.WriteLine($"✅ Project generated successfully at: {finalPath}");
    }

    private static async Task HandleChallengeAsync(string query, HelperCliRuntime runtime, CancellationToken ct)
    {
        Console.WriteLine($"🎭 Initiating Shadow Roundtable for: {query}");
        var report = await runtime.Critic.ChallengeAsync(query, ct);

        Console.WriteLine("\n--- OPINIONS ---");
        foreach (var opinion in report.Opinions)
        {
            Console.WriteLine($"\n[{opinion.Persona}] Score: {opinion.CriticalScore}");
            Console.WriteLine($"Opinion: {opinion.Opinion}");
            Console.WriteLine($"Alternative: {opinion.AlternativeProposal}");
        }

        Console.WriteLine("\n--- FINAL SYNTHESIS ---");
        Console.WriteLine(report.SynthesizedAdvice);
        Console.WriteLine($"\nConflict Level: {report.ConflictLevel:P}");
    }

    private static void PrintBlock(string title, IReadOnlyCollection<string> lines)
    {
        if (lines.Count == 0)
        {
            return;
        }

        Console.WriteLine($"- {title}:");
        foreach (var line in lines)
        {
            Console.WriteLine($"  * {line}");
        }
    }
}

