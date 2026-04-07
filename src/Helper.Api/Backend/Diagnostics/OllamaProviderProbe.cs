using System.Diagnostics;
using System.Text.Json;
using Helper.Api.Backend.Providers;

namespace Helper.Api.Backend.Diagnostics;

public sealed class OllamaProviderProbe : IProviderProbe
{
    private readonly HttpMessageHandler? _handler;

    public OllamaProviderProbe(HttpMessageHandler? handler = null)
    {
        _handler = handler;
    }

    public bool CanProbe(ProviderProfileSummary summary)
        => summary.Profile.TransportKind == ProviderTransportKind.Ollama;

    public async Task<IReadOnlyList<ProviderDoctorCheck>> ProbeAsync(ProviderProfileSummary summary, CancellationToken ct)
    {
        var checks = new List<ProviderDoctorCheck>();
        using var request = new HttpRequestMessage(HttpMethod.Get, BuildUri(summary.Profile.BaseUrl, "api/tags"));

        try
        {
            using var client = CreateClient();
            var stopwatch = Stopwatch.StartNew();
            using var response = await client.SendAsync(request, ct);
            var payload = await response.Content.ReadAsStringAsync(ct);
            stopwatch.Stop();

            if (!response.IsSuccessStatusCode)
            {
                checks.Add(new ProviderDoctorCheck(
                    "ollama_tags",
                    "failed",
                    "error",
                    $"Ollama tags endpoint returned {(int)response.StatusCode}.",
                    string.IsNullOrWhiteSpace(payload) ? null : payload,
                    stopwatch.ElapsedMilliseconds));
                return checks;
            }

            var discoveredCount = CountNamedModels(payload, "models", "name");
            checks.Add(new ProviderDoctorCheck(
                "ollama_tags",
                "healthy",
                "info",
                "Ollama tags endpoint responded successfully.",
                $"Discovered {discoveredCount} model(s).",
                stopwatch.ElapsedMilliseconds));
            return checks;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            checks.Add(new ProviderDoctorCheck(
                "ollama_tags",
                "failed",
                "error",
                "Ollama probe failed.",
                ex.Message));
            return checks;
        }
    }

    private HttpClient CreateClient()
    {
        return _handler is null
            ? new HttpClient { Timeout = TimeSpan.FromSeconds(10) }
            : new HttpClient(_handler, disposeHandler: false) { Timeout = TimeSpan.FromSeconds(10) };
    }

    private static Uri BuildUri(string baseUrl, string path)
        => new($"{baseUrl.Trim().TrimEnd('/')}/{path.TrimStart('/')}", UriKind.Absolute);

    private static int CountNamedModels(string payload, string arrayProperty, string nameProperty)
    {
        using var doc = JsonDocument.Parse(payload);
        if (!doc.RootElement.TryGetProperty(arrayProperty, out var modelsNode) || modelsNode.ValueKind != JsonValueKind.Array)
        {
            return 0;
        }

        return modelsNode.EnumerateArray()
            .Count(model => model.TryGetProperty(nameProperty, out var nameNode) && !string.IsNullOrWhiteSpace(nameNode.GetString()));
    }
}
