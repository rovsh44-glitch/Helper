using Helper.Api.Conversation;

namespace Helper.Runtime.Tests;

public sealed class SelectiveEvidenceMemoryPolicyTests
{
    [Fact]
    public void Select_ReturnsRelevantValidatedEntries_ForFollowUpQuery()
    {
        var policy = new SelectiveEvidenceMemoryPolicy();
        var previous = new SearchSessionState(
            BranchId: "main",
            RootQuery: "latest climate pact announcement",
            LastUserQuery: "latest climate pact announcement",
            LastEffectiveQuery: "latest climate pact announcement",
            LastTurnId: "turn-1",
            UpdatedAtUtc: DateTimeOffset.UtcNow.AddMinutes(-5),
            CategoryHint: "news",
            SourceUrls: new[] { "https://reuters.com/world/climate-pact" },
            CitationLineage: Array.Empty<CitationLineageEntry>(),
            EvidenceMemory: new[]
            {
                new SelectiveEvidenceMemoryEntry(
                    "mem_1",
                    "reuters.com/world/climate-pact|verified_passage|passage:p1",
                    "https://reuters.com/world/climate-pact",
                    "Leaders sign climate pact - Reuters",
                    "verified_passage",
                    "Reuters says negotiators agreed on a climate pact after overnight talks.",
                    "p1",
                    1,
                    "untrusted_web_content",
                    "turn-1",
                    "turn-1",
                    1)
            },
            ContinuationDepth: 0);

        var decision = policy.Select("what about the Reuters coverage?", previous);

        Assert.Single(decision.SelectedEntries);
        Assert.Equal("matched_validated_evidence", decision.Reason);
        Assert.Contains(decision.Trace, line => line.Contains("evidence_memory.match", StringComparison.Ordinal));
    }
}

