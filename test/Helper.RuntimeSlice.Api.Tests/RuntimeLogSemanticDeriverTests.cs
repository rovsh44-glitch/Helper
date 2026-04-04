using SharedRuntimeLogSemantics = Helper.RuntimeLogSemantics;

namespace Helper.RuntimeSlice.Api.Tests;

public sealed class RuntimeLogSemanticDeriverTests
{
    [Fact]
    public void Derive_ExtractsRouteLatencyAndCorrelation()
    {
        var semantics = SharedRuntimeLogSemantics.RuntimeLogSemanticDeriver.Derive(
            "2026-03-25T09:10:18Z info: GET /api/runtime/logs completed in 121 ms correlation-id=rt-review-001",
            "info",
            "slice/runtime-review/sample_data/logs/runtime-main.log",
            isContinuation: false);

        Assert.Equal("http_request", semantics.OperationKind);
        Assert.Equal("/api/runtime/logs", semantics.Route);
        Assert.Equal(121, semantics.LatencyMs);
        Assert.Equal("rt-review-001", semantics.CorrelationId);
        Assert.Contains("route:/api/runtime/logs", semantics.Markers ?? []);
    }
}
