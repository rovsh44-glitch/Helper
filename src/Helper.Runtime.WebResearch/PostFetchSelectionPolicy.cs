using Helper.Runtime.WebResearch.Fetching;
using Helper.Runtime.WebResearch.Ranking;

namespace Helper.Runtime.WebResearch;

internal interface IPostFetchSelectionPolicy
{
    IReadOnlyList<WebSearchDocument> SelectFinalDocuments(
        WebSearchRequest request,
        WebSearchPlan plan,
        IReadOnlyList<WebSearchDocument> documents,
        List<string> trace);
}

internal sealed class PostFetchSelectionPolicy : IPostFetchSelectionPolicy
{
    private readonly IWebPageFetcher _pageFetcher;
    private readonly IWebSearchCandidateNormalizer _candidateNormalizer;
    private readonly IDomainAuthorityFloorPolicy _domainAuthorityFloorPolicy;

    public PostFetchSelectionPolicy(
        IWebPageFetcher pageFetcher,
        IWebSearchCandidateNormalizer candidateNormalizer,
        IDomainAuthorityFloorPolicy domainAuthorityFloorPolicy)
    {
        _pageFetcher = pageFetcher;
        _candidateNormalizer = candidateNormalizer;
        _domainAuthorityFloorPolicy = domainAuthorityFloorPolicy;
    }

    public IReadOnlyList<WebSearchDocument> SelectFinalDocuments(
        WebSearchRequest request,
        WebSearchPlan plan,
        IReadOnlyList<WebSearchDocument> documents,
        List<string> trace)
    {
        if (documents.Count == 0)
        {
            return Array.Empty<WebSearchDocument>();
        }

        var ranked = documents
            .Select(document => new PostFetchSelectionCandidate(
                Document: document,
                Ranking: _candidateNormalizer.Rank(plan, document),
                HasPassageEvidence: document.ExtractedPage is { Passages.Count: > 0 },
                PassageCount: document.ExtractedPage?.Passages.Count ?? 0))
            .ToArray();

        if (!ranked.Any(static candidate => candidate.HasPassageEvidence))
        {
            ranked = ApplySearchHitOnlyGuard(request, plan, ranked, trace);
        }

        ranked = ApplyDeferredCurrentEventsFloorAfterFetch(request, plan, ranked, trace);

        var selected = ranked
            .OrderByDescending(static candidate => candidate.HasPassageEvidence)
            .ThenByDescending(static candidate => candidate.PassageCount)
            .ThenByDescending(static candidate => candidate.Ranking.FinalScore)
            .ThenByDescending(static candidate => candidate.Ranking.Authority.Score)
            .ThenBy(static candidate => candidate.Document.Url, StringComparer.OrdinalIgnoreCase)
            .Take(request.MaxResults)
            .ToArray();

        for (var index = 0; index < selected.Length; index++)
        {
            var candidate = selected[index];
            trace.Add(
                $"web_search.final_after_fetch[{index + 1}] fetched={(candidate.HasPassageEvidence ? "yes" : "no")} passages={candidate.PassageCount} authority={candidate.Ranking.Authority.Label}:{candidate.Ranking.Authority.Score:0.000} final={candidate.Ranking.FinalScore:0.000} url={candidate.Document.Url}");
        }

        return selected.Select(static candidate => candidate.Document).ToArray();
    }

