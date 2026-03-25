using Helper.RuntimeSlice.Contracts;

namespace Helper.RuntimeSlice.Api.Services;

public interface IRuntimeSliceEvolutionService
{
    EvolutionStatusSnapshotDto GetSnapshot();
}

internal sealed class RuntimeSliceEvolutionService : IRuntimeSliceEvolutionService
{
    private readonly FixtureFileStore _fixtures;

    public RuntimeSliceEvolutionService(RuntimeSliceOptions options)
    {
        _fixtures = new FixtureFileStore(options);
    }

    public EvolutionStatusSnapshotDto GetSnapshot()
    {
        return _fixtures.ReadJson<EvolutionStatusSnapshotDto>("evolution_status.json");
    }
}
