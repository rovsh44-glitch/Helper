using System.Text.RegularExpressions;

namespace Helper.Runtime.Core;

public static class CapabilityCatalogIds
{
    private static readonly Regex NonAlphaNumeric = new("[^a-z0-9]+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string ModelRoute(string routeKey)
        => $"model-route:{NormalizePart(routeKey)}";

    public static string TemplateCapability(string templateId, string capability)
        => $"template:{NormalizePart(templateId)}:capability:{NormalizePart(capability)}";

    public static string Tool(string toolName)
        => $"tool:{NormalizePart(toolName)}";

    public static string ExtensionCapability(string extensionId, string capability)
        => $"extension:{NormalizePart(extensionId)}:capability:{NormalizePart(capability)}";

    public static string ExtensionTool(string extensionId, string toolName)
        => $"extension:{NormalizePart(extensionId)}:tool:{NormalizePart(toolName)}";

    public static string NormalizePart(string? value)
    {
        var normalized = NonAlphaNumeric.Replace(value?.Trim().ToLowerInvariant() ?? string.Empty, "-")
            .Trim('-');
        return string.IsNullOrWhiteSpace(normalized) ? "unknown" : normalized;
    }
}

