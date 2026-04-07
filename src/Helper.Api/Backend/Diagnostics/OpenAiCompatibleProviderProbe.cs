using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using Helper.Api.Backend.Providers;

namespace Helper.Api.Backend.Diagnostics;

public sealed class OpenAiCompatibleProviderProbe : IProviderProbe
{
    private readonly HttpMessageHandler? _handler;

    public OpenAiCompatibleProviderProbe(HttpMessageHandler? handler = null)
    {
        _handler = handler;
    }

    public bool CanProbe(ProviderProfileSummary summary)
        => summary.Profile.TransportKind == ProviderTransportKind.OpenAiCompatible;

    public async Task<IReadOnlyList<ProviderDoctorCheck>> ProbeAsync(ProviderProfileSummary summary, CancellationToken ct)
    {
        var checks = new List<ProviderDoctorCheck>();
        var credentialEnv = summary.Profile.Credential?.ApiKeyEnvVar;
        var apiKey = string.IsNullOrWhiteSpace(credentialEnv)
            ? null
            : Environment.GetEnvironmentVariable(credentialEnv);

        using var request = new HttpRequestMessage(HttpMethod.Get, BuildUri(summary.Profile.BaseUrl, "models"));
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
        }

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
                    "models_endpoint",
                    "failed",
                    response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden ? "critical" : "error",
                    $"OpenAI-compatible models endpoint returned {(int)response.StatusCode}.",
                    string.IsNullOrWhiteSpace(payload) ? null : payload,
                    stopwatch.ElapsedMilliseconds));
                return checks;
            }

            var discoveredCount = CountNamedModels(payload, "data", "id");
            checks.Add(new ProviderDoctorCheck(
                "models_endpoint",
                "healthy",
                "info",
                "OpenAI-compatible models endpoint responded successfully.",
                $"Discovered {discoveredCount} model(s).",
                stopwatch.ElapsedMilliseconds));
            return checks;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            checks.Add(new ProviderDoctorCheck(
                "models_endpoint",
                "failed",
                "error",
                "OpenAI-compatible models probe failed.",
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
        if (!doc.RootElement.TryGetProperty(arrayProperty, out var dataNode) || dataNode.ValueKind != JsonValueKind.Array)
        {
            return 0;
        }

        return dataNode.EnumerateArray()
            .Count(model => model.TryGetProperty(nameProperty, out var idNode) && !string.IsNullOrWhiteSpace(idNode.GetString()));
    }
}
