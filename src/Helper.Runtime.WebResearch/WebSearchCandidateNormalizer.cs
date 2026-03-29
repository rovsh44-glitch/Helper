using Helper.Runtime.WebResearch.Normalization;
using Helper.Runtime.WebResearch.Quality;
using Helper.Runtime.WebResearch.Fetching;
using Helper.Runtime.WebResearch.Ranking;

namespace Helper.Runtime.WebResearch;

internal interface IWebSearchCandidateNormalizer
{
    IReadOnlyList<WebSearchDocument> Canonicalize(
        IReadOnlyList<WebSearchDocument> documents,
        string stage,
        List<string> trace);

    IReadOnlyList<WebSearchDocument> Normalize(
        WebSearchRequest request,
        WebSearchPlan plan,
        IReadOnlyList<WebSearchDocument> documents,
        int maxResults,
        bool allowFetchExpansion,
        List<string> trace);

    RankedWebDocumentCandidate Rank(WebSearchPlan plan, WebSearchDocument document);
}

internal sealed class WebSearchCandidateNormalizer : IWebSearchCandidateNormalizer
{
    private readonly ISourceAuthorityScorer _authorityScorer;
    private readonly ISpamAndSeoDemotionPolicy _spamDemotionPolicy;
    private readonly ICanonicalUrlResolver _canonicalUrlResolver;
    private readonly IDuplicateContentCollapsePolicy _duplicateCollapsePolicy;
    private readonly IWebDocumentQualityPolicy _documentQualityPolicy;
    private readonly IMedicalEvidenceFloorPolicy _medicalEvidenceFloorPolicy;
    private readonly IDomainAuthorityFloorPolicy _domainAuthorityFloorPolicy;
    private readonly IWebDocumentReranker _documentReranker;
    private readonly IFetchStabilityPolicy _fetchStabilityPolicy;

    public WebSearchCandidateNormalizer(
        ISourceAuthorityScorer authorityScorer,
        ISpamAndSeoDemotionPolicy spamDemotionPolicy,
        ICanonicalUrlResolver canonicalUrlResolver,
        IDuplicateContentCollapsePolicy duplicateCollapsePolicy,
        IWebDocumentQualityPolicy documentQualityPolicy,
        IMedicalEvidenceFloorPolicy medicalEvidenceFloorPolicy,
        IDomainAuthorityFloorPolicy domainAuthorityFloorPolicy,
        IWebDocumentReranker documentReranker,
        IFetchStabilityPolicy fetchStabilityPolicy)
    {
        _authorityScorer = authorityScorer;
        _spamDemotionPolicy = spamDemotionPolicy;
        _canonicalUrlResolver = canonicalUrlResolver;
        _duplicateCollapsePolicy = duplicateCollapsePolicy;
        _documentQualityPolicy = documentQualityPolicy;
        _medicalEvidenceFloorPolicy = medicalEvidenceFloorPolicy;
        _domainAuthorityFloorPolicy = domainAuthorityFloorPolicy;
        _documentReranker = documentReranker;
        _fetchStabilityPolicy = fetchStabilityPolicy;
    }

