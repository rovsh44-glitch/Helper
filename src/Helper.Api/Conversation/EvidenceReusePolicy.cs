using System.Text.RegularExpressions;

namespace Helper.Api.Conversation;

internal sealed record EvidenceReuseDecision(
    bool PreviousSessionFound,
    bool ReusePreviousSession,
    string EffectiveQuery,
    string Reason,
    IReadOnlyList<string> Trace);

internal interface IEvidenceReusePolicy
{
    EvidenceReuseDecision Evaluate(ChatTurnContext context, SearchSessionState? previousSession);
}

internal sealed partial class EvidenceReusePolicy : IEvidenceReusePolicy
{
    private readonly ISelectiveEvidenceMemoryPolicy _evidenceMemoryPolicy;

    public EvidenceReusePolicy(ISelectiveEvidenceMemoryPolicy? evidenceMemoryPolicy = null)
    {
        _evidenceMemoryPolicy = evidenceMemoryPolicy ?? new SelectiveEvidenceMemoryPolicy();
    }

    public EvidenceReuseDecision Evaluate(ChatTurnContext context, SearchSessionState? previousSession)
    {
        ArgumentNullException.ThrowIfNull(context);

        var currentQuery = NormalizeWhitespace(context.Request.Message);
        if (previousSession is null)
        {
            return new EvidenceReuseDecision(
                PreviousSessionFound: false,
                ReusePreviousSession: false,
                EffectiveQuery: currentQuery,
                Reason: "no_previous_session",
                Trace: new[]
                {
                    "search_session.state_found=no",
                    "search_session.reuse=no reason=no_previous_session"
                });
        }

        var trace = new List<string>
        {
            "search_session.state_found=yes",
            $"search_session.previous_turn={previousSession.LastTurnId ?? "unknown"}",
            $"search_session.root_query={Summarize(previousSession.RootQuery)}",
            $"citation_lineage.available={previousSession.CitationLineage.Count}",
            $"search_session.continuation_depth={previousSession.ContinuationDepth}"
        };

        if (string.IsNullOrWhiteSpace(currentQuery))
        {
            trace.Add("search_session.reuse=no reason=empty_query");
            return new EvidenceReuseDecision(true, false, currentQuery, "empty_query", trace);
        }

        var currentTokens = Tokenize(currentQuery);
        var rootTokens = Tokenize(previousSession.RootQuery);
        var overlap = currentTokens.Intersect(rootTokens, StringComparer.OrdinalIgnoreCase).Count();
        var matchedLineage = FindMatchedLineage(previousSession, currentQuery).ToArray();
        var evidenceMemoryDecision = _evidenceMemoryPolicy.Select(currentQuery, previousSession);
        var isEllipticalFollowUp = DetectEllipticalFollowUp(currentQuery);

        trace.Add($"search_session.token_overlap={overlap}");
        trace.Add($"search_session.lineage_matches={matchedLineage.Length}");
        trace.AddRange(evidenceMemoryDecision.Trace);

        var shouldReuse =
            matchedLineage.Length > 0 ||
            evidenceMemoryDecision.SelectedEntries.Count > 0 ||
            (isEllipticalFollowUp && (previousSession.CitationLineage.Count > 0 || previousSession.EffectiveEvidenceMemory.Count > 0)) ||
            (currentTokens.Count <= 10 && overlap >= 2);

        if (!shouldReuse)
        {
            trace.Add($"search_session.reuse=no reason={(overlap == 0 ? "topic_shift" : "standalone_query")}");
            return new EvidenceReuseDecision(true, false, currentQuery, overlap == 0 ? "topic_shift" : "standalone_query", trace);
        }

        var reason = matchedLineage.Length > 0
            ? "citation_reference"
            : evidenceMemoryDecision.SelectedEntries.Count > 0
                ? "validated_evidence_memory"
            : isEllipticalFollowUp
                ? "elliptical_followup"
                : "topic_overlap";
        var effectiveQuery = BuildEffectiveQuery(previousSession, currentQuery, matchedLineage, evidenceMemoryDecision.SelectedEntries, overlap);
        trace.Add("search_session.reuse=yes");
        trace.Add($"search_session.reuse_reason={reason}");
        trace.Add($"search_session.effective_query={Summarize(effectiveQuery)}");

        return new EvidenceReuseDecision(true, true, effectiveQuery, reason, trace);
    }

