namespace Helper.Api.Conversation;

internal sealed record SelectiveEvidenceMemoryDecision(
    IReadOnlyList<SelectiveEvidenceMemoryEntry> SelectedEntries,
    string Reason,
    IReadOnlyList<string> Trace);

internal interface ISelectiveEvidenceMemoryPolicy
{
    SelectiveEvidenceMemoryDecision Select(string currentQuery, SearchSessionState? previousSession);
}

internal sealed class SelectiveEvidenceMemoryPolicy : ISelectiveEvidenceMemoryPolicy
{
    public SelectiveEvidenceMemoryDecision Select(string currentQuery, SearchSessionState? previousSession)
    {
        if (previousSession is null || previousSession.EffectiveEvidenceMemory.Count == 0)
        {
            return new SelectiveEvidenceMemoryDecision(
                Array.Empty<SelectiveEvidenceMemoryEntry>(),
                "no_evidence_memory",
                new[] { "evidence_memory.available=0", "evidence_memory.selected=0" });
        }

        var query = NormalizeWhitespace(currentQuery);
        var queryTokens = Tokenize(query);
        var trace = new List<string>
        {
            $"evidence_memory.available={previousSession.EffectiveEvidenceMemory.Count}"
        };
        if (queryTokens.Count == 0)
        {
            trace.Add("evidence_memory.selected=0");
            trace.Add("evidence_memory.reason=empty_query");
            return new SelectiveEvidenceMemoryDecision(Array.Empty<SelectiveEvidenceMemoryEntry>(), "empty_query", trace);
        }

        var scored = previousSession.EffectiveEvidenceMemory
            .Select(entry => (Entry: entry, Score: Score(entry, query, queryTokens)))
            .Where(static tuple => tuple.Score > 0)
            .OrderByDescending(static tuple => tuple.Score)
            .ThenByDescending(static tuple => tuple.Entry.SeenCount)
            .ThenBy(static tuple => tuple.Entry.Url, StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToArray();

        trace.Add($"evidence_memory.selected={scored.Length}");
        if (scored.Length > 0)
        {
            foreach (var match in scored)
            {
                trace.Add(
                    $"evidence_memory.match score={match.Score} kind={match.Entry.EvidenceKind} source={ConversationEvidenceIdentitySupport.Summarize(match.Entry.Url)}");
            }

            return new SelectiveEvidenceMemoryDecision(
                scored.Select(static tuple => tuple.Entry).ToArray(),
                "matched_validated_evidence",
                trace);
        }

        trace.Add("evidence_memory.reason=no_match");
        return new SelectiveEvidenceMemoryDecision(Array.Empty<SelectiveEvidenceMemoryEntry>(), "no_match", trace);
    }

    private static int Score(SelectiveEvidenceMemoryEntry entry, string query, IReadOnlyList<string> queryTokens)
    {
        var normalizedTitle = ConversationEvidenceIdentitySupport.NormalizeText(entry.Title);
        var normalizedSummary = ConversationEvidenceIdentitySupport.NormalizeText(entry.Summary);
        var normalizedUrl = ConversationEvidenceIdentitySupport.NormalizeUrl(entry.Url);
        var score = 0;
        var overlap = queryTokens.Count(token =>
            token.Length >= 4 &&
            (normalizedTitle.Contains(token, StringComparison.OrdinalIgnoreCase) ||
             normalizedSummary.Contains(token, StringComparison.OrdinalIgnoreCase)));
        score += Math.Min(4, overlap);

        if (!string.IsNullOrWhiteSpace(entry.Title) &&
            query.Contains(entry.Title, StringComparison.OrdinalIgnoreCase))
        {
            score += 4;
        }

        if (Uri.TryCreate(entry.Url, UriKind.Absolute, out var uri) &&
            query.Contains(uri.Host, StringComparison.OrdinalIgnoreCase))
        {
            score += 3;
        }
        else
        {
            var hostLabel = ExtractHostLabel(normalizedUrl);
            if (hostLabel.Length >= 4 &&
                query.Contains(hostLabel, StringComparison.OrdinalIgnoreCase))
            {
                score += 2;
            }
        }

        if (queryTokens.Count <= 8 && entry.SeenCount >= 2 && overlap >= 1)
        {
            score += 1;
        }

        return score;
    }

    private static string ExtractHostLabel(string normalizedUrl)
    {
        if (string.IsNullOrWhiteSpace(normalizedUrl))
        {
            return string.Empty;
        }

        var slashIndex = normalizedUrl.IndexOf('/', StringComparison.Ordinal);
        var host = slashIndex >= 0 ? normalizedUrl[..slashIndex] : normalizedUrl;
        var dotIndex = host.IndexOf('.', StringComparison.Ordinal);
        return dotIndex >= 0 ? host[..dotIndex] : host;
    }

    private static string NormalizeWhitespace(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return string.Join(' ', value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    private static IReadOnlyList<string> Tokenize(string value)
    {
        return ConversationEvidenceIdentitySupport.NormalizeText(value)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}

