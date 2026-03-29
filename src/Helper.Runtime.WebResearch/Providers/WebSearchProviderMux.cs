namespace Helper.Runtime.WebResearch.Providers;

public sealed class WebSearchProviderMux : IWebSearchProviderClient
{
    private readonly IReadOnlyList<IWebSearchProvider> _providers;
    private readonly IReadOnlyList<string> _providerOrder;
    private readonly IWebProviderHealthState _healthState;
    private readonly ISearchCostBudgetPolicy _costBudgetPolicy;
    private readonly ITurnLatencyBudgetPolicy _latencyBudgetPolicy;

    public WebSearchProviderMux(IEnumerable<IWebSearchProvider> providers)
        : this(
            providers,
            new WebProviderHealthState(),
            new SearchCostBudgetPolicy(),
            new TurnLatencyBudgetPolicy())
    {
    }

    public WebSearchProviderMux(
        IEnumerable<IWebSearchProvider> providers,
        IWebProviderHealthState healthState,
        ISearchCostBudgetPolicy costBudgetPolicy,
        ITurnLatencyBudgetPolicy latencyBudgetPolicy)
    {
        _providers = providers.ToArray();
        _providerOrder = WebSearchProviderSettings.ReadProviderOrder();
        _healthState = healthState;
        _costBudgetPolicy = costBudgetPolicy;
        _latencyBudgetPolicy = latencyBudgetPolicy;
    }

    public WebSearchProviderMux(params IWebSearchProvider[] providers)
        : this((IEnumerable<IWebSearchProvider>)providers)
    {
    }

    public async Task<WebSearchProviderClientResponse> SearchAsync(WebSearchPlan plan, CancellationToken ct = default)
    {
        var trace = new List<string>();
        var orderedProviders = OrderProviders()
            .Select((provider, index) => new ProviderCandidate(provider, index))
            .ToArray();
        var attemptedProviders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var skippedProviders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var spentCostUnits = 0;
        var searchStopwatch = System.Diagnostics.Stopwatch.StartNew();
        var costBudget = _costBudgetPolicy.Resolve(plan);
        var latencyBudget = _latencyBudgetPolicy.Resolve(plan);
        trace.AddRange(costBudget.Trace);
        trace.AddRange(latencyBudget.Trace);

        while (attemptedProviders.Count + skippedProviders.Count < orderedProviders.Length)
        {
            var selection = SelectNextCandidate(
                orderedProviders,
                attemptedProviders,
                skippedProviders,
                plan,
                spentCostUnits,
                searchStopwatch.Elapsed,
                costBudget,
                latencyBudget,
                trace);
            if (selection is null)
            {
                trace.Add(
                    $"provider_governance.degraded_mode=yes reason=no_provider_available attempted={attemptedProviders.Count} skipped={skippedProviders.Count} spent_cost_units={spentCostUnits} elapsed_ms={searchStopwatch.ElapsedMilliseconds}");
                return new WebSearchProviderClientResponse(Array.Empty<WebSearchDocument>(), trace);
            }

            var provider = selection.Provider;
            attemptedProviders.Add(provider.ProviderId);
            trace.AddRange(selection.Trace);

            if (!provider.IsEnabled)
            {
                trace.Add($"{provider.ProviderId}:provider_disabled");
                skippedProviders.Add(provider.ProviderId);
                continue;
            }

            spentCostUnits += selection.CostUnits;
            var providerStopwatch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var response = await provider.SearchAsync(plan, ct).ConfigureAwait(false);
                providerStopwatch.Stop();
                var outcome = response.Documents.Count > 0
                    ? WebProviderExecutionOutcome.Success
                    : WebProviderExecutionOutcome.Empty;
                _healthState.Record(provider.ProviderId, outcome, providerStopwatch.Elapsed, response.Documents.Count);
                trace.AddRange(response.Trace);
                trace.Add(
                    $"provider_governance.result provider={provider.ProviderId} outcome={(response.Documents.Count > 0 ? "results" : "empty")} latency_ms={providerStopwatch.ElapsedMilliseconds} cost_units={selection.CostUnits}");
                if (response.Documents.Count > 0)
                {
                    return new WebSearchProviderClientResponse(response.Documents, trace);
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                providerStopwatch.Stop();
                _healthState.Record(provider.ProviderId, WebProviderExecutionOutcome.Timeout, providerStopwatch.Elapsed);
                trace.Add($"{provider.ProviderId}:timeout");
                trace.Add(
                    $"provider_governance.result provider={provider.ProviderId} outcome=timeout latency_ms={providerStopwatch.ElapsedMilliseconds} cost_units={selection.CostUnits}");
                continue;
            }
            catch
            {
                providerStopwatch.Stop();
                _healthState.Record(provider.ProviderId, WebProviderExecutionOutcome.Error, providerStopwatch.Elapsed);
                trace.Add($"{provider.ProviderId}:error");
                trace.Add(
                    $"provider_governance.result provider={provider.ProviderId} outcome=error latency_ms={providerStopwatch.ElapsedMilliseconds} cost_units={selection.CostUnits}");
                continue;
            }
        }

        trace.Add(
            $"provider_governance.degraded_mode=yes reason=all_providers_exhausted attempted={attemptedProviders.Count} spent_cost_units={spentCostUnits} elapsed_ms={searchStopwatch.ElapsedMilliseconds}");
        return new WebSearchProviderClientResponse(Array.Empty<WebSearchDocument>(), trace);
    }

