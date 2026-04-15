namespace Helper.Runtime.WebResearch;

public sealed class WebSearchSessionCoordinator : IWebSearchSessionCoordinator
{
    private readonly IWebSearchProviderClient _providerClient;
    private readonly IWebQueryPlanner _queryPlanner;
    private readonly ISearchIterationPolicy _iterationPolicy;
    private readonly ISearchEvidenceSufficiencyPolicy _evidenceSufficiencyPolicy;
    private readonly IWebSearchDocumentPipeline _documentPipeline;
    private readonly IAuthoritativeSourceFamilyPolicy _authoritativeSourceFamilyPolicy;

    internal WebSearchSessionCoordinator(
        IWebSearchProviderClient providerClient,
        IWebQueryPlanner queryPlanner,
        ISearchIterationPolicy iterationPolicy,
        ISearchEvidenceSufficiencyPolicy evidenceSufficiencyPolicy,
        IWebSearchDocumentPipeline documentPipeline,
        IAuthoritativeSourceFamilyPolicy? authoritativeSourceFamilyPolicy = null)
    {
        _providerClient = providerClient;
        _queryPlanner = queryPlanner;
        _iterationPolicy = iterationPolicy;
        _evidenceSufficiencyPolicy = evidenceSufficiencyPolicy;
        _documentPipeline = documentPipeline;
        _authoritativeSourceFamilyPolicy = authoritativeSourceFamilyPolicy ?? new AuthoritativeSourceFamilyPolicy();
    }

    public async Task<WebSearchSession> ExecuteAsync(WebSearchRequest request, CancellationToken ct = default)
    {
        var startedAt = DateTimeOffset.UtcNow;
        string? failureReason = null;
        var budget = _iterationPolicy.Resolve(request);
        var plans = _queryPlanner.BuildPlans(request, budget);
        var executedPlans = new List<WebSearchPlan>();
        var iterationTrace = new List<WebSearchIterationTrace>();
        var providerTrace = new List<string>();
        var aggregateDocuments = new List<WebSearchDocument>();
        var stopReason = "iteration_budget_exhausted";
        var pageTrace = new List<string>();
        var allowFetchExpansion = _documentPipeline.CanExpandFetchSelection;

        if (plans.Count == 0)
        {
            stopReason = "empty_query";
        }

        foreach (var plan in plans.Take(budget.MaxIterations))
        {
            if (plan.RewriteTrace is { Count: > 0 })
            {
                providerTrace.AddRange(plan.RewriteTrace.Select(trace => $"{trace} query_kind={plan.QueryKind}"));
            }

            IReadOnlyList<WebSearchDocument> documents;
            try
            {
                var response = await _providerClient.SearchAsync(plan, ct).ConfigureAwait(false);
                documents = response.Documents;
                providerTrace.AddRange(response.Trace.Select(trace => $"{trace} query_kind={plan.QueryKind}"));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                documents = Array.Empty<WebSearchDocument>();
                failureReason ??= ex.Message;
            }

            var authoritativeFamily = _authoritativeSourceFamilyPolicy.Augment(request, plan, documents);
            documents = authoritativeFamily.Documents;
            if (authoritativeFamily.Trace.Count > 0)
            {
                providerTrace.AddRange(authoritativeFamily.Trace.Select(trace => $"{trace} query_kind={plan.QueryKind}"));
            }

            var rankingTrace = new List<string>();
            var normalizedIterationDocuments = _documentPipeline.Normalize(request, plan, documents, plan.MaxResults, allowFetchExpansion, rankingTrace);
            aggregateDocuments = MergeDocuments(aggregateDocuments, normalizedIterationDocuments, ResolveAggregateLimit(request, budget));
            providerTrace.AddRange(rankingTrace);
            executedPlans.Add(plan);

            var sufficiency = _evidenceSufficiencyPolicy.Evaluate(request, executedPlans, aggregateDocuments);
            iterationTrace.Add(new WebSearchIterationTrace(
                executedPlans.Count,
                plan.Query,
                plan.QueryKind,
                normalizedIterationDocuments.Count,
                aggregateDocuments.Count,
                sufficiency.DistinctDomainCount,
                sufficiency.IsSufficient,
                normalizedIterationDocuments.Count > 0 ? "results" : "empty"));

            if (sufficiency.IsSufficient)
            {
                stopReason = $"sufficient:{sufficiency.Reason}";
                break;
            }
        }

        if (aggregateDocuments.Count > 0)
        {
            var aggregateSelectionTrace = new List<string>
            {
                "web_search.final_selection stage=aggregate"
            };
            var aggregateSelectionPlan = executedPlans.LastOrDefault() ?? BuildDefaultPlan(request);
            aggregateDocuments = _documentPipeline.Normalize(
                    request,
                    aggregateSelectionPlan,
                    aggregateDocuments,
                    ResolveAggregateLimit(request, budget),
                    allowFetchExpansion,
                    aggregateSelectionTrace)
                .ToList();
            providerTrace.AddRange(aggregateSelectionTrace);
        }

        var usedDeterministicFallback = false;
        var outcome = executedPlans.Count > 1 ? "iterative_live_results" : "live_results";
        if (aggregateDocuments.Count == 0)
        {
            if ((plans.LastOrDefault()?.AllowDeterministicFallback ?? request.AllowDeterministicFallback))
            {
                aggregateDocuments = WebSearchFallbackBuilder.BuildFromQuery(request.Query).ToList();
                usedDeterministicFallback = aggregateDocuments.Count > 0;
                outcome = usedDeterministicFallback ? "deterministic_fallback" : "empty";
                stopReason = usedDeterministicFallback ? "fallback:user_provided_url" : stopReason;
            }
            else
            {
                outcome = "empty";
            }
        }
        else if (iterationTrace.Count > 0 && iterationTrace[^1].SufficientAfterIteration)
        {
            outcome = executedPlans.Count > 1 ? "iterative_live_results" : "live_results";
        }
        else if (executedPlans.Count > 1)
        {
            outcome = "partial_live_results";
        }

        if (aggregateDocuments.Count > 0)
        {
            var fetchPlan = executedPlans.LastOrDefault() ?? BuildDefaultPlan(request);
            var postFetch = await _documentPipeline.EnrichAndFinalizeAsync(request, fetchPlan, aggregateDocuments, usedDeterministicFallback, ct).ConfigureAwait(false);
            aggregateDocuments = postFetch.Documents.ToList();
            pageTrace.AddRange(postFetch.Trace);
            if (postFetch.UpgradedToDirectPageFetch)
            {
                outcome = "direct_page_fetch";
            }
        }

        var sourceUrls = aggregateDocuments
            .Select(static document => document.ExtractedPage?.CanonicalUrl ?? document.Url)
            .Where(IsHttpUrl)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var terminalPlan = executedPlans.LastOrDefault() ?? BuildDefaultPlan(request);

        return new WebSearchSession(
            request,
            terminalPlan,
            new WebSearchResultBundle(
                terminalPlan.Query,
                aggregateDocuments,
                sourceUrls,
                usedDeterministicFallback,
                outcome,
                failureReason,
                iterationTrace,
                stopReason,
                providerTrace,
                pageTrace),
            startedAt,
            DateTimeOffset.UtcNow);
    }

