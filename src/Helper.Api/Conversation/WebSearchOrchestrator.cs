using Helper.Api.Backend.Application;
using Helper.Runtime.Core;
using Helper.Runtime.Infrastructure;

namespace Helper.Api.Conversation;

internal interface IWebSearchOrchestrator
{
    Task<ChatTurnImmediateOutcome> ExecuteAsync(ChatTurnContext context, CancellationToken ct);
}

internal sealed class WebSearchOrchestrator : IWebSearchOrchestrator
{
    private readonly IResearchService _researchService;
    private readonly IShortHorizonResearchCache _researchCache;
    private readonly ISourceNormalizationService _sourceNormalizer;
    private readonly IToolAuditService? _toolAudit;
    private readonly IFreshnessWindowPolicy _freshnessWindowPolicy;
    private readonly IEvidenceRefreshPolicy _evidenceRefreshPolicy;
    private readonly IStaleEvidenceDisclosurePolicy _staleEvidenceDisclosurePolicy;
    private readonly IEvidenceReusePolicy _evidenceReusePolicy;
    private readonly ICitationLineageStore _citationLineageStore;
    private readonly ISelectiveEvidenceMemoryStore _selectiveEvidenceMemoryStore;
    private readonly IUserProfileService _userProfileService;
    private readonly IConversationWebQueryPlanner _webQueryPlanner;
    private readonly ILocalBaselineAnswerService? _localBaselineAnswerService;

    public WebSearchOrchestrator(
        IResearchService researchService,
        IShortHorizonResearchCache researchCache,
        ISourceNormalizationService sourceNormalizer,
        IToolAuditService? toolAudit = null,
        IFreshnessWindowPolicy? freshnessWindowPolicy = null,
        IEvidenceRefreshPolicy? evidenceRefreshPolicy = null,
        IStaleEvidenceDisclosurePolicy? staleEvidenceDisclosurePolicy = null,
        IEvidenceReusePolicy? evidenceReusePolicy = null,
        ICitationLineageStore? citationLineageStore = null,
        ISelectiveEvidenceMemoryStore? selectiveEvidenceMemoryStore = null,
        IUserProfileService? userProfileService = null,
        IConversationWebQueryPlanner? webQueryPlanner = null,
        ILocalBaselineAnswerService? localBaselineAnswerService = null)
    {
        _researchService = researchService;
        _researchCache = researchCache;
        _sourceNormalizer = sourceNormalizer;
        _toolAudit = toolAudit;
        _freshnessWindowPolicy = freshnessWindowPolicy ?? new FreshnessWindowPolicy();
        _evidenceRefreshPolicy = evidenceRefreshPolicy ?? new EvidenceRefreshPolicy();
        _staleEvidenceDisclosurePolicy = staleEvidenceDisclosurePolicy ?? new StaleEvidenceDisclosurePolicy();
        _evidenceReusePolicy = evidenceReusePolicy ?? new EvidenceReusePolicy();
        _citationLineageStore = citationLineageStore ?? new CitationLineageStore();
        _selectiveEvidenceMemoryStore = selectiveEvidenceMemoryStore ?? new SelectiveEvidenceMemoryStore();
        _userProfileService = userProfileService ?? new UserProfileService();
        _webQueryPlanner = webQueryPlanner ?? new WebQueryPlanner();
        _localBaselineAnswerService = localBaselineAnswerService;
    }

