namespace Helper.Api.Hosting;

public interface IStartupTrafficMonitor
{
    IDisposable BeginRequest(string path);
    int ActiveInteractiveRequests { get; }
    DateTimeOffset? LastInteractiveRequestAtUtc { get; }
    Task WaitForInteractiveIdleAsync(CancellationToken ct);
}

public sealed class StartupTrafficMonitor : IStartupTrafficMonitor
{
    private readonly TimeSpan _idleWindow;
    private int _activeInteractiveRequests;
    private long _lastInteractiveRequestUnixMs;

    public StartupTrafficMonitor()
    {
        _idleWindow = TimeSpan.FromMilliseconds(ReadIdleWindowMs());
    }

    public int ActiveInteractiveRequests => Math.Max(0, Volatile.Read(ref _activeInteractiveRequests));

    public DateTimeOffset? LastInteractiveRequestAtUtc
    {
        get
        {
            var unixMs = Volatile.Read(ref _lastInteractiveRequestUnixMs);
            return unixMs <= 0 ? null : DateTimeOffset.FromUnixTimeMilliseconds(unixMs);
        }
    }

    public IDisposable BeginRequest(string path)
    {
        if (!IsInteractivePath(path))
        {
            return NullScope.Instance;
        }

        Interlocked.Increment(ref _activeInteractiveRequests);
        Interlocked.Exchange(ref _lastInteractiveRequestUnixMs, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        return new RequestScope(this);
    }

    public async Task WaitForInteractiveIdleAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (ActiveInteractiveRequests == 0)
            {
                var last = LastInteractiveRequestAtUtc;
                if (last == null || (DateTimeOffset.UtcNow - last.Value) >= _idleWindow)
                {
                    return;
                }
            }

            await Task.Delay(250, ct);
        }
    }

    private static bool IsInteractivePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        if (!path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !path.Equals("/api/health", StringComparison.OrdinalIgnoreCase) &&
               !path.Equals("/api/readiness", StringComparison.OrdinalIgnoreCase) &&
               !path.Equals("/api/smoke", StringComparison.OrdinalIgnoreCase) &&
               !path.Equals("/api/smoke/stream", StringComparison.OrdinalIgnoreCase) &&
               !path.Equals("/api/handshake", StringComparison.OrdinalIgnoreCase) &&
               !path.Equals("/api/openapi.json", StringComparison.OrdinalIgnoreCase) &&
               !path.Equals("/api/auth/session", StringComparison.OrdinalIgnoreCase);
    }

    private static int ReadIdleWindowMs()
    {
        var raw = Environment.GetEnvironmentVariable("HELPER_WARMUP_IDLE_WINDOW_MS");
        return int.TryParse(raw, out var parsed)
            ? Math.Clamp(parsed, 250, 5000)
            : 1200;
    }

    private sealed class RequestScope : IDisposable
    {
        private readonly StartupTrafficMonitor _owner;
        private int _disposed;

        public RequestScope(StartupTrafficMonitor owner)
        {
            _owner = owner;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            Interlocked.Decrement(ref _owner._activeInteractiveRequests);
            Interlocked.Exchange(ref _owner._lastInteractiveRequestUnixMs, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }
}

