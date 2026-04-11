using Helper.Api.Backend.ModelGateway;
using Helper.Api.Backend.Providers;
using Helper.Api.Hosting;

namespace Helper.Runtime.Tests;

[Trait("Lane", "Fast")]
public sealed class ProviderProfileActivationServiceTests
{
    [Fact]
    public async Task ActivateAsync_AppliesOpenAiCompatibleRuntime_ToAiLink_AndRefreshesGateway()
    {
        using var temp = new TempDirectoryScope("helper_provider_activation_");
        using var env = new EnvironmentVariableScope(new Dictionary<string, string?>
        {
            ["HELPER_OPENAI_API_KEY"] = "activation-secret-key"
        });
        var config = new ApiRuntimeConfig(
            temp.Path,
            temp.Path,
            Path.Combine(temp.Path, "projects"),
            Path.Combine(temp.Path, "library"),
            Path.Combine(temp.Path, "logs"),
            Path.Combine(temp.Path, "templates"),
            "primary-key");
        var store = new ProviderProfileStore(config);
        store.SaveProfiles(new[]
        {
            new ProviderProfile(
                "custom_openai",
                "Custom OpenAI",
                ProviderKind.OpenAiCompatible,
                ProviderTransportKind.OpenAiCompatible,
                "https://api.example.com/v1",
                Enabled: true,
                IsBuiltIn: false,
                IsLocal: false,
                ProviderTrustMode.RemoteTrusted,
                new[] { ProviderWorkloadGoal.HostedReasoning, ProviderWorkloadGoal.ResearchVerified },
                new[]
                {
                    new ProviderModelClassBinding(HelperModelClass.Fast, "gpt-4.1-mini"),
                    new ProviderModelClassBinding(HelperModelClass.Reasoning, "gpt-4.1"),
                    new ProviderModelClassBinding(HelperModelClass.Coder, "gpt-4.1-coder"),
                    new ProviderModelClassBinding(HelperModelClass.Vision, "gpt-4.1-vision"),
                    new ProviderModelClassBinding(HelperModelClass.Critic, "gpt-4.1"),
                    new ProviderModelClassBinding(HelperModelClass.Background, "gpt-4.1")
                },
                new ProviderCredentialReference("HELPER_OPENAI_API_KEY", Required: true),
                EmbeddingModel: "text-embedding-3-large",
                PreferredReasoningEffort: "deep")
        });

        var catalog = new ProviderProfileCatalog(store, new ProviderProfileValidator(), new ProviderCapabilityMatrix());
        var ai = new Helper.Runtime.Infrastructure.AILink();
        var resolver = new ProviderProfileResolver(catalog, ai);
        var gateway = new RecordingModelGateway();
        var activation = new ProviderProfileActivationService(catalog, store, resolver, gateway);

        var result = await activation.ActivateAsync("custom_openai", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(1, gateway.DiscoverCalls);
        var settings = ai.GetRuntimeSettingsSnapshot();
        Assert.Equal(Helper.Runtime.Infrastructure.AiTransportKind.OpenAiCompatible, settings.TransportKind);
        Assert.Equal("https://api.example.com/v1", settings.BaseUrl);
        Assert.Equal("activation-secret-key", settings.ApiKey);
        Assert.Equal("gpt-4.1", settings.DefaultModel);
        Assert.Equal("text-embedding-3-large", settings.EmbeddingModel);
        Assert.Equal("gpt-4.1-coder", settings.ModelBindings?["coder"]);
        Assert.Equal("gpt-4.1", ai.GetCurrentModel());
    }

    private sealed class RecordingModelGateway : IModelGateway
    {
        public int DiscoverCalls { get; private set; }

        public Task DiscoverAsync(CancellationToken ct)
        {
            DiscoverCalls++;
            return Task.CompletedTask;
        }

        public IReadOnlyList<string> GetAvailableModelsSnapshot() => Array.Empty<string>();

        public string GetCurrentModel() => string.Empty;

        public string ResolveModel(HelperModelClass modelClass) => string.Empty;

        public Task WarmAsync(HelperModelClass modelClass, CancellationToken ct) => Task.CompletedTask;

        public Task<string> AskAsync(ModelGatewayRequest request, CancellationToken ct) => Task.FromResult(string.Empty);

        public IAsyncEnumerable<ModelGatewayStreamChunk> StreamAsync(ModelGatewayRequest request, CancellationToken ct) => Empty();

        public ModelGatewaySnapshot GetSnapshot() => new(Array.Empty<string>(), string.Empty, Array.Empty<ModelPoolSnapshot>(), null, null, Array.Empty<string>());

        private static async IAsyncEnumerable<ModelGatewayStreamChunk> Empty()
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
