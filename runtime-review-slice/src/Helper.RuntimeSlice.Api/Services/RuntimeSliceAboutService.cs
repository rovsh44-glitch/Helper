using Helper.RuntimeSlice.Contracts;

namespace Helper.RuntimeSlice.Api.Services;

public interface IRuntimeSliceAboutService
{
    RuntimeSliceAboutDto GetSnapshot();
}

internal sealed class RuntimeSliceAboutService : IRuntimeSliceAboutService
{
    private readonly RuntimeSliceOptions _options;

    public RuntimeSliceAboutService(RuntimeSliceOptions options)
    {
        _options = options;
    }

    public RuntimeSliceAboutDto GetSnapshot()
    {
        return new RuntimeSliceAboutDto(
            ProductName: _options.ProductName,
            SliceName: _options.SliceName,
            Status: "public_safe_fixture_backed",
            FixtureMode: _options.FixtureMode,
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            PublicBoundaries:
            [
                "read_only_endpoints",
                "sanitized_fixture_data",
                "no_model_keys",
                "no_private_core_scripts"
            ]);
    }
}