    public IReadOnlyList<WebSearchDocument> Normalize(
        WebSearchRequest request,
        WebSearchPlan plan,
        IReadOnlyList<WebSearchDocument> documents,
        int maxResults,
        bool allowFetchExpansion,
        List<string> trace)
    {
        if (documents.Count == 0)
        {
            return Array.Empty<WebSearchDocument>();
        }

        var ranked = documents
            .Where(document => WebSearchDocumentPipelineSupport.IsHttpUrl(document.Url))
            .Select(document => new WebSearchDocument(
                document.Url.Trim(),
                string.IsNullOrWhiteSpace(document.Title) ? "Untitled result" : document.Title.Trim(),
                document.Snippet?.Trim() ?? string.Empty,
                document.IsFallback,
                document.ExtractedPage))
            .ToArray();
        var canonicalized = Canonicalize(ranked, "provider", trace);
        canonicalized = ApplyQualityGate(canonicalized, "provider", request.Query, trace);
        var domainProfile = DomainAuthorityProfileResolver.Resolve(request.Query, plan);
        var rankedCandidates = canonicalized
            .Select(document => Rank(plan, document))
            .OrderByDescending(static candidate => candidate.FinalScore)
            .ThenByDescending(static candidate => candidate.Authority.Score)
            .ThenBy(static candidate => candidate.Document.Url, StringComparer.OrdinalIgnoreCase)
            .Select(static candidate => candidate.Document)
            .ToArray();
        var deduplicated = _duplicateCollapsePolicy.Collapse(rankedCandidates, "provider");
        trace.AddRange(deduplicated.Trace);
        var finalRanked = deduplicated.Documents
            .Select(document => Rank(plan, document))
            .OrderByDescending(static candidate => candidate.FinalScore)
            .ThenByDescending(static candidate => candidate.Authority.Score)
            .ThenBy(static candidate => candidate.Document.Url, StringComparer.OrdinalIgnoreCase)
            .ToList();
        finalRanked = _medicalEvidenceFloorPolicy.Apply(request.Query, plan, finalRanked, trace).ToList();
        if (ShouldDeferDomainAuthorityFloor(domainProfile, allowFetchExpansion))
        {
            trace.Add($"web_search.evidence_floor deferred=yes profile={domainProfile.Name} reason=post_fetch_current_events");
        }
        else
        {
            finalRanked = _domainAuthorityFloorPolicy.Apply(request.Query, plan, finalRanked, trace).ToList();
        }

        var selectionLimit = maxResults;
        if (allowFetchExpansion)
        {
            var fetchStability = _fetchStabilityPolicy.Resolve(request.Query, plan, finalRanked.Count, maxResults);
            trace.AddRange(fetchStability.Trace);
            selectionLimit = fetchStability.SelectionLimit;
        }

        var rerankLimit = Math.Min(finalRanked.Count, Math.Max(selectionLimit, 5));
        var reranked = _documentReranker.Rerank(request.Query, plan, finalRanked, rerankLimit);
        trace.AddRange(reranked.Trace);
        finalRanked = reranked.Candidates.ToList();

        for (var index = 0; index < Math.Min(finalRanked.Count, 5); index++)
        {
            var candidate = finalRanked[index];
            var host = Uri.TryCreate(candidate.Document.Url, UriKind.Absolute, out var uri)
                ? uri.Host
                : "unknown";
            trace.Add(
                $"web_search.ranking[{index + 1}] host={host} final={candidate.FinalScore:0.000} authority={candidate.Authority.Label}:{candidate.Authority.Score:0.000} spam_penalty={candidate.Spam.Penalty:0.000} low_trust={(candidate.Spam.LowTrust ? "yes" : "no")} rerank_profile={reranked.ProfileName} domain_profile={domainProfile.Name} query_kind={plan.QueryKind} query=\"{request.Query}\"");
        }

        return finalRanked
            .Select(static candidate => candidate.Document)
            .Take(selectionLimit)
            .ToArray();
    }

    public IReadOnlyList<WebSearchDocument> Canonicalize(
        IReadOnlyList<WebSearchDocument> documents,
        string stage,
        List<string> trace)
    {
        if (documents.Count == 0)
        {
            return Array.Empty<WebSearchDocument>();
        }

        var normalized = new List<WebSearchDocument>(documents.Count);
        foreach (var document in documents)
        {
            var resolution = _canonicalUrlResolver.Resolve(document);
            var normalizedDocument = ApplyCanonicalResolution(document, resolution);
            if (!string.Equals(document.Url, normalizedDocument.Url, StringComparison.OrdinalIgnoreCase) &&
                trace.Count < 32)
            {
                trace.Add(
                    $"web_normalization.canonical_url stage={stage} from={document.Url} to={normalizedDocument.Url} reasons={string.Join(",", resolution.Reasons)}");
            }

            normalized.Add(normalizedDocument);
        }

        return normalized;
    }

    public RankedWebDocumentCandidate Rank(WebSearchPlan plan, WebSearchDocument document)
    {
        var authority = _authorityScorer.Evaluate(plan, document);
        var spam = _spamDemotionPolicy.Evaluate(plan, document);
        var finalScore = Math.Clamp(authority.Score - spam.Penalty, 0d, 1d);
        return new RankedWebDocumentCandidate(document, authority, spam, finalScore);
    }

    private IReadOnlyList<WebSearchDocument> ApplyQualityGate(
        IReadOnlyList<WebSearchDocument> documents,
        string stage,
        string? query,
        List<string> trace)
    {
        if (documents.Count == 0)
        {
            return Array.Empty<WebSearchDocument>();
        }

        var admitted = new List<WebSearchDocument>(documents.Count);
        foreach (var document in documents)
        {
            var decision = _documentQualityPolicy.Evaluate(document, stage, query);
            trace.AddRange(decision.Trace);
            if (decision.Allowed)
            {
                admitted.Add(document);
            }
        }

        return admitted;
    }

    private static bool ShouldDeferDomainAuthorityFloor(DomainAuthorityProfile profile, bool allowFetchExpansion)
    {
        return allowFetchExpansion &&
               profile.Name.Equals("current_events", StringComparison.OrdinalIgnoreCase);
    }

    private static WebSearchDocument ApplyCanonicalResolution(WebSearchDocument document, CanonicalUrlResolution resolution)
    {
        if (document.ExtractedPage is null)
        {
            return document with { Url = resolution.CanonicalUrl };
        }

        return document with
        {
            Url = resolution.CanonicalUrl,
            ExtractedPage = document.ExtractedPage with
            {
                CanonicalUrl = resolution.CanonicalUrl
            }
        };
    }
}

