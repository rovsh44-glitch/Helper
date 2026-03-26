using System.Net;
using System.Text.RegularExpressions;

namespace Helper.RuntimeSlice.Api.Tests;

public sealed class RuntimeSliceUiSmokeTests : IClassFixture<RuntimeSliceTestHostFactory>
{
    private readonly HttpClient _client;

    public RuntimeSliceUiSmokeTests(RuntimeSliceTestHostFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task RootPage_And_ShippedBundle_ExposeTheFourOperatorPanels()
    {
        using var indexResponse = await _client.GetAsync("/");
        var html = await indexResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, indexResponse.StatusCode);
        Assert.Contains("<div id=\"root\"></div>", html, StringComparison.Ordinal);

        var scriptMatch = Regex.Match(html, "src=\"(?<path>/assets/[^\"]+\\.js)\"", RegexOptions.CultureInvariant);
        Assert.True(scriptMatch.Success, "Expected the root page to reference a bundled JS asset.");

        var assetPath = scriptMatch.Groups["path"].Value;
        using var assetResponse = await _client.GetAsync(assetPath);
        var bundle = await assetResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, assetResponse.StatusCode);
        Assert.Contains("Runtime Console", bundle, StringComparison.Ordinal);
        Assert.Contains("Evolution", bundle, StringComparison.Ordinal);
        Assert.Contains("Library Indexing", bundle, StringComparison.Ordinal);
        Assert.Contains("Route Telemetry", bundle, StringComparison.Ordinal);
        Assert.Contains("sanitized sample data", bundle, StringComparison.OrdinalIgnoreCase);
    }
}
