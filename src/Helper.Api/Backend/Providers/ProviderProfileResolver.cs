using Helper.Api.Backend.ModelGateway;
using Helper.Runtime.Infrastructure;

namespace Helper.Api.Backend.Providers;

public sealed class ProviderProfileResolver : IProviderProfileResolver
{
    private readonly IProviderProfileCatalog _catalog;
    private readonly AILink _aiLink;

    public ProviderProfileResolver(IProviderProfileCatalog catalog, AILink aiLink)
    {
        _catalog = catalog;
        _aiLink = aiLink;
    }

    public ProviderProfileSummary? GetActiveProfile() => _catalog.GetActiveProfile();

    public ProviderRuntimeConfiguration? GetRuntimeConfiguration()
    {
        var active = _catalog.GetActiveProfile();
        if (active is null)
        {
            return null;
        }

        string? apiKey = null;
        if (!string.IsNullOrWhiteSpace(active.Profile.Credential?.ApiKeyEnvVar))
        {
            apiKey = Environment.GetEnvironmentVariable(active.Profile.Credential.ApiKeyEnvVar!);
        }

        var defaultModel = ResolveModelBinding(HelperModelClass.Reasoning)
            ?? active.Profile.ModelBindings.FirstOrDefault(binding => !string.IsNullOrWhiteSpace(binding.ModelName))?.ModelName;

        return new ProviderRuntimeConfiguration(
            active.Profile.Id,
            active.Profile.TransportKind,
            active.Profile.BaseUrl,
            apiKey,
            defaultModel,
            active.Profile.EmbeddingModel);
    }

    public string? ResolveModelBinding(HelperModelClass modelClass)
    {
        var active = _catalog.GetActiveProfile();
        return active?.Profile.ModelBindings
            .FirstOrDefault(binding => binding.ModelClass == modelClass)?.ModelName;
    }

    public string? ResolvePreferredReasoningEffort()
        => _catalog.GetActiveProfile()?.Profile.PreferredReasoningEffort;

    public bool SupportsVision()
        => _catalog.GetActiveProfile()?.Capabilities.SupportsVision ?? false;

    public bool PrefersResearchVerification()
        => _catalog.GetActiveProfile()?.Profile.SupportedGoals.Contains(ProviderWorkloadGoal.ResearchVerified) ?? false;

    public bool IsLocalOnly()
        => _catalog.GetActiveProfile()?.Profile.IsLocal ?? false;

    public string? ApplyToRuntime()
    {
        var configuration = GetRuntimeConfiguration();
        if (configuration is null)
        {
            return null;
        }

        Environment.SetEnvironmentVariable("HELPER_ACTIVE_PROVIDER_PROFILE_ID", configuration.ProfileId);
        if (configuration.TransportKind == ProviderTransportKind.OpenAiCompatible)
        {
            Environment.SetEnvironmentVariable("HELPER_OPENAI_BASE_URL", configuration.BaseUrl);
            Environment.SetEnvironmentVariable("HELPER_OPENAI_DEFAULT_MODEL", configuration.DefaultModel);
            Environment.SetEnvironmentVariable("HELPER_OPENAI_API_KEY", configuration.ApiKey);
        }
        else
        {
            Environment.SetEnvironmentVariable("HELPER_AI_BASE_URL", configuration.BaseUrl);
        }

        Environment.SetEnvironmentVariable("HELPER_MODEL_EMBEDDING", configuration.EmbeddingModel);
        if (!string.IsNullOrWhiteSpace(configuration.DefaultModel))
        {
            _aiLink.SwitchModel(configuration.DefaultModel);
        }

        return configuration.ProfileId;
    }
}