    private static WebSearchPlan BuildDefaultPlan(WebSearchRequest request)
    {
        var query = (request.Query ?? string.Empty).Trim();
        var maxResults = Math.Clamp(request.MaxResults, 1, 10);
        var depth = Math.Clamp(request.Depth, 1, 3);
        var purpose = string.IsNullOrWhiteSpace(request.Purpose) ? "research" : request.Purpose.Trim().ToLowerInvariant();
        var searchMode = depth > 1 ? "expanded" : "standard";

        return new WebSearchPlan(query, maxResults, depth, purpose, searchMode, request.AllowDeterministicFallback);
    }

    private static List<WebSearchDocument> MergeDocuments(
        IReadOnlyList<WebSearchDocument> aggregateDocuments,
        IReadOnlyList<WebSearchDocument> iterationDocuments,
        int aggregateLimit)
    {
        return aggregateDocuments
            .Concat(iterationDocuments)
            .Where(document => IsHttpUrl(document.Url))
            .DistinctBy(static document => document.Url, StringComparer.OrdinalIgnoreCase)
            .Take(aggregateLimit)
            .ToList();
    }

    private static int ResolveAggregateLimit(WebSearchRequest request, SearchIterationBudget budget)
    {
        return Math.Clamp(request.MaxResults + ((budget.MaxIterations - 1) * 2), request.MaxResults, 10);
    }

    private static bool IsHttpUrl(string? candidate)
    {
        return Uri.TryCreate(candidate, UriKind.Absolute, out var uri) &&
               (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase));
    }
}

