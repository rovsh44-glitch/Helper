using Helper.Api.Conversation;
using Helper.Runtime.Core;
using Helper.Runtime.WebResearch;

namespace Helper.Runtime.Tests;

public sealed class CitationLineageStoreTests
{
    [Fact]
    public void Capture_ReusesExistingLineageIds_ForRepeatedPassages()
    {
        var store = new CitationLineageStore();
        var previous = new SearchSessionState(
            BranchId: "main",
            RootQuery: "topic",
            LastUserQuery: "topic",
            LastEffectiveQuery: "topic",
            LastTurnId: "turn-1",
            UpdatedAtUtc: DateTimeOffset.UtcNow.AddMinutes(-5),
            CategoryHint: "news",
            SourceUrls: new[] { "https://example.org/report" },
            CitationLineage: new[]
            {
                new CitationLineageEntry(
                    "lin_existing",
                    "example.org/report|fetched_page|passage:p1",
                    "1:p1",
                    "https://example.org/report",
                    "Report",
                    "fetched_page",
                    "2026-03-21",
                    "p1",
                    1,
                    "turn-1",
                    "turn-1",
                    1)
            },
            ContinuationDepth: 0);

        var update = store.Capture(
            previous,
            "turn-2",
            new[] { "https://example.org/report" },
            new[]
            {
                new ResearchEvidenceItem(
                    1,
                    "https://example.org/report",
                    "Report",
                    "Snippet",
                    EvidenceKind: "fetched_page",
                    Passages: new[]
                    {
                        new EvidencePassage(
                            "p1",
                            1,
                            1,
                            "1:p1",
                            "https://example.org/report",
                            "Report",
                            "2026-03-21",
                            "Evidence text",
                            "fetched_page")
                    })
            });

        Assert.Equal(1, update.ReusedCitationCount);
        Assert.Equal(0, update.NewCitationCount);
        Assert.Contains(update.Entries, entry => entry.LineageId == "lin_existing" && entry.LastSeenTurnId == "turn-2" && entry.SeenCount == 2);
    }
}

