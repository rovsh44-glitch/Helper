using Helper.Api.Backend.ModelGateway;

namespace Helper.Api.Backend.Providers;

public enum ProviderKind
{
    Ollama,
    OpenAiCompatible
}

public enum ProviderTransportKind
{
    Ollama,
    OpenAiCompatible
}

public enum ProviderTrustMode
{
    Local,
    RemoteTrusted,
    RemoteUntrusted
}

public enum ProviderWorkloadGoal
{
    LocalFast,
    LocalCoder,
    HostedReasoning,
    ResearchVerified,
    PrivacyFirst
}

public sealed record ProviderModelClassBinding(
    HelperModelClass ModelClass,
    string ModelName);

public sealed record ProviderCredentialReference(
    string? ApiKeyEnvVar,
    bool Required);

public sealed record ProviderProfile(
    string Id,
    string DisplayName,
    ProviderKind Kind,
    ProviderTransportKind TransportKind,
    string BaseUrl,
    bool Enabled,
    bool IsBuiltIn,
    bool IsLocal,
    ProviderTrustMode TrustMode,
    IReadOnlyList<ProviderWorkloadGoal> SupportedGoals,
    IReadOnlyList<ProviderModelClassBinding> ModelBindings,
    ProviderCredentialReference? Credential = null,
    string? EmbeddingModel = null,
    string? PreferredReasoningEffort = null,
    string? Notes = null);

public sealed record ProviderProfileValidationResult(
    bool IsValid,
    IReadOnlyList<string> Alerts,
    IReadOnlyList<string> Warnings);

public sealed record ProviderCapabilitySummary(
    bool SupportsFast,
    bool SupportsReasoning,
    bool SupportsCoder,
    bool SupportsVision,
    bool SupportsBackground,
    bool SupportsResearchVerified,
    bool SupportsPrivacyFirst,
    bool RequiresLocalRuntime);

public sealed record ProviderProfileSummary(
    ProviderProfile Profile,
    ProviderProfileValidationResult Validation,
    ProviderCapabilitySummary Capabilities,
    bool IsActive);

public sealed record ProviderProfilesSnapshot(
    DateTimeOffset GeneratedAtUtc,
    string? ActiveProfileId,
    IReadOnlyList<ProviderProfileSummary> Profiles,
    IReadOnlyList<string> Alerts);

public sealed record ProviderRecommendationRequest(
    string Goal,
    bool PreferLocal = false,
    bool NeedVision = false,
    string LatencyPreference = "balanced",
    string CodingIntensity = "medium");

public sealed record ProviderRecommendationResult(
    string? RecommendedProfileId,
    IReadOnlyList<string> AlternativeProfileIds,
    IReadOnlyList<string> ReasonCodes,
    IReadOnlyList<string> Warnings);

public sealed record ProviderRuntimeConfiguration(
    string ProfileId,
    ProviderTransportKind TransportKind,
    string BaseUrl,
    string? ApiKey,
    string? DefaultModel,
    string? EmbeddingModel);

public sealed record ProviderActivationResult(
    bool Success,
    string? ActiveProfileId,
    IReadOnlyList<string> ReasonCodes,
    IReadOnlyList<string> Warnings);

public interface IProviderProfileStore
{
    IReadOnlyList<ProviderProfile> LoadProfiles();
    void SaveProfiles(IReadOnlyList<ProviderProfile> profiles);
    string? LoadActiveProfileId();
    void SaveActiveProfileId(string? profileId);
}

public interface IProviderProfileValidator
{
    ProviderProfileValidationResult Validate(ProviderProfile profile, IReadOnlyCollection<string>? knownIds = null);
}

public interface IProviderCapabilityMatrix
{
    ProviderCapabilitySummary Summarize(ProviderProfile profile);
}

public interface IProviderProfileCatalog
{
    ProviderProfilesSnapshot GetSnapshot();
    ProviderProfileSummary? GetActiveProfile();
    ProviderProfileSummary? GetById(string profileId);
}

public interface IProviderProfileActivationService
{
    Task<ProviderActivationResult> ActivateAsync(string profileId, CancellationToken ct);
    Task<ProviderActivationResult> EnsureRuntimeSynchronizedAsync(CancellationToken ct);
}

public interface IProviderRecommendationPolicy
{
    ProviderRecommendationResult Recommend(ProviderRecommendationRequest request, IReadOnlyList<ProviderProfileSummary> candidates);
}

public interface IProviderProfileResolver
{
    ProviderProfileSummary? GetActiveProfile();
    ProviderRuntimeConfiguration? GetRuntimeConfiguration();
    string? ResolveModelBinding(HelperModelClass modelClass);
    string? ResolvePreferredReasoningEffort();
    bool SupportsVision();
    bool PrefersResearchVerification();
    bool IsLocalOnly();
    string? ApplyToRuntime();
}
