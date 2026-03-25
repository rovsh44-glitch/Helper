namespace Helper.RuntimeSlice.Api;

public sealed record RuntimeSliceOptions(
    string RepoRoot,
    string FixtureRoot,
    string? WebRoot,
    bool FixtureMode,
    string ProductName,
    string SliceName);
