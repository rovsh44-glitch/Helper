using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Helper.Api.Backend.Configuration;
using Helper.Api.Backend.ModelGateway;
using Helper.Api.Backend.Providers;
using Helper.Api.Hosting;
using Helper.Runtime.Infrastructure;
using Helper.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace Helper.Runtime.Tests;

[Trait("Lane", "Fast")]
public sealed class ProviderProfileEndpointRuntimeIntegrationTests
{
    [Fact]
    public async Task ActivateEndpoint_RewiresRuntime_AndRefreshesCatalog_ThroughOpenAiCompatibleTransport()
    {
        using var temp = new TempDirectoryScope("helper_provider_endpoint_");
        using var env = new EnvironmentVariableScope(new Dictionary<string, string?>
        {
            ["HELPER_OPENAI_API_KEY"] = "endpoint-secret-key",
            ["HELPER_ACTIVE_PROVIDER_PROFILE_ID"] = null
        });

        var runtimeConfig = new ApiRuntimeConfig(
            temp.Path,
            temp.Path,
            Path.Combine(temp.Path, "projects"),
            Path.Combine(temp.Path, "library"),
            Path.Combine(temp.Path, "logs"),
            Path.Combine(temp.Path, "templates"),
            "primary-key");

        var store = new ProviderProfileStore(runtimeConfig);
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

        var handler = new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"data":[{"id":"gpt-4.1-mini"},{"id":"gpt-4.1"}]}""", Encoding.UTF8, "application/json")
        });
        var ai = new AILink(
            new AiProviderRuntimeSettings(AiTransportKind.Ollama, "http://localhost:11434", "qwen2.5-coder:14b"),
            handler);

        await using var app = BuildApp(runtimeConfig, ai);
        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync();

        try
        {
            var addressFeature = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
            var baseAddress = new Uri(Assert.Single(addressFeature!.Addresses));
            using var client = new HttpClient { BaseAddress = baseAddress };

            using var response = await client.PostAsJsonAsync(
                "/api/settings/provider-profiles/activate",
                new ProviderActivationRequestDto("custom_openai"));
            response.EnsureSuccessStatusCode();

            using var activationPayload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.True(activationPayload.RootElement.GetProperty("success").GetBoolean());
            Assert.Equal("custom_openai", activationPayload.RootElement.GetProperty("activeProfileId").GetString());

            using var activeResponse = await client.GetAsync("/api/settings/provider-profiles/active");
            activeResponse.EnsureSuccessStatusCode();
            using var activePayload = JsonDocument.Parse(await activeResponse.Content.ReadAsStringAsync());
            Assert.Equal("custom_openai", activePayload.RootElement.GetProperty("profile").GetProperty("id").GetString());
            Assert.True(activePayload.RootElement.GetProperty("isActive").GetBoolean());

            var request = Assert.Single(handler.Requests);
            Assert.Equal("https://api.example.com/v1/models", request.Uri);
            Assert.Equal("Bearer", request.AuthorizationScheme);
            Assert.Equal("endpoint-secret-key", request.AuthorizationParameter);

            var runtime = ai.GetRuntimeSettingsSnapshot();
            Assert.Equal(AiTransportKind.OpenAiCompatible, runtime.TransportKind);
            Assert.Equal("https://api.example.com/v1", runtime.BaseUrl);
            Assert.Equal("gpt-4.1", runtime.DefaultModel);
            Assert.Equal("endpoint-secret-key", runtime.ApiKey);
            Assert.Equal("text-embedding-3-large", runtime.EmbeddingModel);
            Assert.Equal("gpt-4.1-coder", runtime.ModelBindings?["coder"]);
        }
        finally
        {
            await app.StopAsync();
        }
    }

    private static WebApplication BuildApp(ApiRuntimeConfig runtimeConfig, AILink ai)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton(runtimeConfig);
        builder.Services.AddSingleton<IBackendOptionsCatalog, BackendOptionsCatalog>();
        builder.Services.AddSingleton<IBackendRuntimePolicyProvider>(sp => (IBackendRuntimePolicyProvider)sp.GetRequiredService<IBackendOptionsCatalog>());
        builder.Services.AddSingleton<IModelGatewayTelemetry, ModelGatewayTelemetry>();
        builder.Services.AddHelperApplicationServices(runtimeConfig);
        builder.Services.AddSingleton(ai);
        builder.Services.AddSingleton<IModelGateway>(sp => new HelperModelGateway(
            sp.GetRequiredService<AILink>(),
            sp.GetRequiredService<IBackendOptionsCatalog>(),
            sp.GetRequiredService<IModelGatewayTelemetry>(),
            sp.GetRequiredService<IProviderProfileResolver>()));

        var app = builder.Build();
        var mapMethod = typeof(EndpointRegistrationExtensions).GetMethod(
            "MapSettingsProviderProfileEndpoints",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(mapMethod);
        mapMethod!.Invoke(null, new object[] { app });
        return app;
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;

        public CapturingHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        public List<CapturedRequest> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new CapturedRequest(
                request.Method.Method,
                request.RequestUri?.ToString() ?? string.Empty,
                request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken),
                request.Headers.Authorization?.Scheme,
                request.Headers.Authorization?.Parameter));
            return _responseFactory(request);
        }
    }

    private sealed record CapturedRequest(
        string Method,
        string Uri,
        string Body,
        string? AuthorizationScheme,
        string? AuthorizationParameter);
}
