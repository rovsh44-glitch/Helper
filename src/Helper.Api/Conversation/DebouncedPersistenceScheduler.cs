namespace Helper.Api.Conversation;

public sealed class DebouncedPersistenceScheduler : IDisposable
{
    private readonly TimeSpan _delay;
    private readonly Action _flush;
    private readonly object _sync = new();
    private Timer? _timer;
    private bool _disposed;

    public DebouncedPersistenceScheduler(TimeSpan delay, Action flush)
    {
        _delay = delay < TimeSpan.Zero ? TimeSpan.Zero : delay;
        _flush = flush ?? throw new ArgumentNullException(nameof(flush));
    }

    public void Request()
    {
        if (_disposed)
        {
            return;
        }

        if (_delay == TimeSpan.Zero)
        {
            _flush();
            return;
        }

        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _timer ??= new Timer(_ => FlushFromTimer(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _timer.Change(_delay, Timeout.InfiniteTimeSpan);
        }
    }

    public void FlushNow()
    {
        if (_disposed)
        {
            return;
        }

        lock (_sync)
        {
            _timer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }

        _flush();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        lock (_sync)
        {
            _timer?.Dispose();
            _timer = null;
        }

        _flush();
    }

    private void FlushFromTimer()
    {
        if (_disposed)
        {
            return;
        }

        _flush();
    }
}