    private ProviderSelection? SelectNextCandidate(
        IReadOnlyList<ProviderCandidate> candidates,
        HashSet<string> attemptedProviders,
        HashSet<string> skippedProviders,
        WebSearchPlan plan,
        int spentCostUnits,
        TimeSpan elapsed,
        SearchCostBudget costBudget,
        ProviderTurnLatencyBudget latencyBudget,
        List<string> trace)
    {
        ProviderSelection? bestSelection = null;

        foreach (var candidate in candidates)
        {
            if (attemptedProviders.Contains(candidate.Provider.ProviderId) ||
                skippedProviders.Contains(candidate.Provider.ProviderId))
            {
                continue;
            }

            if (!candidate.Provider.IsEnabled)
            {
                trace.Add($"{candidate.Provider.ProviderId}:provider_disabled");
                skippedProviders.Add(candidate.Provider.ProviderId);
                continue;
            }

            var health = _healthState.Evaluate(candidate.Provider.ProviderId, plan);
            var cost = _costBudgetPolicy.Evaluate(candidate.Provider.ProviderId, plan, spentCostUnits, costBudget);
            var latency = _latencyBudgetPolicy.Evaluate(
                candidate.Provider.ProviderId,
                plan,
                attemptedProviders.Count,
                elapsed,
                latencyBudget,
                health.RollingLatencyMs);

            if (!health.Allowed || !cost.Allowed || !latency.Allowed)
            {
                trace.AddRange(health.Trace);
                trace.AddRange(cost.Trace);
                trace.AddRange(latency.Trace);
                skippedProviders.Add(candidate.Provider.ProviderId);
                continue;
            }

            var score = (candidate.BaseOrderIndex * 4) + health.PriorityPenalty + cost.PriorityPenalty + latency.PriorityPenalty;
            var selection = new ProviderSelection(
                candidate.Provider,
                candidate.BaseOrderIndex,
                cost.CostUnits,
                score,
                health.Trace
                    .Concat(cost.Trace)
                    .Concat(latency.Trace)
                    .Append(
                        $"provider_governance.selection provider={candidate.Provider.ProviderId} score={score} base_order={candidate.BaseOrderIndex} health_penalty={health.PriorityPenalty} cost_penalty={cost.PriorityPenalty} latency_penalty={latency.PriorityPenalty}")
                    .ToArray());

            if (bestSelection is null ||
                selection.Score < bestSelection.Score ||
                (selection.Score == bestSelection.Score && selection.BaseOrderIndex < bestSelection.BaseOrderIndex))
            {
                bestSelection = selection;
            }
        }

        return bestSelection;
    }

    private IReadOnlyList<IWebSearchProvider> OrderProviders()
    {
        if (_providers.Count == 0)
        {
            return Array.Empty<IWebSearchProvider>();
        }

        var byId = _providers.ToDictionary(
            static provider => provider.ProviderId,
            StringComparer.OrdinalIgnoreCase);
        var ordered = new List<IWebSearchProvider>(_providers.Count);

        foreach (var providerId in _providerOrder)
        {
            if (byId.TryGetValue(providerId, out var provider))
            {
                ordered.Add(provider);
            }
        }

        foreach (var provider in _providers)
        {
            if (!ordered.Contains(provider))
            {
                ordered.Add(provider);
            }
        }

        return ordered;
    }

    private sealed record ProviderCandidate(
        IWebSearchProvider Provider,
        int BaseOrderIndex);

    private sealed record ProviderSelection(
        IWebSearchProvider Provider,
        int BaseOrderIndex,
        int CostUnits,
        int Score,
        IReadOnlyList<string> Trace);
}

