namespace Helper.Runtime.WebResearch.Providers;

public sealed record ProviderTurnLatencyBudget(
    int MaxAttempts,
    TimeSpan MaxTotalLatency,
    IReadOnlyList<string> Trace);

public sealed record ProviderLatencyDecision(
    bool Allowed,
    int PriorityPenalty,
    string Reason,
    IReadOnlyList<string> Trace);

public interface ITurnLatencyBudgetPolicy
{
    ProviderTurnLatencyBudget Resolve(WebSearchPlan plan);
    ProviderLatencyDecision Evaluate(
        string providerId,
        WebSearchPlan plan,
        int attemptsUsed,
        TimeSpan elapsed,
        ProviderTurnLatencyBudget budget,
        double observedLatencyMs = 0d);
}

public sealed class TurnLatencyBudgetPolicy : ITurnLatencyBudgetPolicy
{
    public ProviderTurnLatencyBudget Resolve(WebSearchPlan plan)
    {
        var baseMs = WebSearchProviderSettings.ReadSearchLatencyBudgetMs();
        var adjustedMs = plan.SearchMode switch
        {
            "verification" => baseMs + 1800,
            "freshness" => baseMs + 900,
            "focused" => baseMs,
            _ => baseMs
        };
        var maxAttempts = plan.SearchMode == "verification" ? 3 : 2;

        return new ProviderTurnLatencyBudget(
            maxAttempts,
            TimeSpan.FromMilliseconds(Math.Clamp(adjustedMs, 250, 30000)),
            new[]
            {
                $"provider_governance.latency_budget max_attempts={maxAttempts} max_total_ms={Math.Clamp(adjustedMs, 250, 30000)} search_mode={plan.SearchMode}"
            });
    }

    public ProviderLatencyDecision Evaluate(
        string providerId,
        WebSearchPlan plan,
        int attemptsUsed,
        TimeSpan elapsed,
        ProviderTurnLatencyBudget budget,
        double observedLatencyMs = 0d)
    {
        if (attemptsUsed >= budget.MaxAttempts)
        {
            return new ProviderLatencyDecision(
                false,
                100,
                "attempt_limit",
                new[]
                {
                    $"provider_governance.latency provider={providerId} allowed=no reason=attempt_limit attempts_used={attemptsUsed} max_attempts={budget.MaxAttempts}"
                });
        }

        var nominalLatencyMs = ResolveNominalLatencyMs(providerId, observedLatencyMs);
        var wouldExceedBudget = elapsed.TotalMilliseconds + nominalLatencyMs > budget.MaxTotalLatency.TotalMilliseconds &&
                                attemptsUsed > 0;
        if (wouldExceedBudget)
        {
            return new ProviderLatencyDecision(
                false,
                100,
                "would_exceed_total_budget",
                new[]
                {
                    $"provider_governance.latency provider={providerId} allowed=no reason=would_exceed_total_budget elapsed_ms={Math.Max(0, (int)elapsed.TotalMilliseconds)} predicted_ms={Math.Max(0, (int)nominalLatencyMs)} max_total_ms={Math.Max(0, (int)budget.MaxTotalLatency.TotalMilliseconds)}"
                });
        }

        var penalty = nominalLatencyMs switch
        {
            <= 400 => 0,
            <= 900 => 7,
            _ => 14
        };

        return new ProviderLatencyDecision(
            true,
            penalty,
            "within_budget",
            new[]
            {
                $"provider_governance.latency provider={providerId} allowed=yes reason=within_budget predicted_ms={Math.Max(0, (int)nominalLatencyMs)} elapsed_ms={Math.Max(0, (int)elapsed.TotalMilliseconds)}"
            });
    }

    private static double ResolveNominalLatencyMs(string providerId, double observedLatencyMs)
    {
        if (observedLatencyMs > 0d)
        {
            return observedLatencyMs;
        }

        return providerId.ToLowerInvariant() switch
        {
            LocalSearchProvider.Id => 220d,
            SearxSearchProvider.Id => 900d,
            _ => 700d
        };
    }
}

