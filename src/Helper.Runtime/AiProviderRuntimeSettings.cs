using System.Collections.Generic;

namespace Helper.Runtime.Infrastructure;

public enum AiTransportKind
{
    Ollama,
    OpenAiCompatible
}

public sealed record AiProviderRuntimeSettings(
    AiTransportKind TransportKind,
    string BaseUrl,
    string DefaultModel,
    string? ApiKey = null,
    string? EmbeddingModel = null,
    IReadOnlyDictionary<string, string>? ModelBindings = null);
