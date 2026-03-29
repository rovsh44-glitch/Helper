using System.Collections.Concurrent;
using System.Globalization;

namespace Helper.Api.Hosting;

public sealed record WebResearchTelemetrySnapshot(
    DateTimeOffset GeneratedAtUtc,
    int Turns,
    int LiveWebTurns,
    int CachedWebTurns,
    int ForceSearchTurns,
    int NoWebOverrideTurns,
    double AvgQueriesPerTurn,
    double AvgFetchedPagesPerTurn,
    double AvgPassagesPerTurn,
    int TotalBlockedFetches,
    double BlockedFetchRate,
    int StaleDisclosureTurns,
    double StaleDisclosureRate,
    IReadOnlyList<string> Alerts);

public interface IWebResearchTelemetryService
{
    void RecordResponse(ChatResponseDto response);
    WebResearchTelemetrySnapshot GetSnapshot();
}

public sealed class WebResearchTelemetryService : IWebResearchTelemetryService
{
    private long _turns;
    private long _liveWebTurns;
    private long _cachedWebTurns;
    private long _forceSearchTurns;
    private long _noWebOverrideTurns;
    private long _queriesSum;
    private long _fetchedPagesSum;
    private long _passagesSum;
    private long _blockedFetchesTotal;
    private long _staleDisclosureTurns;
    private readonly ConcurrentDictionary<string, long> _statusCounts = new(StringComparer.OrdinalIgnoreCase);

    public void RecordResponse(ChatResponseDto response)
    {
        ArgumentNullException.ThrowIfNull(response);

        var trace = response.SearchTrace;
        if (!ShouldTrack(trace))
        {
            return;
        }

        Interlocked.Increment(ref _turns);

        var requestedMode = Normalize(trace!.RequestedMode);
        var status = Normalize(trace.Status);
        _statusCounts.AddOrUpdate(status, 1, static (_, current) => current + 1);

        if (string.Equals(requestedMode, "force_search", StringComparison.OrdinalIgnoreCase))
        {
            Interlocked.Increment(ref _forceSearchTurns);
        }
        else if (string.Equals(requestedMode, "no_web", StringComparison.OrdinalIgnoreCase))
        {
            Interlocked.Increment(ref _noWebOverrideTurns);
        }

        if (string.Equals(status, "executed_live_web", StringComparison.OrdinalIgnoreCase))
        {
            Interlocked.Increment(ref _liveWebTurns);
        }
        else if (string.Equals(status, "used_cached_web_result", StringComparison.OrdinalIgnoreCase))
        {
            Interlocked.Increment(ref _cachedWebTurns);
        }

        var queryCount = ResolveQueryCount(trace);
        var fetchedPages = ResolveFetchedPageCount(trace);
        var passages = ResolvePassageCount(trace);
        var blockedFetches = ResolveBlockedFetchCount(trace);
        var staleDisclosure = DetectStaleDisclosure(trace, response.UncertaintyFlags);

        Interlocked.Add(ref _queriesSum, queryCount);
        Interlocked.Add(ref _fetchedPagesSum, fetchedPages);
        Interlocked.Add(ref _passagesSum, passages);
        Interlocked.Add(ref _blockedFetchesTotal, blockedFetches);

        if (staleDisclosure)
        {
            Interlocked.Increment(ref _staleDisclosureTurns);
        }
    }

    public WebResearchTelemetrySnapshot GetSnapshot()
    {
        var turns = Volatile.Read(ref _turns);
        if (turns <= 0)
        {
            return new WebResearchTelemetrySnapshot(
                DateTimeOffset.UtcNow,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                Array.Empty<string>());
        }

        var blockedFetches = Volatile.Read(ref _blockedFetchesTotal);
        var staleDisclosureTurns = Volatile.Read(ref _staleDisclosureTurns);
        var snapshot = new WebResearchTelemetrySnapshot(
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            Turns: (int)turns,
            LiveWebTurns: (int)Volatile.Read(ref _liveWebTurns),
            CachedWebTurns: (int)Volatile.Read(ref _cachedWebTurns),
            ForceSearchTurns: (int)Volatile.Read(ref _forceSearchTurns),
            NoWebOverrideTurns: (int)Volatile.Read(ref _noWebOverrideTurns),
            AvgQueriesPerTurn: (double)Volatile.Read(ref _queriesSum) / turns,
            AvgFetchedPagesPerTurn: (double)Volatile.Read(ref _fetchedPagesSum) / turns,
            AvgPassagesPerTurn: (double)Volatile.Read(ref _passagesSum) / turns,
            TotalBlockedFetches: (int)blockedFetches,
            BlockedFetchRate: blockedFetches / (double)turns,
            StaleDisclosureTurns: (int)staleDisclosureTurns,
            StaleDisclosureRate: staleDisclosureTurns / (double)turns,
            Alerts: BuildAlerts(turns));

        return snapshot;
    }

