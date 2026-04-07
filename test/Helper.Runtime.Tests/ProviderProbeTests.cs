using System.Net;
using System.Net.Http;
using System.Text;
using Helper.Api.Backend.Diagnostics;
using Helper.Api.Backend.ModelGateway;
using Helper.Api.Backend.Providers;

namespace Helper.Runtime.Tests;

[Trait("Lane", "Fast")]
public sealed class ProviderProbeTests
{
    [Fact]
    public async Task OllamaProbe_ReportsHealthy_WhenTagsEndpointResponds()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"models":[{"name":"qwen3:30b"},{"name":"command-r7b:7b"}]}""", Encoding.UTF8, "application/json")
        });
        var probe = new OllamaProviderProbe(handler);

        var checks = await probe.ProbeAsync(BuildSummary(ProviderTransportKind.Ollama), CancellationToken.None);

        var check = Assert.Single(checks);
        Assert.Equal("healthy", check.Status);
        Assert.Contains("Discovered 2 model(s).", check.Detail);
    }

    [Fact]
    public async Task OpenAiCompatibleProbe_ReportsFailed_WhenUnauthorized()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("""{"error":"unauthorized"}""", Encoding.UTF8, "application/json")
        });
        var probe = new OpenAiCompatibleProviderProbe(handler);

        var checks = await probe.ProbeAsync(BuildSummary(ProviderTransportKind.OpenAiCompatible), CancellationToken.None);

        var check = Assert.Single(checks);
        Assert.Equal("failed", check.Status);
        Assert.Contains("returned 401", check.Summary, StringComparison.Ordinal);
    }

    private static ProviderProfileSummary BuildSummary(ProviderTransportKind transportKind)
    {
        return new ProviderProfileSummary(
            new ProviderProfile(
                "profile",
                "Profile",
                transportKind == ProviderTransportKind.Ollama ? ProviderKind.Ollama : ProviderKind.OpenAiCompatible,
                transportKind,
                transportKind == ProviderTransportKind.Ollama ? "http://localhost:11434" : "https://api.example.com/v1",
                Enabled: true,
                IsBuiltIn: false,
                IsLocal: transportKind == ProviderTransportKind.Ollama,
                transportKind == ProviderTransportKind.Ollama ? ProviderTrustMode.Local : ProviderTrustMode.RemoteTrusted,
                new[] { ProviderWorkloadGoal.LocalFast },
                new[] { new ProviderModelClassBinding(HelperModelClass.Reasoning, "model") },
                transportKind == ProviderTransportKind.OpenAiCompatible ? new ProviderCredentialReference("HELPER_OPENAI_API_KEY", Required: true) : null),
            new ProviderProfileValidationResult(true, Array.Empty<string>(), Array.Empty<string>()),
            new ProviderCapabilitySummary(true, true, true, transportKind != ProviderTransportKind.Ollama, true, true, transportKind == ProviderTransportKind.Ollama, transportKind == ProviderTransportKind.Ollama),
            IsActive: false);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_responseFactory(request));
        }
    }
}
