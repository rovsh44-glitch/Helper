using Helper.RuntimeSlice.Contracts;

namespace Helper.RuntimeSlice.Api.Services;

public interface IRuntimeSliceReadinessService
{
    StartupReadinessSnapshot GetSnapshot();
}

internal sealed class RuntimeSliceReadinessService : IRuntimeSliceReadinessService
{
    private readonly FixtureFileStore _fixtures;

    public RuntimeSliceReadinessService(RuntimeSliceOptions options)
    {
        _fixtures = new FixtureFileStore(options);
    }

    public StartupReadinessSnapshot GetSnapshot()
    {
        return _fixtures.ReadJson<StartupReadinessSnapshot>("readiness.json");
    }
}
