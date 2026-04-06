using Helper.Api.Backend.ModelGateway;

namespace Helper.Api.Backend.Providers;

public sealed record ProviderModelClassBindingDto(
    string ModelClass,
    string ModelName);

public sealed record ProviderCredentialReferenceDto(
    string? ApiKeyEnvVar,
    bool Required,
    bool Configured);

public sealed record ProviderProfileDto(
    string Id,
    string DisplayName,
    string Kind,
    string TransportKind,
    string BaseUrl,
    bool Enabled,
    bool IsBuiltIn,
    bool IsLocal,
    string TrustMode,
    IReadOnlyList<string> SupportedGoals,
    IReadOnlyList<ProviderModelClassBindingDto> ModelBindings,
    ProviderCredentialReferenceDto? Credential,
    string? EmbeddingModel,
    string? PreferredReasoningEffort,
    string? Notes);

public sealed record ProviderProfileValidationDto(
    bool IsValid,
    IReadOnlyList<string> Alerts,
    IReadOnlyList<string> Warnings);

public sealed record ProviderCapabilitySummaryDto(
    bool SupportsFast,
    bool SupportsReasoning,
    bool SupportsCoder,
    bool SupportsVision,
    bool SupportsBackground,
    bool SupportsResearchVerified,
    bool SupportsPrivacyFirst,
    bool RequiresLocalRuntime);

public sealed record ProviderProfileSummaryDto(
    ProviderProfileDto Profile,
    ProviderProfileValidationDto Validation,
    ProviderCapabilitySummaryDto Capabilities,
    bool IsActive);

public sealed record ProviderProfilesSnapshotDto(
    DateTimeOffset GeneratedAtUtc,
    string? ActiveProfileId,
    IReadOnlyList<ProviderProfileSummaryDto> Profiles,
    IReadOnlyList<string> Alerts);

public sealed record ProviderActivationRequestDto(string ProfileId);

public sealed record ProviderActivationResultDto(
    bool Success,
    string? ActiveProfileId,
    IReadOnlyList<string> ReasonCodes,
    IReadOnlyList<string> Warnings);

public sealed record ProviderRecommendationRequestDto(
    string Goal,
    bool PreferLocal = false,
    bool NeedVision = false,
    string LatencyPreference = "balanced",
    string CodingIntensity = "medium");

public sealed record ProviderRecommendationResultDto(
    string? RecommendedProfileId,
    IReadOnlyList<string> AlternativeProfileIds,
    IReadOnlyList<string> ReasonCodes,
    IReadOnlyList<string> Warnings);

public static class ProviderProfileDtoMapper
{
    public static ProviderProfilesSnapshotDto ToDto(ProviderProfilesSnapshot snapshot)
    {
        return new ProviderProfilesSnapshotDto(
            snapshot.GeneratedAtUtc,
            snapshot.ActiveProfileId,
            snapshot.Profiles.Select(ToDto).ToArray(),
            snapshot.Alerts.ToArray());
    }

    public static ProviderProfileSummaryDto ToDto(ProviderProfileSummary summary)
    {
        return new ProviderProfileSummaryDto(
            ToDto(summary.Profile),
            new ProviderProfileValidationDto(
                summary.Validation.IsValid,
                summary.Validation.Alerts.ToArray(),
                summary.Validation.Warnings.ToArray()),
            new ProviderCapabilitySummaryDto(
                summary.Capabilities.SupportsFast,
                summary.Capabilities.SupportsReasoning,
                summary.Capabilities.SupportsCoder,
                summary.Capabilities.SupportsVision,
                summary.Capabilities.SupportsBackground,
                summary.Capabilities.SupportsResearchVerified,
                summary.Capabilities.SupportsPrivacyFirst,
                summary.Capabilities.RequiresLocalRuntime),
            summary.IsActive);
    }

    public static ProviderActivationResultDto ToDto(ProviderActivationResult result)
    {
        return new ProviderActivationResultDto(
            result.Success,
            result.ActiveProfileId,
            result.ReasonCodes.ToArray(),
            result.Warnings.ToArray());
    }

    public static ProviderRecommendationRequest ToDomain(ProviderRecommendationRequestDto dto)
    {
        return new ProviderRecommendationRequest(
            dto.Goal,
            dto.PreferLocal,
            dto.NeedVision,
            dto.LatencyPreference,
            dto.CodingIntensity);
    }

    public static ProviderRecommendationResultDto ToDto(ProviderRecommendationResult result)
    {
        return new ProviderRecommendationResultDto(
            result.RecommendedProfileId,
            result.AlternativeProfileIds.ToArray(),
            result.ReasonCodes.ToArray(),
            result.Warnings.ToArray());
    }

    private static ProviderProfileDto ToDto(ProviderProfile profile)
    {
        var credential = profile.Credential is null
            ? null
            : new ProviderCredentialReferenceDto(
                profile.Credential.ApiKeyEnvVar,
                profile.Credential.Required,
                !string.IsNullOrWhiteSpace(profile.Credential.ApiKeyEnvVar) &&
                !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(profile.Credential.ApiKeyEnvVar)));

        return new ProviderProfileDto(
            profile.Id,
            profile.DisplayName,
            ToSnakeCase(profile.Kind),
            ToSnakeCase(profile.TransportKind),
            profile.BaseUrl,
            profile.Enabled,
            profile.IsBuiltIn,
            profile.IsLocal,
            ToSnakeCase(profile.TrustMode),
            profile.SupportedGoals.Select(ToSnakeCase).ToArray(),
            profile.ModelBindings.Select(binding => new ProviderModelClassBindingDto(
                ToSnakeCase(binding.ModelClass),
                binding.ModelName)).ToArray(),
            credential,
            profile.EmbeddingModel,
            profile.PreferredReasoningEffort,
            profile.Notes);
    }

    private static string ToSnakeCase<TEnum>(TEnum value)
        where TEnum : struct, Enum
    {
        return value switch
        {
            HelperModelClass modelClass => modelClass switch
            {
                HelperModelClass.Fast => "fast",
                HelperModelClass.Reasoning => "reasoning",
                HelperModelClass.Coder => "coder",
                HelperModelClass.Vision => "vision",
                HelperModelClass.Critic => "critic",
                HelperModelClass.Background => "background",
                _ => value.ToString().ToLowerInvariant()
            },
            _ => string.Concat(value.ToString().SelectMany((character, index) =>
                index > 0 && char.IsUpper(character)
                    ? new[] { '_', char.ToLowerInvariant(character) }
                    : new[] { char.ToLowerInvariant(character) }))
        };
    }
}
