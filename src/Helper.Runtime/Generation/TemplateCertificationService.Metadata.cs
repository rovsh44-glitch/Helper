using Helper.Runtime.Infrastructure;

namespace Helper.Runtime.Generation;

public sealed partial class TemplateCertificationService
{
    private static bool ValidateMetadata(
        TemplateMetadataModel? metadata,
        string templateId,
        string version,
        List<string> errors)
    {
        if (metadata is null)
        {
            errors.Add("Metadata: template.json not found or invalid.");
            return false;
        }

        var passed = true;
        if (string.IsNullOrWhiteSpace(metadata.Id) || !string.Equals(metadata.Id.Trim(), templateId, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"Metadata: Id must equal '{templateId}'.");
            passed = false;
        }

        if (string.IsNullOrWhiteSpace(metadata.Name))
        {
            errors.Add("Metadata: Name is required.");
            passed = false;
        }

        if (string.IsNullOrWhiteSpace(metadata.Description))
        {
            errors.Add("Metadata: Description is required.");
            passed = false;
        }

        if (string.IsNullOrWhiteSpace(metadata.Language))
        {
            errors.Add("Metadata: Language is required.");
            passed = false;
        }

        var workspaceVersion = string.Equals(version, "workspace", StringComparison.OrdinalIgnoreCase);
        var versionMissing = string.IsNullOrWhiteSpace(metadata.Version);
        var versionMatches = !versionMissing &&
                             string.Equals(metadata.Version!.Trim(), version, StringComparison.OrdinalIgnoreCase);
        if (!(workspaceVersion && versionMissing) && !versionMatches)
        {
            errors.Add($"Metadata: Version must equal '{version}'.");
            passed = false;
        }

        var requireV2 = ReadFlag("HELPER_TEMPLATE_CERTIFICATION_REQUIRE_SCHEMA_V2", false);
        if (requireV2)
        {
            var hasProjectType = metadata.ProjectType is { Length: > 0 };
            var hasPlatform = metadata.Platform is { Length: > 0 };
            var hasSmoke = metadata.SmokeScenarios is { Length: > 0 };
            var hasPromotionSource = !string.IsNullOrWhiteSpace(metadata.PromotedFromRunId);
            if (!hasProjectType)
            {
                errors.Add("MetadataV2: ProjectType is required.");
                passed = false;
            }

            if (!hasPlatform)
            {
                errors.Add("MetadataV2: Platform is required.");
                passed = false;
            }

            if (!hasSmoke)
            {
                errors.Add("MetadataV2: SmokeScenarios is required.");
                passed = false;
            }

            if (!hasPromotionSource)
            {
                errors.Add("MetadataV2: PromotedFromRunId is required.");
                passed = false;
            }
        }

        return passed;
    }
}