    private PostFetchSelectionCandidate[] ApplyDeferredCurrentEventsFloorAfterFetch(
        WebSearchRequest request,
        WebSearchPlan plan,
        IReadOnlyList<PostFetchSelectionCandidate> candidates,
        List<string> trace)
    {
        if (ReferenceEquals(_pageFetcher, NoopWebPageFetcher.Instance) || candidates.Count == 0)
        {
            return candidates.ToArray();
        }

        var domainProfile = DomainAuthorityProfileResolver.Resolve(request.Query, plan);
        if (!ShouldDeferDomainAuthorityFloor(domainProfile, allowFetchExpansion: true))
        {
            return candidates.ToArray();
        }

        var retainedRankings = _domainAuthorityFloorPolicy
            .Apply(request.Query, plan, candidates.Select(static candidate => candidate.Ranking).ToArray(), trace)
            .ToArray();
        if (retainedRankings.Length == candidates.Count)
        {
            return candidates.ToArray();
        }

        var retainedUrls = retainedRankings
            .Select(static candidate => candidate.Document.Url)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return candidates
            .Where(candidate => retainedUrls.Contains(candidate.Document.Url))
            .ToArray();
    }

    private static bool ShouldDeferDomainAuthorityFloor(DomainAuthorityProfile profile, bool allowFetchExpansion)
    {
        return allowFetchExpansion &&
               profile.Name.Equals("current_events", StringComparison.OrdinalIgnoreCase);
    }

    private PostFetchSelectionCandidate[] ApplySearchHitOnlyGuard(
        WebSearchRequest request,
        WebSearchPlan plan,
        IReadOnlyList<PostFetchSelectionCandidate> candidates,
        List<string> trace)
    {
        var queryProfile = SourceAuthorityScorer.BuildQueryProfile(request.Query, plan.QueryKind);
        var intentProfile = SearchQueryIntentProfileClassifier.Classify(request.Query, queryProfile);
        var requestProfile = ResearchRequestProfileResolver.From(request.Query);
        var guardSensitive =
            queryProfile.EvidenceHeavy ||
            queryProfile.MedicalEvidenceHeavy ||
            queryProfile.RegulationFreshnessHeavy ||
            LooksLikeArxivPublisherPolicyQuery(request.Query) ||
            intentProfile.ComparisonSensitive ||
            intentProfile.FreshnessSensitive ||
            intentProfile.BroadPromptLike ||
            intentProfile.AmbiguousPromptLike ||
            intentProfile.PaperAnalysisLike ||
            queryProfile.OfficialBias ||
            requestProfile.StrictLiveEvidenceRequired;
        if (!guardSensitive)
        {
            return candidates.ToArray();
        }

        var kept = new List<PostFetchSelectionCandidate>(candidates.Count);
        var dropped = new List<(PostFetchSelectionCandidate Candidate, string Reason)>();
        foreach (var candidate in candidates)
        {
            if (TryResolveSearchHitOnlyDropReason(queryProfile, intentProfile, candidate, out var reason))
            {
                dropped.Add((candidate, reason));
                continue;
            }

            kept.Add(candidate);
        }

        trace.Add($"web_search.search_hit_only_guard applied=yes candidates={candidates.Count} kept={kept.Count} dropped={dropped.Count}");
        foreach (var droppedCandidate in dropped.Take(4))
        {
            trace.Add($"web_search.search_hit_only_guard.drop url={droppedCandidate.Candidate.Document.Url} reason={droppedCandidate.Reason} final={droppedCandidate.Candidate.Ranking.FinalScore:0.000}");
        }

        if (kept.Count == 0)
        {
            var strictEmptyFallback =
                queryProfile.MedicalEvidenceHeavy ||
                queryProfile.RegulationFreshnessHeavy ||
                LooksLikeArxivPublisherPolicyQuery(request.Query) ||
                requestProfile.StrictLiveEvidenceRequired;
            if (strictEmptyFallback)
            {
                trace.Add("web_search.search_hit_only_guard fallback=empty_strict");
                return Array.Empty<PostFetchSelectionCandidate>();
            }

            return candidates.ToArray();
        }

        return kept.ToArray();
    }

