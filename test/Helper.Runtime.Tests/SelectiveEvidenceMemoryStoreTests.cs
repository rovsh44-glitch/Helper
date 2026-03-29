using Helper.Api.Conversation;
using Helper.Runtime.Core;
using Helper.Runtime.WebResearch;

namespace Helper.Runtime.Tests;

public sealed class SelectiveEvidenceMemoryStoreTests
{
    [Fact]
    public void Capture_PersistsOnlyValidatedEvidence_AndReusesExistingEntries()
    {
        var store = new SelectiveEvidenceMemoryStore();
        var previous = new SearchSessionState(
            BranchId: "main",
            RootQuery: "topic",
            LastUserQuery: "topic",
            LastEffectiveQuery: "topic",
            LastTurnId: "turn-1",
            UpdatedAtUtc: DateTimeOffset.UtcNow.AddMinutes(-10),
            CategoryHint: "science",
            SourceUrls: new[] { "https://example.org/paper" },
            CitationLineage: Array.Empty<CitationLineageEntry>(),
            EvidenceMemory: new[]
            {
                new SelectiveEvidenceMemoryEntry(
                    "mem_existing",
                    "example.org/paper|verified_passage|passage:p1",
                    "https://example.org/paper",
                    "Paper",
                    "verified_passage",
                    "Existing summary",
                    "p1",
                    1,
                    "untrusted_web_content",
                    "turn-1",
                    "turn-1",
                    1)
            },
            ContinuationDepth: 0);

        var update = store.Capture(
            previous,
            "turn-2",
            new ResearchEvidenceItem[]
            {
                new(
                    1,
                    "https://example.org/paper",
                    "Paper",
                    "Ignored because passage-level evidence exists",
                    EvidenceKind: "fetched_page",
                    Passages: new[]
                    {
                        new EvidencePassage(
                            "p1",
                            1,
                            1,
                            "1:p1",
                            "https://example.org/paper",
                            "Paper",
                            "2026-03-21",
                            "This is the validated passage that should be persisted.",
                            "verified_passage")
                    }),
                new(
                    2,
                    "https://example.org/hit",
                    "Search hit only",
                    "This search-hit-only snippet should not be persisted.",
                    EvidenceKind: "search_hit")
            });

        Assert.Equal(1, update.ReusedEntryCount);
        Assert.Equal(0, update.NewEntryCount);
        Assert.Single(update.Entries);
        Assert.Equal("mem_existing", update.Entries[0].MemoryId);
        Assert.Equal(2, update.Entries[0].SeenCount);
        Assert.Equal("turn-2", update.Entries[0].LastSeenTurnId);
        Assert.DoesNotContain(update.Entries, entry => entry.Url.Contains("example.org/hit", StringComparison.Ordinal));
    }
}

