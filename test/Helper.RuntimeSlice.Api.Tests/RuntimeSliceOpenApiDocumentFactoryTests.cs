using System.Text.Json;
using Helper.RuntimeSlice.Api;

namespace Helper.RuntimeSlice.Api.Tests;

public sealed class RuntimeSliceOpenApiDocumentFactoryTests
{
    [Fact]
    public void Create_ContainsExpectedReadOnlyEndpoints()
    {
        var payload = JsonSerializer.Serialize(RuntimeSliceOpenApiDocumentFactory.Create());

        Assert.Contains("/api/about", payload, StringComparison.Ordinal);
        Assert.Contains("/api/runtime/logs", payload, StringComparison.Ordinal);
        Assert.Contains("/api/evolution/status", payload, StringComparison.Ordinal);
        Assert.Contains("/api/evolution/library", payload, StringComparison.Ordinal);
        Assert.Contains("/api/telemetry/routes", payload, StringComparison.Ordinal);
    }
}
