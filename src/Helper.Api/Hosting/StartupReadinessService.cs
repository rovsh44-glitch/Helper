using Helper.Api.Backend.Configuration;

namespace Helper.Api.Hosting;

public enum StartupLifecycleState
{
    Booting,
    Listening,
    DependenciesReady,
    MinimalReady,
    WarmReady,
    Degraded,
    Stopping
}

public sealed record StartupReadinessSnapshot(
    string Status,
    string Phase,
    string LifecycleState,
    bool ReadyForChat,
    bool Listening,
    string WarmupMode,
    DateTimeOffset? LastTransitionUtc,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? ListeningAtUtc,
    DateTimeOffset? MinimalReadyAtUtc,
    DateTimeOffset? WarmReadyAtUtc,
    long? TimeToListeningMs,
    long? TimeToReadyMs,
    long? TimeToWarmReadyMs,
    IReadOnlyList<string> Alerts);

public interface IStartupReadinessService
{
    StartupReadinessSnapshot GetSnapshot();
    void MarkListening();
    void MarkCatalogReady();
    void MarkDependenciesReady();
    void MarkMinimalReady(string phase);
    void MarkReadyForChat(string phase);
    void MarkWarmReady();
    void MarkDegraded(string alert, bool readyForChat);
    void MarkStopping();
}

public sealed class StartupReadinessService : IStartupReadinessService
{
    private const int MaxAlerts = 10;

    private readonly object _sync = new();
    private readonly string _warmupMode;
    private readonly List<string> _alerts = new();
    private readonly DateTimeOffset _startedAtUtc = DateTimeOffset.UtcNow;
    private string _status = "starting";
    private string _phase = "booting";
    private StartupLifecycleState _state = StartupLifecycleState.Booting;
    private bool _readyForChat;
    private DateTimeOffset? _lastTransitionUtc = DateTimeOffset.UtcNow;
    private DateTimeOffset? _listeningAtUtc;
    private DateTimeOffset? _minimalReadyAtUtc;
    private DateTimeOffset? _warmReadyAtUtc;

    public StartupReadinessService(IBackendOptionsCatalog? options = null)
    {
        _warmupMode = options?.Warmup.Mode ?? ReadWarmupMode();
    }

    public StartupReadinessSnapshot GetSnapshot()
    {
        lock (_sync)
        {
            return new StartupReadinessSnapshot(
                _status,
                _phase,
                _state.ToString().ToLowerInvariant(),
                _readyForChat,
                _listeningAtUtc.HasValue,
                _warmupMode,
                _lastTransitionUtc,
                _startedAtUtc,
                _listeningAtUtc,
                _minimalReadyAtUtc,
                _warmReadyAtUtc,
                ToDurationMs(_startedAtUtc, _listeningAtUtc),
                ToDurationMs(_startedAtUtc, _minimalReadyAtUtc),
                ToDurationMs(_startedAtUtc, _warmReadyAtUtc),
                _alerts.ToArray());
        }
    }

    public void MarkListening()
    {
        lock (_sync)
        {
            if (_phase != "booting")
            {
                return;
            }

            _phase = "listening";
            _state = StartupLifecycleState.Listening;
            _lastTransitionUtc = DateTimeOffset.UtcNow;
            _listeningAtUtc ??= _lastTransitionUtc;
        }
    }

    public void MarkCatalogReady()
    {
        MarkDependenciesReady();
    }

    public void MarkDependenciesReady()
    {
        lock (_sync)
        {
            if (_readyForChat)
            {
                return;
            }

            _phase = "dependencies_ready";
            _state = StartupLifecycleState.DependenciesReady;
            _lastTransitionUtc = DateTimeOffset.UtcNow;
        }
    }

    public void MarkMinimalReady(string phase)
    {
        lock (_sync)
        {
            _readyForChat = true;
            _phase = string.IsNullOrWhiteSpace(phase) ? "minimal_ready" : phase;
            _state = StartupLifecycleState.MinimalReady;
            if (!string.Equals(_status, "degraded", StringComparison.OrdinalIgnoreCase))
            {
                _status = "ready";
            }

            _lastTransitionUtc = DateTimeOffset.UtcNow;
            _minimalReadyAtUtc ??= _lastTransitionUtc;
        }
    }

    public void MarkReadyForChat(string phase)
    {
        lock (_sync)
        {
            _readyForChat = true;
            _phase = phase;
            _state = string.Equals(phase, "warmup_complete", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(phase, "warm_ready", StringComparison.OrdinalIgnoreCase)
                ? StartupLifecycleState.WarmReady
                : StartupLifecycleState.MinimalReady;
            if (!string.Equals(_status, "degraded", StringComparison.OrdinalIgnoreCase))
            {
                _status = "ready";
            }

            _lastTransitionUtc = DateTimeOffset.UtcNow;
            _minimalReadyAtUtc ??= _lastTransitionUtc;
            if (_state == StartupLifecycleState.WarmReady)
            {
                _warmReadyAtUtc ??= _lastTransitionUtc;
            }
        }
    }

    public void MarkWarmReady()
    {
        lock (_sync)
        {
            _readyForChat = true;
            _phase = "warm_ready";
            _state = StartupLifecycleState.WarmReady;
            if (!string.Equals(_status, "degraded", StringComparison.OrdinalIgnoreCase))
            {
                _status = "ready";
            }

            _lastTransitionUtc = DateTimeOffset.UtcNow;
            _minimalReadyAtUtc ??= _lastTransitionUtc;
            _warmReadyAtUtc ??= _lastTransitionUtc;
        }
    }

    public void MarkDegraded(string alert, bool readyForChat)
    {
        lock (_sync)
        {
            _status = "degraded";
            _state = StartupLifecycleState.Degraded;
            _readyForChat = _readyForChat || readyForChat;
            if (!_readyForChat)
            {
                _phase = "degraded";
            }

            if (!string.IsNullOrWhiteSpace(alert))
            {
                _alerts.Add(alert.Trim());
                if (_alerts.Count > MaxAlerts)
                {
                    _alerts.RemoveRange(0, _alerts.Count - MaxAlerts);
                }
            }

            _lastTransitionUtc = DateTimeOffset.UtcNow;
        }
    }

    public void MarkStopping()
    {
        lock (_sync)
        {
            _phase = "stopping";
            _state = StartupLifecycleState.Stopping;
            _lastTransitionUtc = DateTimeOffset.UtcNow;
        }
    }

    private static string ReadWarmupMode()
    {
        var raw = Environment.GetEnvironmentVariable("HELPER_MODEL_WARMUP_MODE");
        return raw?.Trim().ToLowerInvariant() switch
        {
            "disabled" => "disabled",
            "full" => "full",
            _ => "minimal"
        };
    }

    private static long? ToDurationMs(DateTimeOffset startedAtUtc, DateTimeOffset? endedAtUtc)
    {
        if (!endedAtUtc.HasValue)
        {
            return null;
        }

        return Math.Max(0L, (long)(endedAtUtc.Value - startedAtUtc).TotalMilliseconds);
    }
}

