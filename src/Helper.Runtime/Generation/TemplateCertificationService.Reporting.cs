using System.Text;
using System.Text.Json;

namespace Helper.Runtime.Generation;

public sealed partial class TemplateCertificationService
{
    private async Task WriteReportAsync(
        string reportPath,
        string templateId,
        string version,
        string templatePath,
        bool metadataPassed,
        bool compilePassed,
        bool artifactPassed,
        bool smokePassed,
        bool safetyPassed,
        bool placeholderPassed,
        bool passed,
        IReadOnlyList<string> errors,
        IReadOnlyList<TemplateCertificationSmokeScenario> smokeScenarios,
        IReadOnlyList<string> placeholderFindings,
        CancellationToken ct)
    {
        var directory = Path.GetDirectoryName(reportPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var sb = new StringBuilder();
        sb.AppendLine($"# Template Certification Report ({DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC)");
        sb.AppendLine();
        sb.AppendLine($"- TemplateId: {templateId}");
        sb.AppendLine($"- Version: {version}");
        sb.AppendLine($"- TemplatePath: {templatePath}");
        sb.AppendLine($"- MetadataSchemaPassed: {metadataPassed}");
        sb.AppendLine($"- CompileGatePassed: {compilePassed}");
        sb.AppendLine($"- ArtifactValidationPassed: {artifactPassed}");
        sb.AppendLine($"- SmokePassed: {smokePassed}");
        sb.AppendLine($"- SafetyScanPassed: {safetyPassed}");
        sb.AppendLine($"- PlaceholderScanPassed: {placeholderPassed}");
        sb.AppendLine($"- Passed: {passed}");
        sb.AppendLine();
        sb.AppendLine("## Smoke Scenarios");
        foreach (var scenario in smokeScenarios)
        {
            sb.AppendLine($"- {scenario.Id}: {(scenario.Passed ? "pass" : "fail")} ({scenario.Details})");
        }

        sb.AppendLine();
        sb.AppendLine("## Placeholder Findings");
        if (placeholderFindings.Count == 0)
        {
            sb.AppendLine("- none");
        }
        else
        {
            foreach (var finding in placeholderFindings)
            {
                sb.AppendLine($"- {finding}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("## Errors");
        if (errors.Count == 0)
        {
            sb.AppendLine("- none");
        }
        else
        {
            foreach (var error in errors)
            {
                sb.AppendLine($"- {error}");
            }
        }

        await File.WriteAllTextAsync(reportPath, sb.ToString(), Encoding.UTF8, ct);
        var sidecarPath = Path.ChangeExtension(reportPath, ".json");
        var payload = new
        {
            generatedAtUtc = DateTimeOffset.UtcNow,
            templateId,
            version,
            templatePath,
            metadataPassed,
            compilePassed,
            artifactPassed,
            smokePassed,
            safetyPassed,
            placeholderPassed,
            passed,
            errors,
            smokeScenarios,
            placeholderFindings
        };
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(sidecarPath, json, Encoding.UTF8, ct);
    }

    private async Task WriteGateReportAsync(
        string reportPath,
        bool passed,
        IReadOnlyList<TemplateCertificationReport> templateReports,
        IReadOnlyList<string> violations,
        CancellationToken ct)
    {
        var directory = Path.GetDirectoryName(reportPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var sb = new StringBuilder();
        sb.AppendLine($"# Template Certification Gate ({DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC)");
        sb.AppendLine();
        sb.AppendLine($"- Passed: {passed}");
        sb.AppendLine($"- CertifiedCount: {templateReports.Count(x => x.Passed)}");
        sb.AppendLine($"- FailedCount: {templateReports.Count(x => !x.Passed)}");
        sb.AppendLine();
        sb.AppendLine("## Template Results");
        foreach (var report in templateReports)
        {
            sb.AppendLine($"- {report.TemplateId}:{report.Version} => {(report.Passed ? "pass" : "fail")} ({report.ReportPath})");
        }

        sb.AppendLine();
        sb.AppendLine("## Violations");
        if (violations.Count == 0)
        {
            sb.AppendLine("- none");
        }
        else
        {
            foreach (var violation in violations)
            {
                sb.AppendLine($"- {violation}");
            }
        }

        await File.WriteAllTextAsync(reportPath, sb.ToString(), Encoding.UTF8, ct);
        var jsonPath = Path.ChangeExtension(reportPath, ".json");
        var payload = new
        {
            generatedAtUtc = DateTimeOffset.UtcNow,
            passed,
            certifiedCount = templateReports.Count(x => x.Passed),
            failedCount = templateReports.Count(x => !x.Passed),
            violations,
            templates = templateReports.Select(x => new
            {
                x.TemplateId,
                x.Version,
                x.Passed,
                x.ReportPath
            }).ToArray()
        };
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(jsonPath, json, Encoding.UTF8, ct);
    }

    private string ResolveReportPath(string? reportPath, string templateId, string version)
    {
        if (!string.IsNullOrWhiteSpace(reportPath))
        {
            return Path.GetFullPath(reportPath);
        }

        var docRoot = Path.Combine(_workspaceRoot, "doc");
        Directory.CreateDirectory(docRoot);
        return Path.Combine(docRoot, $"HELPER_TEMPLATE_CERTIFICATION_{templateId}_{version}_{DateTime.UtcNow:yyyy-MM-dd_HH-mm-ss}.md");
    }

    private string ResolveGateReportPath(string? reportPath)
    {
        if (!string.IsNullOrWhiteSpace(reportPath))
        {
            return Path.GetFullPath(reportPath);
        }

        var docRoot = Path.Combine(_workspaceRoot, "doc");
        Directory.CreateDirectory(docRoot);
        return Path.Combine(docRoot, $"HELPER_TEMPLATE_CERTIFICATION_GATE_{DateTime.UtcNow:yyyy-MM-dd_HH-mm-ss}.md");
    }
}
