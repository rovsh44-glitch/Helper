using Helper.Runtime.WebResearch.Fetching;

namespace Helper.Runtime.WebResearch;

internal interface IWebSearchFetchDiagnosticsSummarizer
{
    WebSearchFetchDiagnosticEntry CreateEntry(WebSearchDocument document, WebPageFetchResult fetchResult);
    IReadOnlyList<string> Summarize(IReadOnlyList<WebSearchFetchDiagnosticEntry> entries);
}

internal sealed record WebSearchFetchDiagnosticEntry(
    string Host,
    string Url,
    string Outcome,
    bool Success,
    bool TransportFailureObserved,
    bool TransportRecovered,
    string? FailureCategory,
    string? FailureProfile,
    string? RecoveryProfile,
    IReadOnlyList<string> AttemptProfiles);

internal sealed class WebSearchFetchDiagnosticsSummarizer : IWebSearchFetchDiagnosticsSummarizer
{
    public WebSearchFetchDiagnosticEntry CreateEntry(WebSearchDocument document, WebPageFetchResult fetchResult)
    {
        var host = Uri.TryCreate(document.Url, UriKind.Absolute, out var uri)
            ? uri.Host
            : "unknown";
        var diagnostics = fetchResult.Diagnostics;
        return new WebSearchFetchDiagnosticEntry(
            Host: host,
            Url: document.Url,
            Outcome: fetchResult.Outcome,
            Success: fetchResult.Success && fetchResult.ExtractedPage is not null,
            TransportFailureObserved: diagnostics?.TransportFailureObserved == true,
            TransportRecovered: diagnostics?.TransportRecovered == true,
            FailureCategory: diagnostics?.FinalFailureCategory,
            FailureProfile: diagnostics?.FinalFailureProfile,
            RecoveryProfile: diagnostics?.RecoveryProfile,
            AttemptProfiles: diagnostics?.AttemptProfiles ?? Array.Empty<string>());
    }

    public IReadOnlyList<string> Summarize(IReadOnlyList<WebSearchFetchDiagnosticEntry> entries)
    {
        if (entries.Count == 0)
        {
            return Array.Empty<string>();
        }

        var trace = new List<string>();
        var failed = entries.Where(static entry => !entry.Success).ToArray();
        trace.Add($"web_page_fetch.failure_count={failed.Length}");
        if (failed.Length > 0)
        {
            var outcomeSummary = string.Join(
                ",",
                failed.GroupBy(static entry => entry.Outcome, StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(static group => group.Count())
                    .Select(static group => $"{group.Key}:{group.Count()}"));
            trace.Add($"web_page_fetch.failure_outcomes={outcomeSummary}");

            foreach (var entry in failed.Take(3))
            {
                trace.Add(
                    $"web_page_fetch.failure host={entry.Host} outcome={entry.Outcome} transport={(entry.TransportFailureObserved ? "yes" : "no")} category={entry.FailureCategory ?? "none"} profile={entry.FailureProfile ?? "none"}");
            }
        }

        var transportFailures = entries.Where(static entry => entry.TransportFailureObserved).ToArray();
        trace.Add($"web_page_fetch.transport_failure_count={transportFailures.Length}");
        if (transportFailures.Length > 0)
        {
            var categorySummary = string.Join(
                ",",
                transportFailures
                    .GroupBy(static entry => entry.FailureCategory ?? "unknown", StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(static group => group.Count())
                    .Select(static group => $"{group.Key}:{group.Count()}"));
            trace.Add($"web_page_fetch.transport_categories={categorySummary}");
            trace.Add($"web_page_fetch.transport_failed_hosts={string.Join(",", transportFailures.Select(static entry => entry.Host).Distinct(StringComparer.OrdinalIgnoreCase).Take(4))}");
        }

        var recovered = entries.Where(static entry => entry.TransportRecovered).ToArray();
        trace.Add($"web_page_fetch.transport_recovery_count={recovered.Length}");
        if (recovered.Length > 0)
        {
            var recoverySummary = string.Join(
                ",",
                recovered
                    .GroupBy(static entry => entry.RecoveryProfile ?? "unknown", StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(static group => group.Count())
                    .Select(static group => $"{group.Key}:{group.Count()}"));
            trace.Add($"web_page_fetch.transport_recoveries={recoverySummary}");
        }

        return trace;
    }
}

