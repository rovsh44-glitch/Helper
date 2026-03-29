using System.Text.RegularExpressions;
using Helper.Runtime.Core;
using Helper.Runtime.Infrastructure;

namespace Helper.Runtime.Generation;

internal static class CapabilityContractValidator
{
    private static readonly Regex NonAlphaNumeric = new("[^a-z0-9]+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static TemplateCertificationSmokeScenario EvaluateContract(
        string scenarioId,
        string templatePath,
        TemplateMetadataModel? metadata,
        PolyglotProjectProfile profile,
        Func<string, bool> isBuildArtifactPath)
    {
        var capabilities = metadata?.Capabilities?
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? Array.Empty<string>();
        var templateId = string.IsNullOrWhiteSpace(metadata?.Id)
            ? Path.GetFileName(templatePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            : metadata!.Id!.Trim();

        if (capabilities.Length == 0)
        {
            return new TemplateCertificationSmokeScenario(
                Id: scenarioId,
                Description: "Declared capabilities must be represented in template sources.",
                Passed: true,
                Details: "No capabilities declared in metadata.");
        }

        var sourceFiles = EnumerateSourceFiles(templatePath, profile, isBuildArtifactPath).ToArray();
        if (sourceFiles.Length == 0)
        {
            return new TemplateCertificationSmokeScenario(
                Id: scenarioId,
                Description: "Declared capabilities must be represented in template sources.",
                Passed: false,
                Details: "No source files found for capability validation.");
        }

        var combinedText = string.Join(
            "\n",
            sourceFiles.Select(path => File.ReadAllText(path)));
        var normalizedSource = Normalize(combinedText);
        var sourceLower = combinedText.ToLowerInvariant();

        var missing = new List<string>();
        foreach (var capability in capabilities)
        {
            var capabilityLower = capability.ToLowerInvariant();
            var capabilityNormalized = Normalize(capabilityLower);
            var matched = sourceLower.Contains(capabilityLower, StringComparison.Ordinal) ||
                          (!string.IsNullOrWhiteSpace(capabilityNormalized) && normalizedSource.Contains(capabilityNormalized, StringComparison.Ordinal));
            if (!matched)
            {
                missing.Add(capability);
            }
        }

        if (missing.Count == 0)
        {
            return new TemplateCertificationSmokeScenario(
                Id: scenarioId,
                Description: "Declared capabilities must be represented in template sources.",
                Passed: true,
                Details: $"Matched all {capabilities.Length} capabilities. CapabilityIds: {string.Join(", ", capabilities.Select(capability => CapabilityCatalogIds.TemplateCapability(templateId, capability)))}");
        }

        return new TemplateCertificationSmokeScenario(
            Id: scenarioId,
            Description: "Declared capabilities must be represented in template sources.",
            Passed: false,
            Details: $"Missing capabilities: {string.Join(", ", missing)}. CapabilityIds: {string.Join(", ", missing.Select(capability => CapabilityCatalogIds.TemplateCapability(templateId, capability)))}");
    }

    private static IEnumerable<string> EnumerateSourceFiles(
        string templatePath,
        PolyglotProjectProfile profile,
        Func<string, bool> isBuildArtifactPath)
    {
        foreach (var file in Directory.EnumerateFiles(templatePath, "*.*", SearchOption.AllDirectories))
        {
            if (isBuildArtifactPath(file))
            {
                continue;
            }

            var extension = Path.GetExtension(file);
            var include = profile.Kind switch
            {
                PolyglotProjectKind.Dotnet => extension.Equals(".cs", StringComparison.OrdinalIgnoreCase) ||
                                              extension.Equals(".xaml", StringComparison.OrdinalIgnoreCase),
                PolyglotProjectKind.JavaScript => extension.Equals(".js", StringComparison.OrdinalIgnoreCase) ||
                                                  extension.Equals(".cjs", StringComparison.OrdinalIgnoreCase) ||
                                                  extension.Equals(".mjs", StringComparison.OrdinalIgnoreCase) ||
                                                  extension.Equals(".json", StringComparison.OrdinalIgnoreCase),
                PolyglotProjectKind.TypeScript => extension.Equals(".ts", StringComparison.OrdinalIgnoreCase) ||
                                                  extension.Equals(".tsx", StringComparison.OrdinalIgnoreCase) ||
                                                  extension.Equals(".js", StringComparison.OrdinalIgnoreCase) ||
                                                  extension.Equals(".json", StringComparison.OrdinalIgnoreCase) ||
                                                  extension.Equals(".css", StringComparison.OrdinalIgnoreCase),
                PolyglotProjectKind.Python => extension.Equals(".py", StringComparison.OrdinalIgnoreCase) ||
                                              extension.Equals(".txt", StringComparison.OrdinalIgnoreCase),
                _ => extension.Equals(".cs", StringComparison.OrdinalIgnoreCase) ||
                     extension.Equals(".xaml", StringComparison.OrdinalIgnoreCase) ||
                     extension.Equals(".js", StringComparison.OrdinalIgnoreCase) ||
                     extension.Equals(".cjs", StringComparison.OrdinalIgnoreCase) ||
                     extension.Equals(".mjs", StringComparison.OrdinalIgnoreCase) ||
                     extension.Equals(".ts", StringComparison.OrdinalIgnoreCase) ||
                     extension.Equals(".tsx", StringComparison.OrdinalIgnoreCase) ||
                     extension.Equals(".py", StringComparison.OrdinalIgnoreCase) ||
                     extension.Equals(".json", StringComparison.OrdinalIgnoreCase)
            };

            if (include)
            {
                yield return file;
            }
        }
    }

    private static string Normalize(string value)
    {
        var lower = value.ToLowerInvariant();
        return NonAlphaNumeric.Replace(lower, string.Empty);
    }
}

