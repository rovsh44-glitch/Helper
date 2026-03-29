using Helper.Runtime.WebResearch.Ranking;

namespace Helper.Runtime.WebResearch;

internal sealed record SelectiveQueryExpansionBranch(
    string Query,
    string QueryKind,
    string SearchMode,
    IReadOnlyList<string> Trace);

internal interface ISelectiveMultiQueryExpansionPolicy
{
    IReadOnlyList<SelectiveQueryExpansionBranch> BuildBranches(
        string baseQuery,
        SearchQueryIntentProfile intentProfile,
        SearchRankingQueryProfile queryProfile,
        SearchIterationBudget budget);
}

internal sealed class SelectiveMultiQueryExpansionPolicy : ISelectiveMultiQueryExpansionPolicy
{
    public SelectiveMultiQueryExpansionPolicy(ISearchQueryExpansionPolicy? queryExpansionPolicy = null)
    {
        _queryExpansionPolicy = queryExpansionPolicy ?? new SearchQueryExpansionPolicy();
    }

    private readonly ISearchQueryExpansionPolicy _queryExpansionPolicy;

    public IReadOnlyList<SelectiveQueryExpansionBranch> BuildBranches(
        string baseQuery,
        SearchQueryIntentProfile intentProfile,
        SearchRankingQueryProfile queryProfile,
        SearchIterationBudget budget)
    {
        var maxAdditionalBranches = Math.Max(0, budget.MaxIterations - 1);
        if (maxAdditionalBranches == 0)
        {
            return Array.Empty<SelectiveQueryExpansionBranch>();
        }

        var branches = new List<SelectiveQueryExpansionBranch>(maxAdditionalBranches);
        void AddBranch(SearchQueryExpansionDecision decision, string queryKind, string searchMode, string reason)
        {
            if (branches.Count >= maxAdditionalBranches || string.IsNullOrWhiteSpace(decision.Query))
            {
                return;
            }

            var trace = new List<string>(decision.Trace.Count + 1)
            {
                $"search_query.expansion branch={queryKind} reason={reason}"
            };
            trace.AddRange(decision.Trace);

            branches.Add(new SelectiveQueryExpansionBranch(
                decision.Query,
                queryKind,
                searchMode,
                trace));
        }

        if (intentProfile.FreshnessSensitive)
        {
            AddBranch(
                _queryExpansionPolicy.RewriteFreshness(baseQuery, intentProfile, queryProfile),
                "freshness",
                "freshness",
                "freshness_sensitive");
        }

        if (intentProfile.PaperAnalysisLike)
        {
            AddBranch(
                _queryExpansionPolicy.RewritePaperFocus(baseQuery, intentProfile, queryProfile),
                "paper_focus",
                "focused",
                "paper_analysis");
        }

        if (queryProfile.EvidenceHeavy || queryProfile.MedicalEvidenceHeavy)
        {
            AddBranch(
                _queryExpansionPolicy.RewriteEvidence(baseQuery, intentProfile, queryProfile),
                "evidence",
                "verification",
                "evidence_sensitive");
        }

        if (intentProfile.ContradictionSensitive || intentProfile.ComparisonSensitive)
        {
            AddBranch(
                _queryExpansionPolicy.RewriteContradiction(baseQuery, intentProfile, queryProfile),
                "contradiction",
                "verification",
                intentProfile.ContradictionSensitive ? "contradiction_sensitive" : "comparative_prompt");
        }

        if (intentProfile.BroadPromptLike || intentProfile.AmbiguousPromptLike)
        {
            AddBranch(
                _queryExpansionPolicy.RewriteStepBack(baseQuery, intentProfile, queryProfile),
                "step_back",
                "focused",
                intentProfile.BroadPromptLike ? "broad_prompt" : "ambiguous_prompt");
        }

        if (intentProfile.OfficialBias &&
            (intentProfile.BroadPromptLike || intentProfile.FreshnessSensitive || intentProfile.PaperAnalysisLike))
        {
            AddBranch(
                _queryExpansionPolicy.RewriteOfficial(baseQuery, intentProfile, queryProfile),
                "official",
                "verification",
                "official_bias");
        }

        if (branches.Count == 0 && CountTokens(baseQuery) >= 10)
        {
            AddBranch(
                _queryExpansionPolicy.RewriteNarrow(baseQuery, intentProfile, queryProfile),
                "narrow",
                "focused",
                "long_query_fallback");
        }

        return branches
            .DistinctBy(static branch => $"{branch.QueryKind}:{branch.Query}", StringComparer.OrdinalIgnoreCase)
            .Take(maxAdditionalBranches)
            .ToArray();
    }

    private static int CountTokens(string text)
    {
        return string.IsNullOrWhiteSpace(text)
            ? 0
            : text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;
    }
}

