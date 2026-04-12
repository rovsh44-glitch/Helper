using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Helper.Runtime.Core;

internal static partial class HelperCliCommandDispatcher
{
    private static async Task<bool> HandleTemplateCommandAsync(string command, string[] commandArgs, string query, HelperCliRuntime runtime, CancellationToken ct)
    {
        switch (command)
        {
            case "template-versions":
                await HandleTemplateVersionsAsync(query, runtime, ct);
                return true;
            case "template-activate":
                await HandleTemplateActivateAsync(query, runtime, ct);
                return true;
            case "template-rollback":
                await HandleTemplateRollbackAsync(query, runtime, ct);
                return true;
            case "template-promotion-profile":
                HandleTemplatePromotionProfile(runtime);
                return true;
            case "template-promote-existing":
                await HandleTemplatePromoteExistingAsync(commandArgs, runtime, ct);
                return true;
            case "template-certify":
                await HandleTemplateCertifyAsync(commandArgs, runtime, ct);
                return true;
            case "template-certification-gate":
                await HandleTemplateCertificationGateAsync(query, runtime, ct);
                return true;
            case "template-availability":
                await HandleTemplateAvailabilityAsync(commandArgs, runtime, ct);
                return true;
            default:
                return false;
        }
    }

    private static async Task HandleTemplateVersionsAsync(string query, HelperCliRuntime runtime, CancellationToken ct)
    {
        var templateId = query.Trim();
        if (string.IsNullOrWhiteSpace(templateId))
        {
            Console.WriteLine("Usage: helper template-versions <templateId>");
            return;
        }

        var versions = await runtime.TemplateLifecycle.GetVersionsAsync(templateId, ct);
        if (versions.Count == 0)
        {
            Console.WriteLine($"No versions found for template '{templateId}'.");
            return;
        }

        Console.WriteLine($"Template '{templateId}' versions:");
        foreach (var version in versions)
        {
            Console.WriteLine($"- {version.Version} | active={version.IsActive} | deprecated={version.Deprecated} | path={version.Path}");
        }
    }

    private static async Task HandleTemplateActivateAsync(string query, HelperCliRuntime runtime, CancellationToken ct)
    {
        var parts = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
        {
            Console.WriteLine("Usage: helper template-activate <templateId> <version>");
            return;
        }

        var result = await runtime.TemplateLifecycle.ActivateVersionAsync(parts[0], parts[1], ct);
        Console.WriteLine(result.Success
            ? $"✅ {result.Message} Active version: {result.ActiveVersion}"
            : $"❌ {result.Message}");
    }

    private static async Task HandleTemplateRollbackAsync(string query, HelperCliRuntime runtime, CancellationToken ct)
    {
        var templateId = query.Trim();
        if (string.IsNullOrWhiteSpace(templateId))
        {
            Console.WriteLine("Usage: helper template-rollback <templateId>");
            return;
        }

        var result = await runtime.TemplateLifecycle.RollbackAsync(templateId, ct);
        Console.WriteLine(result.Success
            ? $"✅ Rollback complete. Active version: {result.ActiveVersion}"
            : $"❌ {result.Message}");
    }

    private static void HandleTemplatePromotionProfile(HelperCliRuntime runtime)
    {
        var profile = runtime.PromotionProfile.GetCurrent();
        Console.WriteLine("Template promotion feature profile:");
        Console.WriteLine($"- RuntimePromotionEnabled: {profile.RuntimePromotionEnabled}");
        Console.WriteLine($"- AutoActivateEnabled: {profile.AutoActivateEnabled}");
        Console.WriteLine($"- PostActivationFullRecertifyEnabled: {profile.PostActivationFullRecertifyEnabled}");
        Console.WriteLine($"- FormatMode: {profile.FormatMode}");
        Console.WriteLine($"- RouterV2Enabled: {profile.RouterV2Enabled}");
        Console.WriteLine($"- RouterMinConfidence: {profile.RouterMinConfidence:0.00}");
    }

    private static async Task HandleTemplatePromoteExistingAsync(string[] commandArgs, HelperCliRuntime runtime, CancellationToken ct)
    {
        if (commandArgs.Length < 2)
        {
            Console.WriteLine("Usage: helper template-promote-existing <projectPath> <prompt...>");
            return;
        }

        var projectPath = Path.GetFullPath(commandArgs[0]);
        if (!Directory.Exists(projectPath))
        {
            Console.WriteLine($"❌ Project path not found: {projectPath}");
            Environment.ExitCode = 1;
            return;
        }

        var prompt = string.Join(" ", commandArgs.Skip(1));
        if (string.IsNullOrWhiteSpace(prompt))
        {
            Console.WriteLine("❌ Prompt is required.");
            Environment.ExitCode = 1;
            return;
        }

        var request = new GenerationRequest(prompt, projectPath, new(), $"manual_promotion_{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss}");
        var seedResult = new GenerationResult(true, new List<GeneratedFile>(), projectPath, new List<BuildError>(), TimeSpan.Zero);
        var outcome = await runtime.TemplatePromotionService.TryPromoteAsync(
            request,
            seedResult,
            msg => Console.WriteLine($"[TemplatePromotion] {msg}"),
            ct);

        Console.WriteLine($"- Attempted: {outcome.Attempted}");
        Console.WriteLine($"- Success: {outcome.Success}");
        Console.WriteLine($"- TemplateId: {outcome.TemplateId}");
        Console.WriteLine($"- Version: {outcome.Version}");
        Console.WriteLine($"- Message: {outcome.Message}");
        PrintBlock("Errors", outcome.Errors);

        if (!outcome.Success)
        {
            Environment.ExitCode = 1;
        }
    }

