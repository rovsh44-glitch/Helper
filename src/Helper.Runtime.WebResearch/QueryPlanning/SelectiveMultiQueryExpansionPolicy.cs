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
        var requestProfile = ResearchRequestProfileResolver.From(baseQuery);
        var regulationFreshnessBranching =
            queryProfile.RegulationFreshnessHeavy &&
            (intentProfile.OfficialBias || requestProfile.StrictLiveEvidenceRequired || intentProfile.FreshnessSensitive);
        var strictOfficialBranching =
            requestProfile.StrictLiveEvidenceRequired;
        var climateScientificBranching = LooksLikeClimateSensitivityQuery(baseQuery);
        var arxivPolicyBranching = LooksLikeArxivPublisherPolicyQuery(baseQuery);
        var retractionStatusBranching = LooksLikeRetractionStatusQuery(baseQuery);
        var preferOfficialBeforeContradiction =
            intentProfile.OfficialBias &&
            !queryProfile.MedicalEvidenceHeavy &&
            (intentProfile.BroadPromptLike || intentProfile.FreshnessSensitive || intentProfile.PaperAnalysisLike);

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

        if (regulationFreshnessBranching || strictOfficialBranching || retractionStatusBranching)
        {
            AddBranch(
                _queryExpansionPolicy.RewriteOfficial(baseQuery, intentProfile, queryProfile),
                "official",
                "verification",
                retractionStatusBranching ? "retraction_status_registry" :
                regulationFreshnessBranching ? "regulation_official_bias" :
                "strict_live_evidence");
        }
        else if (arxivPolicyBranching)
        {
            AddBranch(
                _queryExpansionPolicy.RewritePublisherPolicy(baseQuery, intentProfile, queryProfile),
                "publisher_policy",
                "verification",
                "publisher_policy_registry");
        }

        if ((intentProfile.FreshnessSensitive || regulationFreshnessBranching || strictOfficialBranching || retractionStatusBranching) && !arxivPolicyBranching)
        {
            AddBranch(
                _queryExpansionPolicy.RewriteFreshness(baseQuery, intentProfile, queryProfile),
                "freshness",
                "freshness",
                retractionStatusBranching ? "retraction_status_freshness" : "freshness_sensitive");
        }

        if (intentProfile.PaperAnalysisLike || retractionStatusBranching)
        {
            AddBranch(
                _queryExpansionPolicy.RewritePaperFocus(baseQuery, intentProfile, queryProfile),
                "paper_focus",
                "focused",
                retractionStatusBranching ? "retraction_status_paper_focus" : "paper_analysis");
        }

        if (queryProfile.EvidenceHeavy || queryProfile.MedicalEvidenceHeavy)
        {
            AddBranch(
                _queryExpansionPolicy.RewriteEvidence(baseQuery, intentProfile, queryProfile),
                "evidence",
                "verification",
                "evidence_sensitive");
        }
        else if (climateScientificBranching)
        {
            AddBranch(
                _queryExpansionPolicy.RewriteEvidence(baseQuery, intentProfile, queryProfile),
                "evidence",
                "verification",
                "scientific_conflict_reconciliation");
        }

        if (!regulationFreshnessBranching && !strictOfficialBranching && preferOfficialBeforeContradiction)
        {
            AddBranch(
                _queryExpansionPolicy.RewriteOfficial(baseQuery, intentProfile, queryProfile),
                "official",
                "verification",
                "official_bias");
        }

        if (intentProfile.ContradictionSensitive || intentProfile.ComparisonSensitive)
        {
            AddBranch(
                _queryExpansionPolicy.RewriteContradiction(baseQuery, intentProfile, queryProfile),
                "contradiction",
                "verification",
                intentProfile.ContradictionSensitive ? "contradiction_sensitive" : "comparative_prompt");
        }

        if (!regulationFreshnessBranching &&
            !strictOfficialBranching &&
            !preferOfficialBeforeContradiction &&
            intentProfile.OfficialBias &&
            (intentProfile.BroadPromptLike || intentProfile.FreshnessSensitive || intentProfile.PaperAnalysisLike))
        {
            AddBranch(
                _queryExpansionPolicy.RewriteOfficial(baseQuery, intentProfile, queryProfile),
                "official",
                "verification",
                "official_bias");
        }

        if (intentProfile.BroadPromptLike || intentProfile.AmbiguousPromptLike)
        {
            AddBranch(
                _queryExpansionPolicy.RewriteStepBack(baseQuery, intentProfile, queryProfile),
                "step_back",
                "focused",
                intentProfile.BroadPromptLike ? "broad_prompt" : "ambiguous_prompt");
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

    private static bool LooksLikeClimateSensitivityQuery(string text)
    {
        return text.Contains("climate sensitivity", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("equilibrium climate sensitivity", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("transient climate response", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeArxivPublisherPolicyQuery(string text)
    {
        return text.Contains("arxiv", StringComparison.OrdinalIgnoreCase) &&
               (text.Contains("publisher", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("journal", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("repository", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("sherpa", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("romeo", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("open access", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("self-archiving", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("accepted manuscript", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("embargo", StringComparison.OrdinalIgnoreCase));
    }

    private static bool LooksLikeRetractionStatusQuery(string text)
    {
        return text.Contains("retraction", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("retracted", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("withdrawn", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("erratum", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("correction", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("expression of concern", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("crossmark", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("отозван", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("ретракц", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("исправлен", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("оспор", StringComparison.OrdinalIgnoreCase);
    }
}
