using Helper.Runtime.Core;

namespace Helper.Runtime.Knowledge;

public sealed class IndexingTelemetrySink : IIndexingTelemetrySink
{
    private readonly object _sync = new();
    private IndexingTelemetry _current = new(string.Empty);

    public void Report(IndexingTelemetry telemetry)
    {
        lock (_sync)
        {
            _current = telemetry;
        }
    }

    public IndexingTelemetry Snapshot()
    {
        lock (_sync)
        {
            return _current;
        }
    }

    public void Reset()
    {
        lock (_sync)
        {
            _current = new IndexingTelemetry(string.Empty);
        }
    }
}