    public async Task<ChatTurnImmediateOutcome> ExecuteAsync(ChatTurnContext context, CancellationToken ct)
    {
        if (context.ToolCallsUsed >= context.ToolCallBudget)
        {
            context.BudgetExceeded = true;
            context.ExecutionOutput = ChatTurnExecutionSupport.BuildToolBudgetExceededMessage(context);
            return SingleMessage(context.ExecutionOutput);
        }

        var branchId = SearchSessionStateAccessor.ResolveBranchId(context.Conversation, context.Request.BranchId);
        var previousSession = SearchSessionStateAccessor.Get(context.Conversation, branchId);
        var reuseDecision = _evidenceReusePolicy.Evaluate(context, previousSession);
        context.RetrievalTrace.AddRange(reuseDecision.Trace);
        if (reuseDecision.ReusePreviousSession)
        {
            context.IntentSignals.Add("web_search:session_reuse");
        }

        if (ShouldUseLocalBaselineOnly(context))
        {
            return await ExecuteLocalBaselineOnlyAsync(context, branchId, reuseDecision, ct).ConfigureAwait(false);
        }

        context.ToolCallsUsed++;
        context.ToolCalls.Add("research.search");
        context.IntentSignals.Add("web_search:orchestrated");
        var profile = _userProfileService.Resolve(context.Conversation);
        var queryPlan = _webQueryPlanner.Build(context, profile, reuseDecision.EffectiveQuery);
        context.RetrievalTrace.AddRange(queryPlan.Trace);
        context.RetrievalTrace.Add($"search_session.input_mode={ConversationInputMode.Normalize(context.Request.InputMode)}");
        var effectiveQuery = queryPlan.Query;
        var researchCallAt = DateTimeOffset.UtcNow;
        var usedCachedSnapshot = TryGetCachedSnapshot(
            context.Request.Message,
            reuseDecision.EffectiveQuery,
            effectiveQuery,
            out var cachedSnapshot,
            out var cacheLookupKind);
        if (usedCachedSnapshot)
        {
            context.RetrievalTrace.Add($"search_session.cache_lookup={cacheLookupKind}");
        }

        var freshness = usedCachedSnapshot
            ? _freshnessWindowPolicy.Assess(context.Request.Message, cachedSnapshot.StoredAtUtc, cachedSnapshot.CategoryHint ?? context.ResolvedLiveWebReason)
            : null;
        var refreshDecision = freshness is null
            ? null
            : _evidenceRefreshPolicy.Evaluate(context, freshness);

        if (usedCachedSnapshot &&
            freshness is not null &&
            refreshDecision is not null &&
            refreshDecision.Action != WebEvidenceRefreshAction.RefreshBeforeUse)
        {
            ApplyResearchResult(context, cachedSnapshot.Result, cacheHit: true);
            UpdateSearchSessionState(context, branchId, reuseDecision, effectiveQuery, cachedSnapshot.Result);
            _staleEvidenceDisclosurePolicy.Apply(
                context,
                cachedSnapshot,
                freshness,
                refreshDecision,
                refreshAttempted: false,
                refreshFailed: false);
            _toolAudit?.Record(ChatTurnExecutionSupport.BuildAuditEntry(
                context,
                researchCallAt,
                "research.search",
                "CHAT_EXECUTE",
                success: true,
                details: $"cache_hit:{freshness.State.ToString().ToLowerInvariant()}"));
            return SingleMessage(context.ExecutionOutput);
        }

        ResearchResult research;
        using (var stageCts = ChatTurnExecutionSupport.CreateStageCancellation("research", context, ct))
        {
            try
            {
                research = await _researchService.ResearchAsync(effectiveQuery, 1, null, stageCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stageCts.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                if (usedCachedSnapshot &&
                    freshness is not null &&
                    refreshDecision is not null &&
                    refreshDecision.UseCachedFallbackOnFailure)
                {
                    ApplyResearchResult(context, cachedSnapshot.Result, cacheHit: true);
                    UpdateSearchSessionState(context, branchId, reuseDecision, effectiveQuery, cachedSnapshot.Result);
                    _staleEvidenceDisclosurePolicy.Apply(
                        context,
                        cachedSnapshot,
                        freshness,
                        refreshDecision,
                        refreshAttempted: true,
                        refreshFailed: true,
                        refreshFailureReason: "stage_timeout");
                    _toolAudit?.Record(ChatTurnExecutionSupport.BuildAuditEntry(
                        context,
                        researchCallAt,
                        "research.search",
                        "CHAT_EXECUTE",
                        success: true,
                        details: $"refresh_timeout_fallback:{freshness.State.ToString().ToLowerInvariant()}"));
                    return SingleMessage(context.ExecutionOutput);
                }

                ChatTurnExecutionSupport.ApplyStageTimeoutFallback(context, "research");
                _toolAudit?.Record(ChatTurnExecutionSupport.BuildAuditEntry(
                    context,
                    researchCallAt,
                    "research.search",
                    "CHAT_EXECUTE",
                    success: false,
                    error: "Stage timeout",
                    details: ChatTurnExecutionSupport.BuildStageTimeoutDetails(context, "research")));
                return SingleMessage(context.ExecutionOutput);
            }
            catch (Exception ex) when (usedCachedSnapshot &&
                                       freshness is not null &&
                                       refreshDecision is not null &&
                                       refreshDecision.UseCachedFallbackOnFailure)
            {
                ApplyResearchResult(context, cachedSnapshot.Result, cacheHit: true);
                UpdateSearchSessionState(context, branchId, reuseDecision, effectiveQuery, cachedSnapshot.Result);
                _staleEvidenceDisclosurePolicy.Apply(
                    context,
                    cachedSnapshot,
                    freshness,
                    refreshDecision,
                    refreshAttempted: true,
                    refreshFailed: true,
                    refreshFailureReason: ex.Message);
                _toolAudit?.Record(ChatTurnExecutionSupport.BuildAuditEntry(
                    context,
                    researchCallAt,
                    "research.search",
                    "CHAT_EXECUTE",
                    success: true,
                    details: $"refresh_failed_fallback:{freshness.State.ToString().ToLowerInvariant()}"));
                return SingleMessage(context.ExecutionOutput);
            }
        }

        _researchCache.Set(context.Request.Message, research, context.ResolvedLiveWebReason);
        if (!string.Equals(reuseDecision.EffectiveQuery, context.Request.Message, StringComparison.OrdinalIgnoreCase))
        {
            _researchCache.Set(reuseDecision.EffectiveQuery, research, context.ResolvedLiveWebReason);
        }
        if (!string.Equals(effectiveQuery, context.Request.Message, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(effectiveQuery, reuseDecision.EffectiveQuery, StringComparison.OrdinalIgnoreCase))
        {
            _researchCache.Set(effectiveQuery, research, context.ResolvedLiveWebReason);
        }

        context.RetrievalTrace.Add($"web_cache.write category={(context.ResolvedLiveWebReason ?? "general")}");
        context.RetrievalTrace.Add("web_cache.write_state=fresh");
        ApplyResearchResult(context, research, cacheHit: false);
        UpdateSearchSessionState(context, branchId, reuseDecision, effectiveQuery, research);
        _toolAudit?.Record(ChatTurnExecutionSupport.BuildAuditEntry(
            context,
            researchCallAt,
            "research.search",
            "CHAT_EXECUTE",
            success: true,
            details: "live_fetch"));
        return SingleMessage(context.ExecutionOutput);
    }

    private async Task<ChatTurnImmediateOutcome> ExecuteLocalBaselineOnlyAsync(
        ChatTurnContext context,
        string branchId,
        EvidenceReuseDecision reuseDecision,
        CancellationToken ct)
    {
        if (_localBaselineAnswerService is null)
        {
            context.RetrievalTrace.Add("benchmark.local_first.local_only service=missing fallback=research_search");
            context.LocalFirstBenchmarkMode = LocalFirstBenchmarkMode.None;
            return await ExecuteAsync(context, ct).ConfigureAwait(false);
        }

        context.ToolCallsUsed++;
        context.ToolCalls.Add("research.local_baseline");
        context.IntentSignals.Add("benchmark:local_first_local_only");
        context.RetrievalTrace.Add("benchmark.local_first.mode=local_only");
        context.RetrievalTrace.Add("web_search.skipped reason=benchmark_local_only");
        var baselineCallAt = DateTimeOffset.UtcNow;

        LocalBaselineAnswerResult localBaseline;
        using (var stageCts = ChatTurnExecutionSupport.CreateStageCancellation("research", context, ct))
        {
            try
            {
                localBaseline = await LocalBaselineAnswerServiceSupport.GenerateDetailedAsync(
                    _localBaselineAnswerService,
                    context.Request.Message,
                    stageCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stageCts.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                ChatTurnExecutionSupport.ApplyStageTimeoutFallback(context, "research");
                _toolAudit?.Record(ChatTurnExecutionSupport.BuildAuditEntry(
                    context,
                    baselineCallAt,
                    "research.local_baseline",
                    "CHAT_EXECUTE",
                    success: false,
                    error: "Stage timeout",
                    details: ChatTurnExecutionSupport.BuildStageTimeoutDetails(context, "research")));
                return SingleMessage(context.ExecutionOutput);
            }
        }

        var research = new ResearchResult(
            reuseDecision.EffectiveQuery,
            "Local baseline",
            localBaseline.Sources.ToList(),
            new List<string>(),
            localBaseline.Answer,
            DateTime.UtcNow,
            EvidenceItems: localBaseline.EvidenceItems,
            SearchTrace: new[]
            {
                "benchmark.local_first.answer_source=local_baseline",
                "web_search.outcome=local_only",
                "web_search.stop_reason=benchmark_local_only"
            }.Concat(localBaseline.Trace).ToArray());

        ApplyResearchResult(context, research, cacheHit: false, successSignal: "benchmark:local_first_local_only");
        UpdateSearchSessionState(context, branchId, reuseDecision, reuseDecision.EffectiveQuery, research);
        _toolAudit?.Record(ChatTurnExecutionSupport.BuildAuditEntry(
            context,
            baselineCallAt,
            "research.local_baseline",
            "CHAT_EXECUTE",
            success: true,
            details: "benchmark_local_only"));
        return SingleMessage(context.ExecutionOutput);
    }

    private void ApplyResearchResult(ChatTurnContext context, ResearchResult research, bool cacheHit, string? successSignal = null)
    {
        context.ExecutionOutput = research.FullReport;
        context.ResearchEvidenceItems.Clear();
        if (research.EvidenceItems is { Count: > 0 })
        {
            context.ResearchEvidenceItems.AddRange(research.EvidenceItems);
        }
        if (research.SearchTrace is { Count: > 0 })
        {
            context.RetrievalTrace.AddRange(research.SearchTrace);
        }

        var normalized = _sourceNormalizer.Normalize(research.Sources);
        context.Sources.Clear();
        context.Sources.AddRange(normalized.Sources.Select(static source => source.Source));
        context.UncertaintyFlags.AddRange(normalized.Alerts);
        context.IntentSignals.Add(cacheHit ? "web_search:cache_hit" : successSignal ?? "web_search:live_fetch");
    }

    private bool TryGetCachedSnapshot(
        string rawQuery,
        string reuseEffectiveQuery,
        string plannedQuery,
        out CachedWebEvidenceSnapshot snapshot,
        out string cacheLookupKind)
    {
        if (_researchCache.TryGetSnapshot(rawQuery, out snapshot))
        {
            cacheLookupKind = "raw_query";
            return true;
        }

        if (!string.Equals(rawQuery, reuseEffectiveQuery, StringComparison.OrdinalIgnoreCase) &&
            _researchCache.TryGetSnapshot(reuseEffectiveQuery, out snapshot))
        {
            cacheLookupKind = "effective_query";
            return true;
        }

        if (!string.Equals(plannedQuery, rawQuery, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(plannedQuery, reuseEffectiveQuery, StringComparison.OrdinalIgnoreCase) &&
            _researchCache.TryGetSnapshot(plannedQuery, out snapshot))
        {
            cacheLookupKind = "planned_query";
            return true;
        }

        snapshot = default!;
        cacheLookupKind = "none";
        return false;
    }

    private void UpdateSearchSessionState(
        ChatTurnContext context,
        string branchId,
        EvidenceReuseDecision reuseDecision,
        string plannedQuery,
        ResearchResult research)
    {
        var previousSession = reuseDecision.ReusePreviousSession
            ? SearchSessionStateAccessor.Get(context.Conversation, branchId)
            : null;
        var lineageUpdate = _citationLineageStore.Capture(previousSession, context.TurnId, research.Sources, research.EvidenceItems);
        var evidenceMemoryUpdate = _selectiveEvidenceMemoryStore.Capture(previousSession, context.TurnId, research.EvidenceItems);
        context.RetrievalTrace.AddRange(lineageUpdate.Trace);
        context.RetrievalTrace.AddRange(evidenceMemoryUpdate.Trace);

        SearchSessionStateAccessor.Set(
            context.Conversation,
            new SearchSessionState(
                BranchId: branchId,
                RootQuery: ResolveRootQuery(previousSession, reuseDecision, context, plannedQuery),
                LastUserQuery: context.Request.Message.Trim(),
                LastEffectiveQuery: plannedQuery,
                LastTurnId: context.TurnId,
                UpdatedAtUtc: DateTimeOffset.UtcNow,
                CategoryHint: context.ResolvedLiveWebReason,
                SourceUrls: research.Sources
                    .Where(static source => !string.IsNullOrWhiteSpace(source))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                CitationLineage: lineageUpdate.Entries,
                EvidenceMemory: evidenceMemoryUpdate.Entries,
                ContinuationDepth: previousSession is not null && reuseDecision.ReusePreviousSession
                    ? previousSession.ContinuationDepth + 1
                    : 0,
                LastReuseReason: reuseDecision.ReusePreviousSession ? reuseDecision.Reason : null,
                LastInputMode: ConversationInputMode.Normalize(context.Request.InputMode)));

        context.RetrievalTrace.Add($"search_session.saved branch={branchId}");
    }

    private static string ResolveRootQuery(
        SearchSessionState? previousSession,
        EvidenceReuseDecision reuseDecision,
        ChatTurnContext context,
        string plannedQuery)
    {
        if (previousSession is not null && reuseDecision.ReusePreviousSession)
        {
            return previousSession.RootQuery;
        }

        var rawQuery = context.Request.Message.Trim();
        return ConversationInputMode.IsVoice(context.Request.InputMode) &&
               !string.IsNullOrWhiteSpace(plannedQuery)
            ? plannedQuery
            : rawQuery;
    }

    private static ChatTurnImmediateOutcome SingleMessage(string content)
    {
        return new ChatTurnImmediateOutcome(new[] { new ChatTurnMessage(ChatStreamChunkType.Token, content) });
    }

    private static bool ShouldUseLocalBaselineOnly(ChatTurnContext context)
    {
        return context.IsLocalFirstBenchmarkTurn &&
               context.LocalFirstBenchmarkMode == LocalFirstBenchmarkMode.LocalOnly &&
               string.Equals(context.ResolvedLiveWebRequirement, "no_web_needed", StringComparison.OrdinalIgnoreCase);
    }
}

