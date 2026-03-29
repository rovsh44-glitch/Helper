using Helper.RuntimeSlice.Api;
using Helper.RuntimeSlice.Api.Services;

namespace Helper.RuntimeSlice.Api.Tests;

public sealed class RuntimeSliceRouteTelemetryServiceTests
{
    [Fact]
    public void GetSnapshot_AggregatesBucketsAndAlerts()
    {
        var options = CreateOptions();
        var service = new RuntimeSliceRouteTelemetryService(options);

        var snapshot = service.GetSnapshot();

        Assert.Equal(5, snapshot.TotalEvents);
        Assert.Contains(snapshot.Alerts, alert => alert.Contains("degraded or failed", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(snapshot.Qualities, bucket => bucket.Key == "degraded");
        Assert.Contains(snapshot.Qualities, bucket => bucket.Key == "failed");
        Assert.Contains(snapshot.Qualities, bucket => bucket.Key == "blocked");
    }

    private static RuntimeSliceOptions CreateOptions()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        return new RuntimeSliceOptions(
            RepoRoot: repoRoot,
            FixtureRoot: Path.Combine(repoRoot, "slice", "runtime-review", "sample_data"),
            WebRoot: null,
            FixtureMode: true,
            ProductName: "Helper",
            SliceName: "Runtime Review Slice");
    }
}
