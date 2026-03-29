namespace Helper.Runtime.WebResearch.Providers;

public sealed record SearchCostBudget(
    int MaxUnits,
    IReadOnlyList<string> Trace);

public sealed record ProviderCostDecision(
    bool Allowed,
    int CostUnits,
    int PriorityPenalty,
    string Reason,
    IReadOnlyList<string> Trace);

public interface ISearchCostBudgetPolicy
{
    SearchCostBudget Resolve(WebSearchPlan plan);
    ProviderCostDecision Evaluate(string providerId, WebSearchPlan plan, int spentUnits, SearchCostBudget budget);
}

public sealed class SearchCostBudgetPolicy : ISearchCostBudgetPolicy
{
    public SearchCostBudget Resolve(WebSearchPlan plan)
    {
        var baseBudget = WebSearchProviderSettings.ReadSearchCostBudgetUnits();
        var adjusted = plan.SearchMode switch
        {
            "verification" => baseBudget + 2,
            "freshness" => baseBudget + 1,
            "focused" => baseBudget,
            _ => baseBudget
        };

        return new SearchCostBudget(
            Math.Clamp(adjusted, 0, 8),
            new[]
            {
                $"provider_governance.cost_budget max_units={Math.Clamp(adjusted, 0, 8)} search_mode={plan.SearchMode} query_kind={plan.QueryKind}"
            });
    }

    public ProviderCostDecision Evaluate(string providerId, WebSearchPlan plan, int spentUnits, SearchCostBudget budget)
    {
        var costUnits = ResolveCostUnits(providerId);
        var allowed = spentUnits + costUnits <= budget.MaxUnits;
        return new ProviderCostDecision(
            allowed,
            costUnits,
            PriorityPenalty: Math.Max(0, (costUnits - 1) * 8),
            Reason: allowed ? "within_budget" : "cost_budget_exhausted",
            Trace: new[]
            {
                $"provider_governance.cost provider={providerId} allowed={(allowed ? "yes" : "no")} reason={(allowed ? "within_budget" : "cost_budget_exhausted")} spent={spentUnits} cost_units={costUnits} max_units={budget.MaxUnits}"
            });
    }

    private static int ResolveCostUnits(string providerId)
    {
        return providerId.ToLowerInvariant() switch
        {
            LocalSearchProvider.Id => 1,
            SearxSearchProvider.Id => 2,
            _ => 2
        };
    }
}

