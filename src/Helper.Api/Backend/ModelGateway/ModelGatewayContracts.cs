namespace Helper.Api.Backend.ModelGateway;

public enum HelperModelClass
{
    Fast,
    Reasoning,
    Coder,
    Vision,
    Critic,
    Background
}

public enum ModelExecutionPool
{
    Interactive,
    Background,
    Maintenance
}

public sealed record ModelGatewayRequest(
    string Prompt,
    HelperModelClass ModelClass,
    ModelExecutionPool Pool,
    string? PreferredModel = null,
    string? SystemInstruction = null,
    int KeepAliveSeconds = 300);

public sealed record ModelGatewayStreamChunk(
    string Content,
    DateTimeOffset TimestampUtc,
    DateTimeOffset ModelStreamStartedAtUtc);

public sealed record ModelPoolSnapshot(
    string Pool,
    int InFlight,
    long TotalCalls,
    long FailedCalls,
    long TimeoutCalls,
    double AvgLatencyMs);

public sealed record ModelGatewaySnapshot(
    IReadOnlyList<string> AvailableModels,
    string CurrentModel,
    IReadOnlyList<ModelPoolSnapshot> Pools,
    DateTimeOffset? LastCatalogRefreshAtUtc,
    DateTimeOffset? LastWarmupAtUtc,
    IReadOnlyList<string> Alerts);

public interface IModelGateway
{
    Task DiscoverAsync(CancellationToken ct);
    IReadOnlyList<string> GetAvailableModelsSnapshot();
    string GetCurrentModel();
    string ResolveModel(HelperModelClass modelClass);
    Task WarmAsync(HelperModelClass modelClass, CancellationToken ct);
    Task<string> AskAsync(ModelGatewayRequest request, CancellationToken ct);
    IAsyncEnumerable<ModelGatewayStreamChunk> StreamAsync(ModelGatewayRequest request, CancellationToken ct);
    ModelGatewaySnapshot GetSnapshot();
}

