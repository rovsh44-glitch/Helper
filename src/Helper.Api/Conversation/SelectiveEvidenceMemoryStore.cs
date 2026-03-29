using Helper.Runtime.Core;

namespace Helper.Api.Conversation;

internal sealed record SelectiveEvidenceMemoryEntry(
    string MemoryId,
    string EvidenceKey,
    string Url,
    string Title,
    string EvidenceKind,
    string Summary,
    string? PassageId,
    int? PassageOrdinal,
    string TrustLevel,
    string FirstSeenTurnId,
    string LastSeenTurnId,
    int SeenCount);

internal sealed record SelectiveEvidenceMemoryUpdate(
    IReadOnlyList<SelectiveEvidenceMemoryEntry> Entries,
    int CurrentEntryCount,
    int ReusedEntryCount,
    int NewEntryCount,
    IReadOnlyList<string> Trace);

internal interface ISelectiveEvidenceMemoryStore
{
    SelectiveEvidenceMemoryUpdate Capture(
        SearchSessionState? previousSession,
        string turnId,
        IReadOnlyList<ResearchEvidenceItem>? evidenceItems);
}

internal sealed class SelectiveEvidenceMemoryStore : ISelectiveEvidenceMemoryStore
{
    private const int MaxStoredEntries = 8;
    private const int MaxCurrentEntries = 6;

    public SelectiveEvidenceMemoryUpdate Capture(
        SearchSessionState? previousSession,
        string turnId,
        IReadOnlyList<ResearchEvidenceItem>? evidenceItems)
    {
        var previousEntries = previousSession?.EffectiveEvidenceMemory ?? Array.Empty<SelectiveEvidenceMemoryEntry>();
        var previousByKey = previousEntries
            .GroupBy(static entry => entry.EvidenceKey, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.Ordinal)
            ?? new Dictionary<string, SelectiveEvidenceMemoryEntry>(StringComparer.Ordinal);
        var currentEntries = EnumerateCurrentEntries(evidenceItems).Take(MaxCurrentEntries).ToArray();
        var currentKeys = new HashSet<string>(currentEntries.Select(static entry => entry.EvidenceKey), StringComparer.Ordinal);
        var mergedEntries = new List<SelectiveEvidenceMemoryEntry>(Math.Max(previousByKey.Count, currentEntries.Length));
        var trace = new List<string>();
        var reusedCount = 0;
        var newCount = 0;

        foreach (var current in currentEntries)
        {
            if (previousByKey.TryGetValue(current.EvidenceKey, out var existing))
            {
                reusedCount++;
                mergedEntries.Add(existing with
                {
                    Url = current.Url,
                    Title = current.Title,
                    EvidenceKind = current.EvidenceKind,
                    Summary = current.Summary,
                    PassageId = current.PassageId,
                    PassageOrdinal = current.PassageOrdinal,
                    TrustLevel = current.TrustLevel,
                    LastSeenTurnId = turnId,
                    SeenCount = existing.SeenCount + 1
                });
            }
            else
            {
                newCount++;
                mergedEntries.Add(current with
                {
                    MemoryId = $"mem_{Guid.NewGuid():N}",
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

        var ordered = mergedEntries
            .OrderByDescending(static entry => entry.SeenCount)
            .ThenByDescending(static entry => entry.LastSeenTurnId, StringComparer.Ordinal)
            .Take(MaxStoredEntries)
            .ToArray();

        trace.Add($"evidence_memory.entries={ordered.Length}");
        trace.Add($"evidence_memory.current={currentEntries.Length}");
        trace.Add($"evidence_memory.reused={reusedCount}");
        trace.Add($"evidence_memory.new={newCount}");

        foreach (var entry in ordered.Take(3))
        {
            trace.Add(
                $"evidence_memory.entry id={entry.MemoryId} kind={entry.EvidenceKind} source={ConversationEvidenceIdentitySupport.Summarize(entry.Url)} seen={entry.SeenCount}");
        }

        return new SelectiveEvidenceMemoryUpdate(
            ordered,
            currentEntries.Length,
            reusedCount,
            newCount,
            trace);
    }

    private static IEnumerable<SelectiveEvidenceMemoryEntry> EnumerateCurrentEntries(IReadOnlyList<ResearchEvidenceItem>? evidenceItems)
    {
        if (evidenceItems is null || evidenceItems.Count == 0)
        {
            yield break;
        }

        foreach (var item in evidenceItems.OrderBy(static item => item.Ordinal))
        {
            if (item.IsFallback)
            {
                continue;
            }

            if (item.Passages is { Count: > 0 })
            {
                foreach (var passage in item.Passages
                             .Where(static passage => !string.IsNullOrWhiteSpace(passage.Text))
                             .OrderBy(static passage => passage.PassageOrdinal)
                             .Take(2))
                {
                    yield return new SelectiveEvidenceMemoryEntry(
                        MemoryId: string.Empty,
                        EvidenceKey: ConversationEvidenceIdentitySupport.BuildEvidenceKey(
                            passage.Url,
                            passage.EvidenceKind,
                            passage.PassageId,
                            passage.PassageOrdinal,
                            passage.Title,
                            passage.Text),
                        Url: passage.Url,
                        Title: passage.Title,
                        EvidenceKind: passage.EvidenceKind,
                        Summary: TrimSummary(passage.Text),
                        PassageId: passage.PassageId,
                        PassageOrdinal: passage.PassageOrdinal,
                        TrustLevel: string.IsNullOrWhiteSpace(passage.TrustLevel) ? item.TrustLevel : passage.TrustLevel,
                        FirstSeenTurnId: string.Empty,
                        LastSeenTurnId: string.Empty,
                        SeenCount: 0);
                }

                continue;
            }

            if (!ShouldPersistItem(item))
            {
                continue;
            }

            yield return new SelectiveEvidenceMemoryEntry(
                MemoryId: string.Empty,
                EvidenceKey: ConversationEvidenceIdentitySupport.BuildEvidenceKey(
                    item.Url,
                    item.EvidenceKind,
                    null,
                    null,
                    item.Title,
                    item.Snippet),
                Url: item.Url,
                Title: item.Title,
                EvidenceKind: item.EvidenceKind,
                Summary: TrimSummary(item.Snippet),
                PassageId: null,
                PassageOrdinal: null,
                TrustLevel: item.TrustLevel,
                FirstSeenTurnId: string.Empty,
                LastSeenTurnId: string.Empty,
                SeenCount: 0);
        }
    }

    private static bool ShouldPersistItem(ResearchEvidenceItem item)
    {
        if (string.IsNullOrWhiteSpace(item.Url) || string.IsNullOrWhiteSpace(item.Snippet))
        {
            return false;
        }

        return string.Equals(item.EvidenceKind, "fetched_page", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(item.EvidenceKind, "fetched_document_pdf", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(item.EvidenceKind, "local_library_chunk", StringComparison.OrdinalIgnoreCase);
    }

    private static string TrimSummary(string text)
    {
        var normalized = text
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
        return normalized.Length <= 220 ? normalized : normalized[..220].TrimEnd() + "...";
    }
}

