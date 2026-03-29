using System.Text.Json;

namespace Helper.Runtime.Infrastructure;

internal sealed class TemplateMetadataModel
{
    public string? Id { get; init; }
    public string? Name { get; init; }
    public string? Description { get; init; }
    public string? Language { get; init; }
    public string[]? Tags { get; init; }
    public string? Version { get; init; }
    public bool Deprecated { get; init; }
    public string[]? Capabilities { get; init; }
    public string[]? Constraints { get; init; }
    public string? ProjectType { get; init; }
    public string? Platform { get; init; }
    public string[]? SmokeScenarios { get; init; }
    public string? PromotedFromRunId { get; init; }
}

internal static class TemplateMetadataReader
{
    public static async Task<TemplateMetadataModel?> TryLoadAsync(string templateRoot, CancellationToken ct = default)
    {
        var metadataPath = Path.Combine(templateRoot, "template.json");
        if (!File.Exists(metadataPath))
        {
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(metadataPath, ct);
            return JsonSerializer.Deserialize<TemplateMetadataModel>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch
        {
            return null;
        }
    }
}