    private static async Task HandleTemplateCertifyAsync(string[] commandArgs, HelperCliRuntime runtime, CancellationToken ct)
    {
        if (commandArgs.Length < 2)
        {
            Console.WriteLine("Usage: helper template-certify <templateId> <version> [reportPath] [--template-path <path>]");
            return;
        }

        string? reportPath = null;
        string? templatePath = null;
        for (var index = 2; index < commandArgs.Length; index++)
        {
            if (string.Equals(commandArgs[index], "--template-path", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= commandArgs.Length)
                {
                    Console.WriteLine("Usage: helper template-certify <templateId> <version> [reportPath] [--template-path <path>]");
                    Environment.ExitCode = 1;
                    return;
                }

                templatePath = commandArgs[++index];
                continue;
            }

            if (reportPath is null)
            {
                reportPath = commandArgs[index];
                continue;
            }

            Console.WriteLine($"Unknown template-certify argument: {commandArgs[index]}");
            Environment.ExitCode = 1;
            return;
        }

        var report = await runtime.TemplateCertification.CertifyAsync(commandArgs[0], commandArgs[1], reportPath, templatePath, ct);
        Console.WriteLine("Template certification:");
        Console.WriteLine($"- TemplateId: {report.TemplateId}");
        Console.WriteLine($"- Version: {report.Version}");
        Console.WriteLine($"- Passed: {report.Passed}");
        Console.WriteLine($"- ReportPath: {report.ReportPath}");
        PrintBlock("Errors", report.Errors);

        if (!report.Passed)
        {
            Environment.ExitCode = 1;
        }
    }

    private static async Task HandleTemplateCertificationGateAsync(string query, HelperCliRuntime runtime, CancellationToken ct)
    {
        var outputPath = string.IsNullOrWhiteSpace(query) ? null : query.Trim();
        var gate = await runtime.TemplateCertification.EvaluateGateAsync(outputPath, ct);
        Console.WriteLine("Template certification gate:");
        Console.WriteLine($"- Passed: {gate.Passed}");
        Console.WriteLine($"- ReportPath: {gate.ReportPath}");
        Console.WriteLine($"- CertifiedCount: {gate.CertifiedCount}");
        Console.WriteLine($"- FailedCount: {gate.FailedCount}");
        PrintBlock("Violations", gate.Violations);

        if (!gate.Passed)
        {
            Environment.ExitCode = 1;
        }
    }

    private static async Task HandleTemplateAvailabilityAsync(string[] commandArgs, HelperCliRuntime runtime, CancellationToken ct)
    {
        if (commandArgs.Length < 1 || string.IsNullOrWhiteSpace(commandArgs[0]))
        {
            Console.WriteLine("Usage: helper template-availability <templateId>");
            return;
        }

        var templateId = commandArgs[0].Trim();
        var resolution = await runtime.TemplateManager.ResolveTemplateAvailabilityAsync(templateId, ct);
        Console.WriteLine("Template availability:");
        Console.WriteLine($"- TemplateId: {resolution.TemplateId}");
        Console.WriteLine($"- State: {resolution.State}");
        Console.WriteLine($"- ExistsOnDisk: {resolution.ExistsOnDisk}");
        Console.WriteLine($"- TemplateRootPath: {resolution.TemplateRootPath ?? "n/a"}");
        Console.WriteLine($"- Reason: {resolution.Reason}");
        if (!string.IsNullOrWhiteSpace(resolution.CertificationReportPath))
        {
            Console.WriteLine($"- CertificationReportPath: {resolution.CertificationReportPath}");
        }

        if (resolution.Template is not null)
        {
            Console.WriteLine($"- Certified: {resolution.Template.Certified}");
            Console.WriteLine($"- HasCriticalAlerts: {resolution.Template.HasCriticalAlerts}");
            Console.WriteLine($"- TemplateVersion: {resolution.Template.Version ?? "workspace"}");
            Console.WriteLine($"- TemplatePath: {resolution.Template.RootPath}");
        }

        if (resolution.CriticalAlerts is { Count: > 0 })
        {
            Console.WriteLine("- CriticalAlerts:");
            foreach (var alert in resolution.CriticalAlerts)
            {
                Console.WriteLine($"  * {alert}");
            }
        }

        if (resolution.State != TemplateAvailabilityState.Available)
        {
            Environment.ExitCode = 1;
        }
    }
}

