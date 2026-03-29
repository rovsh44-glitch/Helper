using Helper.Runtime.WebResearch.Ranking;

namespace Helper.Runtime.WebResearch;

public sealed class WebQueryPlanner : IWebQueryPlanner
{
    public WebQueryPlanner()
        : this(new SearchTopicCoreRewritePolicy(), new SelectiveMultiQueryExpansionPolicy())
    {
    }

    internal WebQueryPlanner(
        ISearchTopicCoreRewritePolicy topicCoreRewritePolicy,
        ISelectiveMultiQueryExpansionPolicy multiQueryExpansionPolicy)
    {
        _topicCoreRewritePolicy = topicCoreRewritePolicy;
        _multiQueryExpansionPolicy = multiQueryExpansionPolicy;
    }

    private readonly ISearchTopicCoreRewritePolicy _topicCoreRewritePolicy;
    private readonly ISelectiveMultiQueryExpansionPolicy _multiQueryExpansionPolicy;

    public IReadOnlyList<WebSearchPlan> BuildPlans(WebSearchRequest request, SearchIterationBudget budget)
    {
        var normalizedInput = SearchQueryIntentProfileClassifier.NormalizeWhitespace(request.Query);
        var queryProfile = SourceAuthorityScorer.BuildQueryProfile(normalizedInput, null);
        var intentProfile = SearchQueryIntentProfileClassifier.Classify(normalizedInput, queryProfile);
        var primaryRewrite = _topicCoreRewritePolicy.Rewrite(normalizedInput);
        var baseQuery = SearchQueryIntentProfileClassifier.NormalizeWhitespace(primaryRewrite.Query);
        if (baseQuery.Length == 0)
        {
            return Array.Empty<WebSearchPlan>();
        }

        var plans = new List<WebSearchPlan>
        {
            CreatePlan(request, baseQuery, "primary", "standard", primaryRewrite.Trace)
        };

        foreach (var branch in _multiQueryExpansionPolicy.BuildBranches(baseQuery, intentProfile, queryProfile, budget))
        {
            plans.Add(CreatePlan(
                request,
                branch.Query,
                branch.QueryKind,
                branch.SearchMode,
                primaryRewrite.Trace.Concat(branch.Trace).ToArray()));
        }

        return plans
            .Where(static plan => !string.IsNullOrWhiteSpace(plan.Query))
            .DistinctBy(static plan => $"{plan.QueryKind}:{plan.Query}", StringComparer.OrdinalIgnoreCase)
            .Take(budget.MaxIterations)
            .ToArray();
    }

    private static WebSearchPlan CreatePlan(WebSearchRequest request, string query, string queryKind, string searchMode, IReadOnlyList<string>? rewriteTrace = null)
    {
        return new WebSearchPlan(
            query,
            Math.Clamp(request.MaxResults, 1, 10),
            Math.Clamp(request.Depth, 1, 3),
            string.IsNullOrWhiteSpace(request.Purpose) ? "research" : request.Purpose.Trim().ToLowerInvariant(),
            searchMode,
            request.AllowDeterministicFallback,
            queryKind,
            rewriteTrace);
    }
}

