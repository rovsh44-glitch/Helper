using Helper.RuntimeSlice.Contracts;

namespace Helper.RuntimeSlice.Api.Services;

public interface IRuntimeSliceLibraryService
{
    IReadOnlyList<LibraryItemDto> GetSnapshot();
}

internal sealed class RuntimeSliceLibraryService : IRuntimeSliceLibraryService
{
    private readonly FixtureFileStore _fixtures;

    public RuntimeSliceLibraryService(RuntimeSliceOptions options)
    {
        _fixtures = new FixtureFileStore(options);
    }

    public IReadOnlyList<LibraryItemDto> GetSnapshot()
    {
        return _fixtures.ReadJson<IReadOnlyList<LibraryItemDto>>("indexing_queue.json");
    }
}
