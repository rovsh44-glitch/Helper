using Helper.Api.Hosting;
using Helper.Runtime.Core;

namespace Helper.Api.Conversation;

internal interface IWebSearchTraceProjector
{
    SearchTraceDto? Build(ChatTurnContext context);
}

internal sealed class WebSearchTraceProjector : IWebSearchTraceProjector
{
    private readonly ICitationQuotePolicy _citationQuotePolicy;

    public WebSearchTraceProjector()
        : this(new CitationQuotePolicy())
    {
    }

    internal WebSearchTraceProjector(ICitationQuotePolicy citationQuotePolicy)
    {
        _citationQuotePolicy = citationQuotePolicy;
    }

    public SearchTraceDto? Build(ChatTurnContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var requestedMode = NormalizeMode(context.Request.LiveWebMode);
        var resolvedRequirement = NormalizeRequirement(context.ResolvedLiveWebRequirement);
        var hasLiveWebActivity =
            !string.Equals(requestedMode, "auto", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(resolvedRequirement, "no_web_needed", StringComparison.OrdinalIgnoreCase) ||
            context.ResearchEvidenceItems.Count > 0 ||
            context.RetrievalTrace.Count > 0 ||
            context.ToolCalls.Contains("research.search", StringComparer.OrdinalIgnoreCase);
        if (!hasLiveWebActivity)
        {
            return null;
        }

        var signals = context.LiveWebSignals
            .Where(static signal => !string.IsNullOrWhiteSpace(signal))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var events = BuildEvents(context, requestedMode, resolvedRequirement);
        var sources = BuildSources(context);

        return new SearchTraceDto(
            RequestedMode: requestedMode,
            ResolvedRequirement: resolvedRequirement,
            Reason: string.IsNullOrWhiteSpace(context.ResolvedLiveWebReason) ? null : context.ResolvedLiveWebReason,
            Status: ResolveStatus(context, requestedMode, resolvedRequirement),
            Signals: signals,
            Events: events,
            Sources: sources,
            InputMode: ConversationInputMode.Normalize(context.Request.InputMode));
    }

    private static string[] BuildEvents(ChatTurnContext context, string requestedMode, string resolvedRequirement)
    {
        var trace = new List<string>
        {
            $"input_mode:{ConversationInputMode.Normalize(context.Request.InputMode)}",
            $"requested_mode:{requestedMode}",
            $"resolved_requirement:{resolvedRequirement}"
        };

        if (!string.IsNullOrWhiteSpace(context.ResolvedLiveWebReason))
        {
            trace.Add($"decision_reason:{context.ResolvedLiveWebReason}");
        }

        if (context.ToolCalls.Contains("research.search", StringComparer.OrdinalIgnoreCase))
        {
            trace.Add(context.IntentSignals.Contains("web_search:cache_hit", StringComparer.OrdinalIgnoreCase)
                ? "web_execution:cache_hit"
                : "web_execution:live_fetch");
        }

        if (context.LiveWebSignals.Count > 0)
        {
            trace.Add($"signal_count:{context.LiveWebSignals.Count}");
        }

        foreach (var entry in context.RetrievalTrace)
        {
            if (string.IsNullOrWhiteSpace(entry))
            {
                continue;
            }

            trace.Add(entry.Trim());
        }

        var distinct = trace
            .Where(static entry => !string.IsNullOrWhiteSpace(entry))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var prioritized = distinct
            .Where(IsCriticalFetchVisibilityEvent)
            .ToList();
        prioritized.AddRange(distinct.Where(entry => !IsCriticalFetchVisibilityEvent(entry)));
        return prioritized
            .Take(64)
            .ToArray();
    }

    private SearchTraceSourceDto[] BuildSources(ChatTurnContext context)
    {
        if (context.ResearchEvidenceItems.Count > 0)
        {
            return context.ResearchEvidenceItems
                .OrderBy(static item => item.Ordinal)
                .Select(item =>
                {
                    var excerptDecision = _citationQuotePolicy.Build(
                        item.Snippet,
                        item.Url,
                        item.Title,
                        item.EvidenceKind,
                        CitationRenderSurface.Trace);
                    return new SearchTraceSourceDto(
                        Ordinal: item.Ordinal,
                        Title: string.IsNullOrWhiteSpace(item.Title) ? $"Source {item.Ordinal}" : item.Title.Trim(),
                        Url: string.IsNullOrWhiteSpace(item.Url) ? string.Empty : item.Url.Trim(),
                        PublishedAt: item.PublishedAt,
                        EvidenceKind: item.EvidenceKind,
                        TrustLevel: item.TrustLevel,
                        WasSanitized: item.WasSanitized,
                        SafetyFlags: MergeSafetyFlags(item.SafetyFlags, excerptDecision.Flags),
                        Snippet: excerptDecision.Included ? excerptDecision.Text : null,
                        PassageCount: item.Passages?.Count ?? 0);
                })
                .ToArray();
        }

        return context.Sources
            .Where(static source => !string.IsNullOrWhiteSpace(source))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(static (source, index) => new SearchTraceSourceDto(
                Ordinal: index + 1,
                Title: source.Trim(),
                Url: source.Trim(),
                Snippet: null))
            .ToArray();
    }

    private static string[]? MergeSafetyFlags(IReadOnlyList<string>? left, IReadOnlyList<string>? right)
    {
        var merged = (left ?? Array.Empty<string>())
            .Concat(right ?? Array.Empty<string>())
            .Where(static flag => !string.IsNullOrWhiteSpace(flag))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return merged.Length == 0 ? null : merged;
    }

    private static string ResolveStatus(ChatTurnContext context, string requestedMode, string resolvedRequirement)
    {
        if (string.Equals(requestedMode, "no_web", StringComparison.OrdinalIgnoreCase))
        {
            return "disabled_by_user";
        }

        if (context.ToolCalls.Contains("research.search", StringComparer.OrdinalIgnoreCase))
        {
            return context.IntentSignals.Contains("web_search:cache_hit", StringComparer.OrdinalIgnoreCase)
                ? "used_cached_web_result"
                : "executed_live_web";
        }

        if (!string.Equals(resolvedRequirement, "no_web_needed", StringComparison.OrdinalIgnoreCase))
        {
            return "web_considered_but_not_executed";
        }

        return "not_needed";
    }

    private static string NormalizeMode(string? mode)
    {
        if (string.Equals(mode, "force_search", StringComparison.OrdinalIgnoreCase))
        {
            return "force_search";
        }

        if (string.Equals(mode, "no_web", StringComparison.OrdinalIgnoreCase))
        {
            return "no_web";
        }

        return "auto";
    }

    private static string NormalizeRequirement(string? requirement)
    {
        if (string.Equals(requirement, "web_required", StringComparison.OrdinalIgnoreCase))
        {
            return "web_required";
        }

        if (string.Equals(requirement, "web_helpful", StringComparison.OrdinalIgnoreCase))
        {
            return "web_helpful";
        }

        return "no_web_needed";
    }

    private static bool IsCriticalFetchVisibilityEvent(string entry)
    {
        return entry.StartsWith("web_page_fetch.failure_count=", StringComparison.OrdinalIgnoreCase) ||
               entry.StartsWith("web_page_fetch.failure_outcomes=", StringComparison.OrdinalIgnoreCase) ||
               entry.StartsWith("web_page_fetch.transport_failure_count=", StringComparison.OrdinalIgnoreCase) ||
               entry.StartsWith("web_page_fetch.transport_categories=", StringComparison.OrdinalIgnoreCase) ||
               entry.StartsWith("web_page_fetch.transport_failed_hosts=", StringComparison.OrdinalIgnoreCase) ||
               entry.StartsWith("web_page_fetch.transport_recovery_count=", StringComparison.OrdinalIgnoreCase) ||
               entry.StartsWith("web_page_fetch.transport_recoveries=", StringComparison.OrdinalIgnoreCase) ||
               entry.StartsWith("web_page_fetch.render_recovery", StringComparison.OrdinalIgnoreCase) ||
               entry.StartsWith("web_page_fetch.failure host=", StringComparison.OrdinalIgnoreCase) ||
               entry.StartsWith("web_page_render.detected=", StringComparison.OrdinalIgnoreCase) ||
               entry.StartsWith("browser_render.", StringComparison.OrdinalIgnoreCase) ||
               entry.StartsWith("web_search.search_hit_only_guard", StringComparison.OrdinalIgnoreCase);
    }
}

