using Helper.Runtime.Core;

namespace Helper.Api.Conversation;

internal sealed record CitationLineageEntry(
    string LineageId,
    string EvidenceKey,
    string CitationLabel,
    string Url,
    string Title,
    string EvidenceKind,
    string? PublishedAt,
    string? PassageId,
    int? PassageOrdinal,
    string FirstSeenTurnId,
    string LastSeenTurnId,
    int SeenCount);

internal sealed record CitationLineageUpdate(
    IReadOnlyList<CitationLineageEntry> Entries,
    int CurrentCitationCount,
    int ReusedCitationCount,
    int NewCitationCount,
    IReadOnlyList<string> Trace);

internal interface ICitationLineageStore
{
    CitationLineageUpdate Capture(
        SearchSessionState? previousSession,
        string turnId,
        IReadOnlyList<string> sourceUrls,
        IReadOnlyList<ResearchEvidenceItem>? evidenceItems);
}

internal sealed partial class CitationLineageStore : ICitationLineageStore
{
    public CitationLineageUpdate Capture(
        SearchSessionState? previousSession,
        string turnId,
        IReadOnlyList<string> sourceUrls,
        IReadOnlyList<ResearchEvidenceItem>? evidenceItems)
    {
        var previousByKey = previousSession?.CitationLineage
            .GroupBy(static entry => entry.EvidenceKey, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.Ordinal)
            ?? new Dictionary<string, CitationLineageEntry>(StringComparer.Ordinal);

        var currentEntries = EnumerateCurrentEntries(sourceUrls, evidenceItems).ToArray();
        var currentKeys = new HashSet<string>(StringComparer.Ordinal);
        var mergedEntries = new List<CitationLineageEntry>(Math.Max(currentEntries.Length, previousByKey.Count));
        var trace = new List<string>();
        var reusedCount = 0;
        var newCount = 0;

        foreach (var current in currentEntries)
        {
            currentKeys.Add(current.EvidenceKey);
            if (previousByKey.TryGetValue(current.EvidenceKey, out var existing))
            {
                reusedCount++;
                mergedEntries.Add(existing with
                {
                    CitationLabel = current.CitationLabel,
                    Url = current.Url,
                    Title = current.Title,
                    EvidenceKind = current.EvidenceKind,
                    PublishedAt = current.PublishedAt,
                    PassageId = current.PassageId,
                    PassageOrdinal = current.PassageOrdinal,
                    LastSeenTurnId = turnId,
                    SeenCount = existing.SeenCount + 1
                });
            }
            else
            {
                newCount++;
                mergedEntries.Add(current with
                {
                    LineageId = $"lin_{Guid.NewGuid():N}",
                    FirstSeenTurnId = turnId,
                    LastSeenTurnId = turnId,
                    SeenCount = 1
                });
            }
        }

        foreach (var previous in previousByKey.Values)
        {
            if (!currentKeys.Contains(previous.EvidenceKey))
            {
                mergedEntries.Add(previous);
            }
        }

        trace.Add($"citation_lineage.entries={mergedEntries.Count}");
        trace.Add($"citation_lineage.current={currentEntries.Length}");
        trace.Add($"citation_lineage.reused={reusedCount}");
        trace.Add($"citation_lineage.new={newCount}");

        foreach (var entry in mergedEntries.Take(3))
        {
            trace.Add(
                $"citation_lineage.entry id={entry.LineageId} source={ConversationEvidenceIdentitySupport.Summarize(entry.Url)} first_turn={entry.FirstSeenTurnId} last_turn={entry.LastSeenTurnId} seen={entry.SeenCount}");
        }

        return new CitationLineageUpdate(
            mergedEntries
                .OrderByDescending(static entry => entry.SeenCount)
                .ThenBy(static entry => entry.Url, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            currentEntries.Length,
            reusedCount,
            newCount,
            trace);
    }

    private static IEnumerable<CitationLineageEntry> EnumerateCurrentEntries(
        IReadOnlyList<string> sourceUrls,
        IReadOnlyList<ResearchEvidenceItem>? evidenceItems)
    {
        if (evidenceItems is { Count: > 0 })
        {
            foreach (var item in evidenceItems.OrderBy(static item => item.Ordinal))
            {
                if (item.Passages is { Count: > 0 })
                {
                    foreach (var passage in item.Passages.OrderBy(static passage => passage.PassageOrdinal))
                    {
                        yield return new CitationLineageEntry(
                            LineageId: string.Empty,
                            EvidenceKey: ConversationEvidenceIdentitySupport.BuildEvidenceKey(
                                passage.Url,
                                passage.EvidenceKind,
                                passage.PassageId,
                                passage.PassageOrdinal,
                                passage.Title,
                                passage.Text),
                            CitationLabel: passage.CitationLabel,
                            Url: passage.Url,
                            Title: passage.Title,
                            EvidenceKind: passage.EvidenceKind,
                            PublishedAt: passage.PublishedAt,
                            PassageId: passage.PassageId,
                            PassageOrdinal: passage.PassageOrdinal,
                            FirstSeenTurnId: string.Empty,
                            LastSeenTurnId: string.Empty,
                            SeenCount: 0);
                    }

                    continue;
                }

                yield return new CitationLineageEntry(
                    LineageId: string.Empty,
                    EvidenceKey: ConversationEvidenceIdentitySupport.BuildEvidenceKey(item.Url, item.EvidenceKind, null, null, item.Title, item.Snippet),
                    CitationLabel: item.Ordinal.ToString(),
                    Url: item.Url,
                    Title: item.Title,
                    EvidenceKind: item.EvidenceKind,
                    PublishedAt: item.PublishedAt,
                    PassageId: null,
                    PassageOrdinal: null,
                    FirstSeenTurnId: string.Empty,
                    LastSeenTurnId: string.Empty,
                    SeenCount: 0);
            }

            yield break;
        }

        foreach (var source in sourceUrls.Where(static url => !string.IsNullOrWhiteSpace(url)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            yield return new CitationLineageEntry(
                LineageId: string.Empty,
                EvidenceKey: ConversationEvidenceIdentitySupport.BuildEvidenceKey(source, "source_url", null, null, source, source),
                CitationLabel: "?",
                Url: source,
                Title: source,
                EvidenceKind: "source_url",
                PublishedAt: null,
                PassageId: null,
                PassageOrdinal: null,
                FirstSeenTurnId: string.Empty,
                LastSeenTurnId: string.Empty,
                SeenCount: 0);
        }
    }

}

