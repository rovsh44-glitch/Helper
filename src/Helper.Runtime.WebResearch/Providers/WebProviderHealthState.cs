using System.Collections.Concurrent;

namespace Helper.Runtime.WebResearch.Providers;

public enum WebProviderExecutionOutcome
{
    Success,
    Empty,
    Timeout,
    Error
}

public sealed record WebProviderHealthDecision(
    bool Allowed,
    string Status,
    string Reason,
    int PriorityPenalty,
    double RollingLatencyMs,
    IReadOnlyList<string> Trace);

public interface IWebProviderHealthState
{
    WebProviderHealthDecision Evaluate(string providerId, WebSearchPlan plan);
    void Record(string providerId, WebProviderExecutionOutcome outcome, TimeSpan latency, int resultCount = 0);
}

public sealed class WebProviderHealthState : IWebProviderHealthState
{
    private readonly ConcurrentDictionary<string, ProviderHealthAccumulator> _providers = new(StringComparer.OrdinalIgnoreCase);

    public WebProviderHealthDecision Evaluate(string providerId, WebSearchPlan plan)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        var state = _providers.GetOrAdd(providerId, static _ => new ProviderHealthAccumulator());
        var snapshot = state.Snapshot();
        var now = DateTimeOffset.UtcNow;

        if (snapshot.CooldownUntilUtc is { } cooldownUntil && cooldownUntil > now)
        {
            var remainingMs = Math.Max(1, (int)(cooldownUntil - now).TotalMilliseconds);
            return new WebProviderHealthDecision(
                false,
                "cooldown",
                "recent_failures",
                PriorityPenalty: 100,
                snapshot.RollingLatencyMs,
                new[]
                {
                    $"provider_governance.health provider={providerId} allowed=no status=cooldown reason=recent_failures remaining_ms={remainingMs}"
                });
        }

        var status = "healthy";
        var reason = "healthy";
        var penalty = 0;
        var slowThresholdMs = WebSearchProviderSettings.ReadSlowProviderLatencyMs();
        if (snapshot.ConsecutiveTimeouts > 0 || snapshot.ConsecutiveErrors > 0)
        {
            status = "degraded";
            reason = snapshot.ConsecutiveTimeouts >= snapshot.ConsecutiveErrors ? "timeout_streak" : "error_streak";
            penalty += 30;
        }

        if (snapshot.RollingLatencyMs >= slowThresholdMs)
        {
            status = "degraded";
            reason = reason == "healthy" ? "slow_latency" : reason;
            penalty += 16;
        }
        else if (snapshot.RollingLatencyMs >= slowThresholdMs * 0.6d)
        {
            penalty += 6;
        }

        return new WebProviderHealthDecision(
            true,
            status,
            reason,
            penalty,
            snapshot.RollingLatencyMs,
            new[]
            {
                $"provider_governance.health provider={providerId} allowed=yes status={status} reason={reason} rolling_latency_ms={snapshot.RollingLatencyMs:0} timeouts={snapshot.ConsecutiveTimeouts} errors={snapshot.ConsecutiveErrors}"
            });
    }

    public void Record(string providerId, WebProviderExecutionOutcome outcome, TimeSpan latency, int resultCount = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        var state = _providers.GetOrAdd(providerId, static _ => new ProviderHealthAccumulator());
        state.Record(outcome, latency, resultCount);
    }

    private sealed class ProviderHealthAccumulator
    {
        private readonly object _sync = new();
        private int _consecutiveTimeouts;
        private int _consecutiveErrors;
        private double _rollingLatencyMs;
        private DateTimeOffset? _cooldownUntilUtc;

        public void Record(WebProviderExecutionOutcome outcome, TimeSpan latency, int resultCount)
        {
            lock (_sync)
            {
                var latencyMs = Math.Max(1d, latency.TotalMilliseconds);
                _rollingLatencyMs = _rollingLatencyMs <= 0d
                    ? latencyMs
                    : ((_rollingLatencyMs * 0.7d) + (latencyMs * 0.3d));

                switch (outcome)
                {
                    case WebProviderExecutionOutcome.Success:
                    case WebProviderExecutionOutcome.Empty:
                        _consecutiveTimeouts = 0;
                        _consecutiveErrors = 0;
                        _cooldownUntilUtc = null;
                        break;
                    case WebProviderExecutionOutcome.Timeout:
                        _consecutiveTimeouts++;
                        _consecutiveErrors = 0;
                        if (_consecutiveTimeouts >= WebSearchProviderSettings.ReadMaxConsecutiveTimeouts())
                        {
                            _cooldownUntilUtc = DateTimeOffset.UtcNow.Add(WebSearchProviderSettings.ReadProviderCooldownWindow());
                        }
                        break;
                    case WebProviderExecutionOutcome.Error:
                        _consecutiveErrors++;
                        _consecutiveTimeouts = 0;
                        if (_consecutiveErrors >= WebSearchProviderSettings.ReadMaxConsecutiveErrors())
                        {
                            _cooldownUntilUtc = DateTimeOffset.UtcNow.Add(WebSearchProviderSettings.ReadProviderCooldownWindow());
                        }
                        break;
                }
            }
        }

        public ProviderHealthSnapshot Snapshot()
        {
            lock (_sync)
            {
                return new ProviderHealthSnapshot(
                    _consecutiveTimeouts,
                    _consecutiveErrors,
                    _rollingLatencyMs,
                    _cooldownUntilUtc);
            }
        }
    }

    private sealed record ProviderHealthSnapshot(
        int ConsecutiveTimeouts,
        int ConsecutiveErrors,
        double RollingLatencyMs,
        DateTimeOffset? CooldownUntilUtc);
}

