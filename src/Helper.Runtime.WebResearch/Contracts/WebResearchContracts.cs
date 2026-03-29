namespace Helper.Runtime.WebResearch;

public sealed record WebSearchRequest(
    string Query,
    int Depth = 1,
    int MaxResults = 5,
    string Purpose = "research",
    bool AllowDeterministicFallback = true);

public sealed record WebSearchPlan(
    string Query,
    int MaxResults,
    int Depth,
    string Purpose,
    string SearchMode,
    bool AllowDeterministicFallback,
    string QueryKind = "primary",
    IReadOnlyList<string>? RewriteTrace = null);

public sealed record WebSearchDocument(
    string Url,
    string Title,
    string Snippet,
    bool IsFallback = false,
    ExtractedWebPage? ExtractedPage = null);

public sealed record ExtractedWebPassage(
    int Ordinal,
    string Text,
    string TrustLevel = "untrusted_web_content",
    bool WasSanitized = false,
    IReadOnlyList<string>? SafetyFlags = null);

public sealed record ExtractedWebPage(
    string RequestedUrl,
    string ResolvedUrl,
    string CanonicalUrl,
    string Title,
    string? PublishedAt,
    string Body,
    IReadOnlyList<ExtractedWebPassage> Passages,
    string ContentType,
    string TrustLevel = "untrusted_web_content",
    bool WasSanitized = false,
    bool InjectionSignalsDetected = false,
    IReadOnlyList<string>? SafetyFlags = null);

public sealed record WebSearchResultBundle(
    string Query,
    IReadOnlyList<WebSearchDocument> Documents,
    IReadOnlyList<string> SourceUrls,
    bool UsedDeterministicFallback,
    string Outcome,
    string? FailureReason = null,
    IReadOnlyList<WebSearchIterationTrace>? Iterations = null,
    string? StopReason = null,
    IReadOnlyList<string>? ProviderTrace = null,
    IReadOnlyList<string>? PageTrace = null);

public sealed record WebSearchIterationTrace(
    int Ordinal,
    string Query,
    string QueryKind,
    int ResultCount,
    int AggregateResultCount,
    int DistinctDomainCount,
    bool SufficientAfterIteration,
    string Outcome);

public sealed record WebSearchSession(
    WebSearchRequest Request,
    WebSearchPlan Plan,
    WebSearchResultBundle ResultBundle,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt);

public sealed record SearchIterationBudget(
    int MaxIterations,
    string Reason);

public sealed record WebEvidenceSufficiencyDecision(
    bool IsSufficient,
    string Reason,
    int DistinctDomainCount,
    int LiveResultCount);

public sealed record WebSearchProviderClientResponse(
    IReadOnlyList<WebSearchDocument> Documents,
    IReadOnlyList<string> Trace);

public sealed record WebPageFetchResult(
    string RequestedUrl,
    string? ResolvedUrl,
    bool Success,
    string Outcome,
    ExtractedWebPage? ExtractedPage,
    IReadOnlyList<string> Trace,
    bool UsedBrowserRenderFallback = false,
    WebPageFetchDiagnostics? Diagnostics = null);

public sealed record WebPageFetchDiagnostics(
    int AttemptCount = 0,
    int RetryCount = 0,
    bool TransportFailureObserved = false,
    bool TransportRecovered = false,
    string? RecoveryProfile = null,
    string? FinalFailureCategory = null,
    string? FinalFailureProfile = null,
    string? FinalFailureReason = null,
    IReadOnlyList<string>? AttemptProfiles = null);

public sealed record ContentTypeAdmissionDecision(
    bool Allowed,
    string ReasonCode,
    IReadOnlyList<string> Trace);

public interface IWebSearchProviderClient
{
    Task<WebSearchProviderClientResponse> SearchAsync(WebSearchPlan plan, CancellationToken ct = default);
}

public interface IWebSearchSessionCoordinator
{
    Task<WebSearchSession> ExecuteAsync(WebSearchRequest request, CancellationToken ct = default);
}

public interface IWebQueryPlanner
{
    IReadOnlyList<WebSearchPlan> BuildPlans(WebSearchRequest request, SearchIterationBudget budget);
}

public interface ISearchIterationPolicy
{
    SearchIterationBudget Resolve(WebSearchRequest request);
}

public interface ISearchEvidenceSufficiencyPolicy
{
    WebEvidenceSufficiencyDecision Evaluate(
        WebSearchRequest request,
        IReadOnlyList<WebSearchPlan> executedPlans,
        IReadOnlyList<WebSearchDocument> aggregateDocuments);
}