    private static string BuildEffectiveQuery(
        SearchSessionState previousSession,
        string currentQuery,
        IReadOnlyList<CitationLineageEntry> matchedLineage,
        IReadOnlyList<SelectiveEvidenceMemoryEntry> selectedEvidenceMemory,
        int overlap)
    {
        if (overlap >= 3 && currentQuery.Length >= 48)
        {
            return currentQuery;
        }

        if (matchedLineage.Count > 0)
        {
            var sourceHints = matchedLineage
                .Take(2)
                .Select(static entry => string.IsNullOrWhiteSpace(entry.Title) ? entry.Url : entry.Title)
                .Distinct(StringComparer.OrdinalIgnoreCase);
            return $"{previousSession.RootQuery}. Follow-up: {currentQuery}. Prior relevant sources: {string.Join("; ", sourceHints)}";
        }

        if (selectedEvidenceMemory.Count > 0)
        {
            var validatedHints = selectedEvidenceMemory
                .Take(2)
                .Select(static entry =>
                {
                    var source = string.IsNullOrWhiteSpace(entry.Title) ? entry.Url : entry.Title;
                    return $"{source}: {TrimMemorySummary(entry.Summary)}";
                })
                .Distinct(StringComparer.OrdinalIgnoreCase);
            return $"{previousSession.RootQuery}. Follow-up: {currentQuery}. Previously validated context: {string.Join("; ", validatedHints)}";
        }

        return $"{previousSession.RootQuery}. Follow-up: {currentQuery}";
    }

    private static string TrimMemorySummary(string summary)
    {
        if (string.IsNullOrWhiteSpace(summary))
        {
            return "validated evidence";
        }

        return summary.Length <= 120 ? summary : summary[..120].TrimEnd() + "...";
    }

    private static IEnumerable<CitationLineageEntry> FindMatchedLineage(SearchSessionState previousSession, string currentQuery)
    {
        if (previousSession.CitationLineage.Count == 0)
        {
            return Array.Empty<CitationLineageEntry>();
        }

        var query = currentQuery.ToLowerInvariant();
        return previousSession.CitationLineage.Where(entry =>
        {
            if (!string.IsNullOrWhiteSpace(entry.Title) &&
                query.Contains(entry.Title, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(entry.Title) &&
                Tokenize(entry.Title)
                    .Where(static token => token.Length >= 5)
                    .Any(token => query.Contains(token, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            if (Uri.TryCreate(entry.Url, UriKind.Absolute, out var uri) &&
                query.Contains(uri.Host, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (Uri.TryCreate(entry.Url, UriKind.Absolute, out var sourceUri))
            {
                var hostLabel = sourceUri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
                    ? sourceUri.Host[4..]
                    : sourceUri.Host;
                if (hostLabel.Contains('.', StringComparison.Ordinal))
                {
                    hostLabel = hostLabel[..hostLabel.IndexOf('.', StringComparison.Ordinal)];
                }

                if (hostLabel.Length >= 4 &&
                    query.Contains(hostLabel, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            if (query.Contains($"[{entry.CitationLabel}]", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (int.TryParse(entry.CitationLabel, out var citationOrdinal))
            {
                return query.Contains($"source {citationOrdinal}", StringComparison.OrdinalIgnoreCase) ||
                       query.Contains($"источник {citationOrdinal}", StringComparison.OrdinalIgnoreCase);
            }

            return false;
        });
    }

    private static bool DetectEllipticalFollowUp(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return false;
        }

        return FollowUpLeadRegex().IsMatch(query) ||
               query.Contains("what about", StringComparison.OrdinalIgnoreCase) ||
               query.Contains("how about", StringComparison.OrdinalIgnoreCase) ||
               query.Contains("same for", StringComparison.OrdinalIgnoreCase) ||
               query.Contains("what changed", StringComparison.OrdinalIgnoreCase) ||
               query.Contains("сравни", StringComparison.OrdinalIgnoreCase) ||
               query.Contains("что насчет", StringComparison.OrdinalIgnoreCase) ||
               query.Contains("что насчёт", StringComparison.OrdinalIgnoreCase) ||
               query.Contains("а как", StringComparison.OrdinalIgnoreCase) ||
               query.Contains("источник", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> Tokenize(string value)
    {
        return SpaceRegex()
            .Replace(NonWordRegex().Replace(value.ToLowerInvariant(), " "), " ")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static string NormalizeWhitespace(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return SpaceRegex().Replace(value.Trim(), " ");
    }

    private static string Summarize(string value)
    {
        return value.Length <= 160 ? value : value[..160];
    }

    [GeneratedRegex("^(and|also|what about|how about|same for|then|continue|а|и|тогда|дальше|ещё|еще)\\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex FollowUpLeadRegex();

    [GeneratedRegex("[^\\p{L}\\p{Nd}]+", RegexOptions.Compiled)]
    private static partial Regex NonWordRegex();

    [GeneratedRegex("\\s+", RegexOptions.Compiled)]
    private static partial Regex SpaceRegex();
}

