using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Helper.RuntimeSlice.Contracts;

namespace Helper.RuntimeSlice.Api.Tests;

public sealed class RuntimeSliceEndpointIntegrationTests : IClassFixture<RuntimeSliceTestHostFactory>
{
    private readonly HttpClient _client;

    public RuntimeSliceEndpointIntegrationTests(RuntimeSliceTestHostFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task HealthEndpoint_ReturnsHealthyPayload()
    {
        using var response = await _client.GetAsync("/api/health");
        var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("healthy", payload.RootElement.GetProperty("status").GetString());
        Assert.Equal("runtime-review", payload.RootElement.GetProperty("slice").GetString());
    }

    [Fact]
    public async Task ReadinessEndpoint_ReturnsFixtureBackedSnapshot()
    {
        var snapshot = await _client.GetFromJsonAsync<StartupReadinessSnapshot>("/api/readiness");

        Assert.NotNull(snapshot);
        Assert.Equal("fixture", snapshot!.WarmupMode);
        Assert.NotEmpty(snapshot.Alerts);
    }

    [Fact]
    public async Task OpenApiEndpoint_ReturnsExpectedReadOnlySurface()
    {
        using var response = await _client.GetAsync("/api/openapi.json");
        var payload = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("/api/health", payload, StringComparison.Ordinal);
        Assert.Contains("/api/readiness", payload, StringComparison.Ordinal);
        Assert.Contains("/api/runtime/logs", payload, StringComparison.Ordinal);
        Assert.Contains("/api/evolution/status", payload, StringComparison.Ordinal);
        Assert.Contains("/api/evolution/library", payload, StringComparison.Ordinal);
        Assert.Contains("/api/telemetry/routes", payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RuntimeLogsEndpoint_ReturnsStructuredSanitizedEntries()
    {
        var snapshot = await _client.GetFromJsonAsync<RuntimeLogsSnapshotDto>("/api/runtime/logs?tail=5&maxSources=2");

        Assert.NotNull(snapshot);
        Assert.NotEmpty(snapshot!.Sources);
        Assert.NotEmpty(snapshot.Entries);
        Assert.All(snapshot.Sources, source =>
            Assert.DoesNotMatch(@"(?i)\b[a-z]:(?:\\|/)(?!redacted_runtime(?:\\|/))", source.DisplayPath));
        Assert.Contains(snapshot.Entries, entry => entry.Semantics is not null);
    }

    [Fact]
    public async Task EvolutionStatusEndpoint_ReturnsMeaningfulProgressState()
    {
        var snapshot = await _client.GetFromJsonAsync<EvolutionStatusSnapshotDto>("/api/evolution/status");

        Assert.NotNull(snapshot);
        Assert.True(snapshot!.ProcessedFiles > 0);
        Assert.True(snapshot.TotalFiles >= snapshot.ProcessedFiles);
        Assert.NotEmpty(snapshot.Goals);
    }

    [Fact]
    public async Task EvolutionLibraryEndpoint_ReturnsNonEmptyQueueSnapshot()
    {
        var items = await _client.GetFromJsonAsync<List<LibraryItemDto>>("/api/evolution/library");

        Assert.NotNull(items);
        Assert.NotEmpty(items!);
        Assert.Contains(items, item => item.Status.Equals("queued", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RouteTelemetryEndpoint_ReturnsAggregatedSnapshot()
    {
        var snapshot = await _client.GetFromJsonAsync<RouteTelemetrySnapshot>("/api/telemetry/routes");

        Assert.NotNull(snapshot);
        Assert.True(snapshot!.TotalEvents > 0);
        Assert.NotEmpty(snapshot.Routes);
        Assert.NotEmpty(snapshot.Recent);
        Assert.NotEmpty(snapshot.Alerts);
    }
}
