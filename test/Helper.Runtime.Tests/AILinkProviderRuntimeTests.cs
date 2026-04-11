using System.Net;
using System.Net.Http;
using System.Text;
using Helper.Runtime.Infrastructure;

namespace Helper.Runtime.Tests;

[Trait("Lane", "Fast")]
public sealed class AILinkProviderRuntimeTests
{
    [Fact]
    public async Task DiscoverModelsAsync_UsesOpenAiCompatibleModelsEndpoint_AndBearerAuth()
    {
        var handler = new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"data":[{"id":"gpt-4.1-mini"},{"id":"gpt-4.1"}]}""", Encoding.UTF8, "application/json")
        });
        var ai = new AILink(
            new AiProviderRuntimeSettings(AiTransportKind.OpenAiCompatible, "https://api.example.com/v1", "gpt-4.1-mini", "secret-key"),
            handler);

        await ai.DiscoverModelsAsync(CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://api.example.com/v1/models", request.Uri);
        Assert.Equal("Bearer", request.AuthorizationScheme);
        Assert.Equal("secret-key", request.AuthorizationParameter);
        Assert.Contains("gpt-4.1-mini", ai.GetAvailableModelsSnapshot());
        Assert.Contains("gpt-4.1", ai.GetAvailableModelsSnapshot());
    }

    [Fact]
    public async Task AskAsync_UsesOpenAiCompatibleChatCompletionsPayload()
    {
        var handler = new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"choices":[{"message":{"content":"assistant reply"}}]}""", Encoding.UTF8, "application/json")
        });
        var ai = new AILink(
            new AiProviderRuntimeSettings(AiTransportKind.OpenAiCompatible, "https://api.example.com/v1", "gpt-4.1-mini", "secret-key"),
            handler);

        var response = await ai.AskAsync("hello", CancellationToken.None, systemInstruction: "be concise");

        Assert.Equal("assistant reply", response);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://api.example.com/v1/chat/completions", request.Uri);
        Assert.Contains("\"messages\"", request.Body, StringComparison.Ordinal);
        Assert.Contains("\"role\":\"system\"", request.Body, StringComparison.Ordinal);
        Assert.Contains("\"role\":\"user\"", request.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StreamAsync_ParsesOpenAiCompatibleServerSentEvents()
    {
        var handler = new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """
data: {"choices":[{"delta":{"content":"Hel"}}]}
data: {"choices":[{"delta":{"content":"lo"}}]}
data: {"choices":[{"delta":{},"finish_reason":"stop"}]}
data: [DONE]
""",
                Encoding.UTF8,
                "text/event-stream")
        });
        var ai = new AILink(
            new AiProviderRuntimeSettings(AiTransportKind.OpenAiCompatible, "https://api.example.com/v1", "gpt-4.1-mini", "secret-key"),
            handler);

        var tokens = new List<string>();
        await foreach (var token in ai.StreamAsync("hello", ct: CancellationToken.None))
        {
            tokens.Add(token);
        }

        Assert.Equal(new[] { "Hel", "lo" }, tokens);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://api.example.com/v1/chat/completions", request.Uri);
    }

    [Fact]
    public async Task EmbedAsync_UsesOpenAiCompatibleEmbeddingsEndpoint()
    {
        var handler = new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"data":[{"embedding":[0.1,0.2,0.3]}]}""", Encoding.UTF8, "application/json")
        });
        var ai = new AILink(
            new AiProviderRuntimeSettings(
                AiTransportKind.OpenAiCompatible,
                "https://api.example.com/v1",
                "gpt-4.1-mini",
                "secret-key",
                EmbeddingModel: "text-embedding-3-small"),
            handler);

        var embedding = await ai.EmbedAsync("hello world", CancellationToken.None);

        Assert.Equal(3, embedding.Length);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://api.example.com/v1/embeddings", request.Uri);
        Assert.Contains("\"model\":\"text-embedding-3-small\"", request.Body, StringComparison.Ordinal);
        Assert.Contains("\"input\":\"hello world\"", request.Body, StringComparison.Ordinal);
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
