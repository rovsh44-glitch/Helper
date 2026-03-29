using System.Text.Json;
using Helper.Runtime.Core;
using Helper.Runtime.Infrastructure;

namespace Helper.Runtime.Generation;

internal sealed class TemplatePromotionMetadataNormalizer
{
    public async Task NormalizeAsync(
        string versionRoot,
        string templateId,
        string version,
        GenerationRequest request,
        CancellationToken ct)
    {
        var metadataPath = Path.Combine(versionRoot, "template.json");
        var metadata = await TemplateMetadataReader.TryLoadAsync(versionRoot, ct)
            ?? new TemplateMetadataModel();

        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (metadata.Tags is not null)
        {
            foreach (var tag in metadata.Tags.Where(x => !string.IsNullOrWhiteSpace(x)))
            {
                tags.Add(tag.Trim());
            }
        }

        tags.Add("autopromoted");
        tags.Add("runtime-candidate");

        var projectType = InferProjectType(versionRoot, metadata.ProjectType);
        var platform = InferPlatform(versionRoot, metadata.Platform);
        var smokeScenarios = metadata.SmokeScenarios is { Length: > 0 }
            ? metadata.SmokeScenarios
            : BuildDefaultSmokeScenarios(projectType);
        var promotedFromRunId = string.IsNullOrWhiteSpace(request.SessionId)
            ? $"promotion_{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}"
            : request.SessionId!;

        var normalized = new
        {
            Id = templateId,
            Name = string.IsNullOrWhiteSpace(metadata.Name) ? $"{templateId}/{version} (Auto-Generated)" : metadata.Name.Trim(),
            Description = string.IsNullOrWhiteSpace(metadata.Description)
                ? "Automatically generalized template from successful project."
                : metadata.Description.Trim(),
            Language = string.IsNullOrWhiteSpace(metadata.Language) ? "csharp" : metadata.Language.Trim(),
            Tags = tags.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray(),
            Version = version,
            Deprecated = false,
            Capabilities = metadata.Capabilities ?? Array.Empty<string>(),
            Constraints = metadata.Constraints ?? Array.Empty<string>(),
            ProjectType = projectType,
            Platform = platform,
            SmokeScenarios = smokeScenarios,
            PromotedFromRunId = promotedFromRunId
        };

        var json = JsonSerializer.Serialize(normalized, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(metadataPath, json, ct);
    }

    private static string InferProjectType(string rootPath, string? metadataProjectType)
    {
        if (!string.IsNullOrWhiteSpace(metadataProjectType))
        {
            return metadataProjectType.Trim();
        }

        var hasXaml = Directory.EnumerateFiles(rootPath, "*.xaml", SearchOption.AllDirectories).Any();
        var hasAppXaml = Directory.EnumerateFiles(rootPath, "App.xaml", SearchOption.AllDirectories).Any();
        if (hasXaml)
        {
            return hasAppXaml ? "wpf-app" : "wpf-library";
        }

        var hasProgram = Directory.EnumerateFiles(rootPath, "Program.cs", SearchOption.AllDirectories).Any();
        return hasProgram ? "console" : "library";
    }

    private static string InferPlatform(string rootPath, string? metadataPlatform)
    {
        if (!string.IsNullOrWhiteSpace(metadataPlatform))
        {
            return metadataPlatform.Trim();
        }

        var hasXaml = Directory.EnumerateFiles(rootPath, "*.xaml", SearchOption.AllDirectories).Any();
        return hasXaml ? "windows" : "cross-platform";
    }

    private static string[] BuildDefaultSmokeScenarios(string projectType)
    {
        if (projectType.Contains("wpf", StringComparison.OrdinalIgnoreCase))
        {
            return new[]
            {
                "compile",
                "artifact-validation",
                "wpf-mainwindow-present"
            };
        }

        return new[]
        {
            "compile",
            "artifact-validation"
        };
    }
}

