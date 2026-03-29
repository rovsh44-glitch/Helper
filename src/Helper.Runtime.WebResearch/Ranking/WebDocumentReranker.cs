namespace Helper.Runtime.WebResearch.Ranking;

internal sealed record WebDocumentRerankResult(
    string ProfileName,
    IReadOnlyList<RankedWebDocumentCandidate> Candidates,
    IReadOnlyList<string> Trace);

internal interface IWebDocumentReranker
{
    WebDocumentRerankResult Rerank(
        string? requestQuery,
        WebSearchPlan plan,
        IReadOnlyList<RankedWebDocumentCandidate> rankedDocuments,
        int limit);
}

internal sealed class WebDocumentReranker : IWebDocumentReranker
{
    public WebDocumentRerankResult Rerank(
        string? requestQuery,
        WebSearchPlan plan,
        IReadOnlyList<RankedWebDocumentCandidate> rankedDocuments,
        int limit)
    {
        if (rankedDocuments.Count == 0 || limit <= 0)
        {
            return new WebDocumentRerankResult("none", Array.Empty<RankedWebDocumentCandidate>(), Array.Empty<string>());
        }

        var profile = WebDocumentRerankerProfileResolver.Resolve(requestQuery, plan);
        var trace = new List<string>
        {
            $"web_search.rerank profile={profile.Name} domain_profile={profile.DomainAuthorityProfileName} candidates={rankedDocuments.Count} limit={limit} query_kind={plan.QueryKind}"
        };

        if (rankedDocuments.Count == 1)
        {
            trace.Add("web_search.rerank skipped=single_candidate");
            return new WebDocumentRerankResult(profile.Name, rankedDocuments, trace);
        }

        var scored = rankedDocuments
            .Select((candidate, index) => WebDocumentRerankingCandidateScorer.Score(profile, requestQuery, candidate, index))
            .OrderByDescending(static candidate => candidate.Score)
            .ThenByDescending(static candidate => candidate.Candidate.FinalScore)
            .ThenBy(static candidate => candidate.OriginalOrder)
            .ToList();

        var selected = WebDocumentRerankingSelectionPolicy.SelectTop(scored, limit, profile);
        for (var index = 0; index < Math.Min(selected.Count, 5); index++)
        {
            var item = selected[index];
            var host = Uri.TryCreate(item.Candidate.Document.Url, UriKind.Absolute, out var uri)
                ? uri.Host
                : "unknown";
            trace.Add(
                $"web_search.rerank[{index + 1}] host={host} profile={profile.Name} domain_profile={profile.DomainAuthorityProfileName} score={item.Score:0.000} base={item.Candidate.FinalScore:0.000} semantic={item.SemanticFit:0.000} authority_lift={item.AuthorityLift:0.000} domain_lift={item.DomainAuthorityLift:0.000} source_lift={item.SourceTypeLift:0.000} fresh_lift={item.FreshnessLift:0.000} evidence_lift={item.EvidenceLift:0.000} penalty={item.Penalty:0.000} source_kind={item.SourceKind}");
        }

        trace.Add(
            $"web_search.rerank_selection profile={profile.Name} selected={selected.Count} distinct_hosts={selected.Select(static item => GetHost(item.Candidate.Document.Url)).Distinct(StringComparer.OrdinalIgnoreCase).Count()}");

        return new WebDocumentRerankResult(
            profile.Name,
            selected.Select(static item => item.Candidate).ToArray(),
            trace);
    }

    private static string GetHost(string? url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri)
            ? uri.Host
            : "unknown";
    }
}

