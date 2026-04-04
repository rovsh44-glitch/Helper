using Helper.RuntimeSlice.Api;
using Helper.RuntimeSlice.Api.Services;
using Helper.Runtime.Tests;

namespace Helper.RuntimeSlice.Api.Tests;

public sealed class RuntimeSliceLogServiceTests
{
    [Fact]
    public void GetSnapshot_LoadsSourcesAndProducesSemanticEntries()
    {
        var options = CreateOptions();
        var service = new RuntimeSliceLogService(options);

        var snapshot = service.GetSnapshot();

        Assert.Equal(2, snapshot.Sources.Count);
        Assert.NotEmpty(snapshot.Entries);
        Assert.All(snapshot.Entries, entry => Assert.DoesNotContain(@"C:\", entry.Text, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(snapshot.Entries, entry => entry.Semantics?.OperationKind == "http_request");
    }

    private static RuntimeSliceOptions CreateOptions()
    {
        var repoRoot = TestWorkspaceRoot.ResolveRoot();
        return new RuntimeSliceOptions(
            RepoRoot: repoRoot,
            FixtureRoot: Path.Combine(repoRoot, "slice", "runtime-review", "sample_data"),
            WebRoot: null,
            FixtureMode: true,
            ProductName: "Helper",
            SliceName: "Runtime Review Slice");
    }
}
