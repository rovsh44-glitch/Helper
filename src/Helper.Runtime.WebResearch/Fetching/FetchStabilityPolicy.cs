using Helper.Runtime.WebResearch.Ranking;

namespace Helper.Runtime.WebResearch.Fetching;

internal sealed record FetchStabilityBudgetDecision(
    int SelectionLimit,
    int AttemptBudget,
    int SuccessTarget,
    bool BackfillEnabled,
    string Mode,
    IReadOnlyList<string> Trace);

internal interface IFetchStabilityPolicy
{
    FetchStabilityBudgetDecision Resolve(
        string? requestQuery,
        WebSearchPlan plan,
        int availableCandidates,
        int requestedMaxResults);
}

internal sealed class FetchStabilityPolicy : IFetchStabilityPolicy
{
    public FetchStabilityBudgetDecision Resolve(
        string? requestQuery,
        WebSearchPlan plan,
        int availableCandidates,
        int requestedMaxResults)
    {
        var boundedMaxResults = Math.Clamp(requestedMaxResults, 1, 10);
        if (availableCandidates <= 0)
        {
            return new FetchStabilityBudgetDecision(
                SelectionLimit: 0,
                AttemptBudget: 0,
                SuccessTarget: 0,
                BackfillEnabled: false,
                Mode: "empty",
                Trace: new[] { "web_page_fetch.policy mode=empty selection_limit=0 attempt_budget=0 success_target=0" });
        }

        var baseFetchBudget = Math.Min(WebPageFetchSettings.ReadMaxFetchesPerSearch(), availableCandidates);
        var selectionLimit = Math.Min(availableCandidates, boundedMaxResults);
        var attemptBudget = Math.Min(selectionLimit, baseFetchBudget);
        var successTarget = Math.Min(1, selectionLimit);
        var mode = "standard";
        var backfillEnabled = false;

        var query = string.IsNullOrWhiteSpace(requestQuery) ? plan.Query : requestQuery!;
        var queryProfile = SourceAuthorityScorer.BuildQueryProfile(query, plan.QueryKind);
        var domainProfile = DomainAuthorityProfileResolver.Resolve(query, plan);
        var rerankProfile = WebDocumentRerankerProfileResolver.Resolve(query, plan);
        var evidenceBackfillPreferred =
            (queryProfile.MedicalEvidenceHeavy && domainProfile.Name is "medical_evidence" or "medical_conflict") ||
            rerankProfile.Name is "medical_evidence" or "contrastive_review" or "paper_analysis";

        if (evidenceBackfillPreferred)
        {
            selectionLimit = Math.Min(availableCandidates, Math.Max(boundedMaxResults + 2, 5));
            attemptBudget = Math.Min(
                selectionLimit,
                Math.Max(baseFetchBudget, WebPageFetchSettings.ReadMaxFetchAttemptsPerSearch()));
            successTarget = Math.Min(2, selectionLimit);
            backfillEnabled = attemptBudget > baseFetchBudget || selectionLimit > boundedMaxResults;
            mode = domainProfile.Name switch
            {
                "medical_conflict" => "evidence_backfill_medical_conflict",
                "medical_evidence" => "evidence_backfill_medical",
                _ => "evidence_backfill"
            };
        }
        else if (domainProfile.Name == "current_events" &&
                 rerankProfile.Name == "factual_freshness")
        {
            selectionLimit = Math.Min(availableCandidates, Math.Max(boundedMaxResults, 3));
            attemptBudget = Math.Min(selectionLimit, Math.Max(baseFetchBudget, 3));
            successTarget = Math.Min(selectionLimit, attemptBudget);
            mode = "freshness_cluster_fetch";
        }
        else if ((domainProfile.Name == "law_regulation" || domainProfile.Name == "finance_market") &&
                 rerankProfile.Name == "factual_freshness")
        {
            selectionLimit = Math.Min(availableCandidates, Math.Max(boundedMaxResults + 2, 4));
            attemptBudget = Math.Min(selectionLimit, Math.Max(baseFetchBudget, 4));
            successTarget = Math.Min(2, attemptBudget);
            backfillEnabled = attemptBudget > baseFetchBudget || selectionLimit > boundedMaxResults;
            mode = "regulation_freshness_backfill";
        }

        return new FetchStabilityBudgetDecision(
            SelectionLimit: selectionLimit,
            AttemptBudget: attemptBudget,
            SuccessTarget: successTarget,
            BackfillEnabled: backfillEnabled,
            Mode: mode,
            Trace: new[]
            {
                $"web_page_fetch.policy mode={mode} selection_limit={selectionLimit} attempt_budget={attemptBudget} success_target={successTarget} domain_profile={domainProfile.Name} rerank_profile={rerankProfile.Name}"
            });
    }
}

