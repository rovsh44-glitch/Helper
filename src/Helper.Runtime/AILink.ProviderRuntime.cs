using System.Collections.ObjectModel;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Helper.Runtime.Infrastructure;

public partial class AILink
{
    public AILink(string baseUrl = "http://localhost:11434", string defaultModel = "qwen2.5-coder:14b")
        : this(new AiProviderRuntimeSettings(AiTransportKind.Ollama, baseUrl, defaultModel))
    {
    }

    public AILink(AiProviderRuntimeSettings runtimeSettings, HttpMessageHandler? handler = null)
    {
        _httpClient = handler is null
            ? new HttpClient(new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromSeconds(HttpPooledConnectionLifetimeSeconds),
                PooledConnectionIdleTimeout = TimeSpan.FromSeconds(HttpPooledConnectionIdleTimeoutSeconds),
                MaxConnectionsPerServer = 8
            })
            : new HttpClient(handler, disposeHandler: false);
        _httpClient.Timeout = TimeSpan.FromSeconds(HttpTimeoutSeconds);

        ApplyRuntime(runtimeSettings);
    }

    public void ApplyRuntime(AiProviderRuntimeSettings runtimeSettings)
    {
        var normalized = NormalizeRuntimeSettings(runtimeSettings);
        lock (_modelCatalogLock)
        {
            _runtimeSettings = normalized;
            _currentModel = normalized.DefaultModel;
            _availableModels = SeedCatalog(normalized);
        }
    }

    public AiProviderRuntimeSettings GetRuntimeSettingsSnapshot()
    {
        lock (_modelCatalogLock)
        {
            return CloneRuntimeSettings(_runtimeSettings);
        }
    }

    private static AiProviderRuntimeSettings CloneRuntimeSettings(AiProviderRuntimeSettings settings)
    {
        return new AiProviderRuntimeSettings(
            settings.TransportKind,
            settings.BaseUrl,
            settings.DefaultModel,
            settings.ApiKey,
            settings.EmbeddingModel,
            CloneBindings(settings.ModelBindings));
    }

    private static AiProviderRuntimeSettings NormalizeRuntimeSettings(AiProviderRuntimeSettings settings)
    {
        var transport = settings.TransportKind;
        var baseUrl = string.IsNullOrWhiteSpace(settings.BaseUrl)
            ? transport == AiTransportKind.OpenAiCompatible ? "https://api.openai.com/v1" : "http://localhost:11434"
            : settings.BaseUrl.Trim();
        var defaultModel = string.IsNullOrWhiteSpace(settings.DefaultModel)
            ? transport == AiTransportKind.OpenAiCompatible ? "gpt-4.1-mini" : "qwen2.5-coder:14b"
            : settings.DefaultModel.Trim();
        var embeddingModel = string.IsNullOrWhiteSpace(settings.EmbeddingModel)
            ? null
            : settings.EmbeddingModel.Trim();
        var apiKey = string.IsNullOrWhiteSpace(settings.ApiKey)
            ? null
            : settings.ApiKey.Trim();

        return new AiProviderRuntimeSettings(
            transport,
            baseUrl,
            defaultModel,
            apiKey,
            embeddingModel,
            CloneBindings(settings.ModelBindings));
    }

    private static IReadOnlyDictionary<string, string> CloneBindings(IReadOnlyDictionary<string, string>? bindings)
    {
        var normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (bindings is not null)
        {
            foreach (var binding in bindings)
            {
                if (string.IsNullOrWhiteSpace(binding.Key) || string.IsNullOrWhiteSpace(binding.Value))
                {
                    continue;
                }

                normalized[binding.Key.Trim()] = binding.Value.Trim();
            }
        }

        return new ReadOnlyDictionary<string, string>(normalized);
    }

    private static List<string> SeedCatalog(AiProviderRuntimeSettings settings)
    {
        var seeded = new List<string>();
        if (!string.IsNullOrWhiteSpace(settings.DefaultModel))
        {
            seeded.Add(settings.DefaultModel);
        }

        if (settings.ModelBindings is not null)
        {
            foreach (var binding in settings.ModelBindings.Values)
            {
                if (!string.IsNullOrWhiteSpace(binding))
                {
                    seeded.Add(binding);
                }
            }
        }

        return seeded
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private string? ResolveRuntimeCategoryBinding(string category)
    {
        var settings = GetRuntimeSettingsSnapshot();
        if (settings.ModelBindings is null || settings.ModelBindings.Count == 0)
        {
            return null;
        }

        return settings.ModelBindings.TryGetValue(category, out var binding) && !string.IsNullOrWhiteSpace(binding)
            ? binding
            : null;
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, AiProviderRuntimeSettings settings, string relativePath, string? payload = null)
    {
        var request = new HttpRequestMessage(method, BuildEndpointUri(settings, relativePath));
        if (!string.IsNullOrWhiteSpace(payload))
        {
            request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
        }

        if (settings.TransportKind == AiTransportKind.OpenAiCompatible &&
            !string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        }

        return request;
    }

    private static Uri BuildEndpointUri(AiProviderRuntimeSettings settings, string relativePath)
    {
        var baseUrl = settings.BaseUrl.Trim().TrimEnd('/');
        var path = relativePath.TrimStart('/');
        return new Uri($"{baseUrl}/{path}", UriKind.Absolute);
    }

    private static List<string> ParseDiscoveredModels(string json, AiTransportKind transportKind)
    {
        using var doc = JsonDocument.Parse(json);
        return transportKind == AiTransportKind.OpenAiCompatible
            ? ParseOpenAiCompatibleModels(doc)
            : ParseOllamaModels(doc);
    }

    private static List<string> ParseOllamaModels(JsonDocument doc)
    {
        if (!doc.RootElement.TryGetProperty("models", out var models) || models.ValueKind != JsonValueKind.Array)
        {
            return new List<string>();
        }

        return models.EnumerateArray()
            .Select(model => model.TryGetProperty("name", out var node) ? node.GetString() : null)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<string> ParseOpenAiCompatibleModels(JsonDocument doc)
    {
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            return new List<string>();
        }

        return data.EnumerateArray()
            .Select(model => model.TryGetProperty("id", out var node) ? node.GetString() : null)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string ResolveEmbeddingModel(AiProviderRuntimeSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.EmbeddingModel))
        {
            return settings.EmbeddingModel!;
        }

        return settings.TransportKind == AiTransportKind.OpenAiCompatible
            ? "text-embedding-3-small"
            : "nomic-embed-text:latest";
    }
}