    private IReadOnlyList<string> BuildAlerts(long turns)
    {
        var alerts = new List<string>();
        var blockedFetchRate = Volatile.Read(ref _blockedFetchesTotal) / (double)Math.Max(1, turns);
        var staleDisclosureRate = Volatile.Read(ref _staleDisclosureTurns) / (double)Math.Max(1, turns);
        var avgFetchedPages = Volatile.Read(ref _fetchedPagesSum) / (double)Math.Max(1, turns);

        if (turns >= 5 && avgFetchedPages < 0.50)
        {
            alerts.Add("Average fetched-page count is below 0.5 per tracked web turn; search path may still rely too heavily on snippets.");
        }

        if (turns >= 5 && blockedFetchRate > 0.40)
        {
            alerts.Add("Blocked fetch rate is above 40%; review fetch security policy, provider quality, or URL admission heuristics.");
        }

        if (turns >= 5 && staleDisclosureRate > 0.25)
        {
            alerts.Add("Stale disclosure rate is above 25%; freshness cache refresh policy needs review.");
        }

        return alerts;
    }

    private static bool ShouldTrack(SearchTraceDto? trace)
    {
        if (trace is null)
        {
            return false;
        }

        var status = Normalize(trace.Status);
        return !string.Equals(status, "not_needed", StringComparison.OrdinalIgnoreCase);
    }

    private static int ResolveQueryCount(SearchTraceDto trace)
    {
        var explicitCount = ExtractIntValue(trace.Events, "web_search.iteration_count=");
        if (explicitCount > 0)
        {
            return explicitCount;
        }

        var countedIterations = (trace.Events ?? Array.Empty<string>())
            .Count(static entry => entry.StartsWith("web_search.iteration[", StringComparison.OrdinalIgnoreCase));
        return countedIterations > 0 ? countedIterations : 1;
    }

    private static int ResolveFetchedPageCount(SearchTraceDto trace)
    {
        var explicitCount = ExtractIntValue(trace.Events, "web_page_fetch.extracted_count=");
        if (explicitCount >= 0)
        {
            return explicitCount;
        }

        return (trace.Sources ?? Array.Empty<SearchTraceSourceDto>())
            .Count(static source => source.PassageCount > 0);
    }

    private static int ResolvePassageCount(SearchTraceDto trace)
    {
        var explicitCount = ExtractIntValue(trace.Events, "web_page_fetch.passage_count=");
        if (explicitCount >= 0)
        {
            return explicitCount;
        }

        return (trace.Sources ?? Array.Empty<SearchTraceSourceDto>())
            .Sum(static source => Math.Max(0, source.PassageCount));
    }

    private static int ResolveBlockedFetchCount(SearchTraceDto trace)
    {
        return (trace.Events ?? Array.Empty<string>())
            .Count(static entry =>
                entry.Contains("web_fetch.blocked", StringComparison.OrdinalIgnoreCase) ||
                entry.Contains("redirect_blocked", StringComparison.OrdinalIgnoreCase) ||
                entry.Contains("content_type_blocked", StringComparison.OrdinalIgnoreCase));
    }

    private static bool DetectStaleDisclosure(SearchTraceDto trace, IReadOnlyList<string>? uncertaintyFlags)
    {
        if ((trace.Events ?? Array.Empty<string>()).Any(static entry =>
                entry.Contains("web_cache.state=aging", StringComparison.OrdinalIgnoreCase) ||
                entry.Contains("web_cache.state=stale", StringComparison.OrdinalIgnoreCase) ||
                entry.Contains("web_cache.refresh_failed=yes", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return (uncertaintyFlags ?? Array.Empty<string>()).Any(static flag =>
            flag.Contains("web_cache_", StringComparison.OrdinalIgnoreCase));
    }

    private static int ExtractIntValue(IReadOnlyList<string>? events, string prefix)
    {
        if (events is null)
        {
            return -1;
        }

        foreach (var entry in events)
        {
            if (string.IsNullOrWhiteSpace(entry) ||
                !entry.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var raw = entry[prefix.Length..].Trim();
            if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                return Math.Max(0, parsed);
            }
        }

        return -1;
    }

    private static string Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim();
    }
}

