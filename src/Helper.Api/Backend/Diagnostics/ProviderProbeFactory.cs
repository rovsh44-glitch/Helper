using Helper.Api.Backend.Providers;

namespace Helper.Api.Backend.Diagnostics;

public sealed class ProviderProbeFactory : IProviderProbeFactory
{
    private readonly IReadOnlyList<IProviderProbe> _probes;

    public ProviderProbeFactory(IEnumerable<IProviderProbe> probes)
    {
        _probes = probes.ToArray();
    }

    public IProviderProbe Resolve(ProviderProfileSummary summary)
    {
        var probe = _probes.FirstOrDefault(candidate => candidate.CanProbe(summary));
        if (probe is null)
        {
            throw new InvalidOperationException($"No provider probe is registered for transport '{summary.Profile.TransportKind}'.");
        }

        return probe;
    }
}
