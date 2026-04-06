using Helper.Api.Backend.ModelGateway;

namespace Helper.Api.Backend.Providers;

public sealed class ProviderProfileCatalog : IProviderProfileCatalog
{
    private readonly IProviderProfileStore _store;
    private readonly IProviderProfileValidator _validator;
    private readonly IProviderCapabilityMatrix _capabilityMatrix;

    public ProviderProfileCatalog(
        IProviderProfileStore store,
        IProviderProfileValidator validator,
        IProviderCapabilityMatrix capabilityMatrix)
    {
        _store = store;
        _validator = validator;
        _capabilityMatrix = capabilityMatrix;
    }

    public ProviderProfilesSnapshot GetSnapshot()
    {
        var profiles = BuildProfiles();
        var active = ResolveActiveProfile(profiles);
        var summaries = profiles
            .Select(profile =>
            {
                var validation = _validator.Validate(profile, profiles.Select(item => item.Id).ToArray());
                var capabilities = _capabilityMatrix.Summarize(profile);
                return new ProviderProfileSummary(
                    profile,
                    validation,
                    capabilities,
                    active is not null && string.Equals(active.Id, profile.Id, StringComparison.OrdinalIgnoreCase));
            })
            .OrderByDescending(summary => summary.IsActive)
            .ThenBy(summary => summary.Profile.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var alerts = summaries
            .Where(summary => !summary.Validation.IsValid)
            .SelectMany(summary => summary.Validation.Alerts.Select(alert => $"{summary.Profile.Id}: {alert}"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new ProviderProfilesSnapshot(
            DateTimeOffset.UtcNow,
            active?.Id,
            summaries,
            alerts);
    }

    public ProviderProfileSummary? GetActiveProfile()
    {
        var snapshot = GetSnapshot();
        return snapshot.Profiles.FirstOrDefault(summary => summary.IsActive);
    }

    public ProviderProfileSummary? GetById(string profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId))
        {
            return null;
        }

        return GetSnapshot().Profiles.FirstOrDefault(summary =>
            string.Equals(summary.Profile.Id, profileId, StringComparison.OrdinalIgnoreCase));
    }

    private IReadOnlyList<ProviderProfile> BuildProfiles()
    {
        var profiles = new Dictionary<string, ProviderProfile>(StringComparer.OrdinalIgnoreCase);
        foreach (var builtIn in BuildBuiltInProfiles())
        {
            profiles[builtIn.Id] = builtIn;
        }

        foreach (var custom in _store.LoadProfiles())
        {
            profiles[custom.Id] = custom with { IsBuiltIn = false };
        }

        return profiles.Values.ToArray();
    }

    private ProviderProfile? ResolveActiveProfile(IReadOnlyList<ProviderProfile> profiles)
    {
        var configuredActiveId = _store.LoadActiveProfileId();
        if (!string.IsNullOrWhiteSpace(configuredActiveId))
        {
            var resolved = profiles.FirstOrDefault(profile =>
                string.Equals(profile.Id, configuredActiveId, StringComparison.OrdinalIgnoreCase));
            if (resolved is not null)
            {
                var validation = _validator.Validate(resolved, profiles.Select(profile => profile.Id).ToArray());
                if (validation.IsValid && resolved.Enabled)
                {
                    return resolved;
                }
            }
        }

        return profiles
            .Select(profile => (Profile: profile, Validation: _validator.Validate(profile, profiles.Select(item => item.Id).ToArray())))
            .Where(item => item.Profile.Enabled && item.Validation.IsValid)
            .Select(item => item.Profile)
            .OrderByDescending(profile => profile.IsBuiltIn)
            .ThenBy(profile => profile.DisplayName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static IReadOnlyList<ProviderProfile> BuildBuiltInProfiles()
    {
        var profiles = new List<ProviderProfile>
        {
            new(
                Id: "local_ollama_default",
                DisplayName: "Local Ollama",
                Kind: ProviderKind.Ollama,
                TransportKind: ProviderTransportKind.Ollama,
                BaseUrl: Environment.GetEnvironmentVariable("HELPER_AI_BASE_URL")?.Trim() ?? "http://localhost:11434",
                Enabled: true,
                IsBuiltIn: true,
                IsLocal: true,
                TrustMode: ProviderTrustMode.Local,
                SupportedGoals: new[]
                {
                    ProviderWorkloadGoal.LocalFast,
                    ProviderWorkloadGoal.LocalCoder,
                    ProviderWorkloadGoal.PrivacyFirst
                },
                ModelBindings: BuildDefaultOllamaBindings(),
                Credential: null,
                EmbeddingModel: Environment.GetEnvironmentVariable("HELPER_MODEL_EMBEDDING")?.Trim(),
                PreferredReasoningEffort: "balanced",
                Notes: "Built-in local Ollama profile.")
        };

        var openAiBaseUrl = Environment.GetEnvironmentVariable("HELPER_OPENAI_BASE_URL")?.Trim()
            ?? Environment.GetEnvironmentVariable("OPENAI_BASE_URL")?.Trim();
        var openAiApiKeyEnv = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HELPER_OPENAI_API_KEY"))
            ? "HELPER_OPENAI_API_KEY"
            : !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPENAI_API_KEY"))
                ? "OPENAI_API_KEY"
                : "HELPER_OPENAI_API_KEY";
        if (!string.IsNullOrWhiteSpace(openAiBaseUrl))
        {
            profiles.Add(new ProviderProfile(
                Id: "openai_compatible_env",
                DisplayName: "OpenAI-Compatible",
                Kind: ProviderKind.OpenAiCompatible,
                TransportKind: ProviderTransportKind.OpenAiCompatible,
                BaseUrl: openAiBaseUrl,
                Enabled: true,
                IsBuiltIn: true,
                IsLocal: false,
                TrustMode: ProviderTrustMode.RemoteTrusted,
                SupportedGoals: new[]
                {
                    ProviderWorkloadGoal.HostedReasoning,
                    ProviderWorkloadGoal.ResearchVerified
                },
                ModelBindings: BuildDefaultOpenAiCompatibleBindings(),
                Credential: new ProviderCredentialReference(openAiApiKeyEnv, Required: true),
                EmbeddingModel: Environment.GetEnvironmentVariable("HELPER_OPENAI_EMBEDDING_MODEL")?.Trim()
                    ?? Environment.GetEnvironmentVariable("HELPER_MODEL_EMBEDDING")?.Trim()
                    ?? "text-embedding-3-small",
                PreferredReasoningEffort: "deep",
                Notes: "Built-in OpenAI-compatible profile sourced from environment."));
        }

        return profiles;
    }

    private static IReadOnlyList<ProviderModelClassBinding> BuildDefaultOllamaBindings()
    {
        return new[]
        {
            new ProviderModelClassBinding(HelperModelClass.Fast, Environment.GetEnvironmentVariable("HELPER_MODEL_FAST")?.Trim() ?? "command-r7b:7b"),
            new ProviderModelClassBinding(HelperModelClass.Reasoning, Environment.GetEnvironmentVariable("HELPER_MODEL_REASONING")?.Trim() ?? "qwen3:30b"),
            new ProviderModelClassBinding(HelperModelClass.Coder, Environment.GetEnvironmentVariable("HELPER_MODEL_CODER")?.Trim() ?? "qwen2.5-coder:14b"),
            new ProviderModelClassBinding(HelperModelClass.Vision, Environment.GetEnvironmentVariable("HELPER_MODEL_VISION")?.Trim() ?? "qwen3-vl:8b"),
            new ProviderModelClassBinding(HelperModelClass.Critic, Environment.GetEnvironmentVariable("HELPER_MODEL_CRITIC")?.Trim() ?? (Environment.GetEnvironmentVariable("HELPER_MODEL_REASONING")?.Trim() ?? "qwen3:30b")),
            new ProviderModelClassBinding(HelperModelClass.Background, Environment.GetEnvironmentVariable("HELPER_MODEL_REASONING")?.Trim() ?? "qwen3:30b")
        };
    }

    private static IReadOnlyList<ProviderModelClassBinding> BuildDefaultOpenAiCompatibleBindings()
    {
        var defaultModel = Environment.GetEnvironmentVariable("HELPER_OPENAI_DEFAULT_MODEL")?.Trim()
            ?? Environment.GetEnvironmentVariable("OPENAI_MODEL")?.Trim()
            ?? "gpt-4.1-mini";

        return new[]
        {
            new ProviderModelClassBinding(HelperModelClass.Fast, defaultModel),
            new ProviderModelClassBinding(HelperModelClass.Reasoning, defaultModel),
            new ProviderModelClassBinding(HelperModelClass.Coder, defaultModel),
            new ProviderModelClassBinding(HelperModelClass.Vision, defaultModel),
            new ProviderModelClassBinding(HelperModelClass.Critic, defaultModel),
            new ProviderModelClassBinding(HelperModelClass.Background, defaultModel)
        };
    }
}