    private static bool TryResolveSearchHitOnlyDropReason(
        SearchRankingQueryProfile queryProfile,
        SearchQueryIntentProfile intentProfile,
        PostFetchSelectionCandidate candidate,
        out string reason)
    {
        if (candidate.Ranking.Spam.LowTrust)
        {
            reason = "low_trust_without_fetch";
            return true;
        }

        var spamReasons = candidate.Ranking.Spam.Reasons;
        if (spamReasons.Contains("chat_or_app_landing_for_evidence_query", StringComparer.OrdinalIgnoreCase))
        {
            reason = "chat_or_app_landing_for_evidence_query";
            return true;
        }

        if (spamReasons.Contains("interactive_or_entertainment_page", StringComparer.OrdinalIgnoreCase) ||
            spamReasons.Contains("ugc_or_marketplace_page", StringComparer.OrdinalIgnoreCase) ||
            spamReasons.Contains("commercial_product_page_for_evidence_query", StringComparer.OrdinalIgnoreCase))
        {
            reason = "non_evidence_page_type";
            return true;
        }

        var authorityReasons = candidate.Ranking.Authority.Reasons;
        var hasStrongEvidenceSignal =
            candidate.Ranking.Authority.IsAuthoritative ||
            authorityReasons.Contains("medical_evidence_match", StringComparer.OrdinalIgnoreCase) ||
            authorityReasons.Contains("primary_medical_research_match", StringComparer.OrdinalIgnoreCase) ||
            authorityReasons.Contains("reference_document_match", StringComparer.OrdinalIgnoreCase) ||
            authorityReasons.Contains("official_bias_match", StringComparer.OrdinalIgnoreCase) ||
            authorityReasons.Contains("evidence_source_match", StringComparer.OrdinalIgnoreCase) ||
            candidate.Ranking.Authority.Label is "major_medical_reference" or
                "medical_research_index" or
                "medical_research_fulltext" or
                "evidence_review" or
                "clinical_reference" or
                "clinical_reference_society" or
                "academic_index" or
                "academic_publisher_article";

        if (queryProfile.MedicalEvidenceHeavy && !hasStrongEvidenceSignal)
        {
            reason = "insufficient_medical_authority_without_fetch";
            return true;
        }

        if (LooksLikeRetractionEvidenceSource(candidate.Document))
        {
            reason = string.Empty;
            return false;
        }

        var nonMedicalEvidenceSensitive =
            !queryProfile.MedicalEvidenceHeavy &&
            (queryProfile.EvidenceHeavy ||
             queryProfile.RegulationFreshnessHeavy ||
             intentProfile.ComparisonSensitive ||
             intentProfile.FreshnessSensitive ||
             intentProfile.BroadPromptLike ||
             intentProfile.PaperAnalysisLike);
        if (nonMedicalEvidenceSensitive &&
            !hasStrongEvidenceSignal &&
            candidate.Ranking.FinalScore < 0.50d)
        {
            reason = "insufficient_evidence_authority_without_fetch";
            return true;
        }

        reason = string.Empty;
        return false;
    }

    private static bool LooksLikeRetractionEvidenceSource(WebSearchDocument document)
    {
        var descriptor = $"{document.Url} {document.Title} {document.Snippet}";
        return descriptor.Contains("retraction", StringComparison.OrdinalIgnoreCase) ||
               descriptor.Contains("retrak", StringComparison.OrdinalIgnoreCase) ||
               descriptor.Contains("ретрак", StringComparison.OrdinalIgnoreCase) ||
               descriptor.Contains("ethics", StringComparison.OrdinalIgnoreCase) ||
               descriptor.Contains("crossmark", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeArxivPublisherPolicyQuery(string? text)
    {
        var value = text ?? string.Empty;
        return value.Contains("arxiv", StringComparison.OrdinalIgnoreCase) &&
               (value.Contains("publisher", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("repository", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("journal", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("sherpa", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("romeo", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("open access", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("self-archiving", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("accepted manuscript", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("embargo", StringComparison.OrdinalIgnoreCase));
    }

    private sealed record PostFetchSelectionCandidate(
        WebSearchDocument Document,
        RankedWebDocumentCandidate Ranking,
        bool HasPassageEvidence,
        int PassageCount);
}

