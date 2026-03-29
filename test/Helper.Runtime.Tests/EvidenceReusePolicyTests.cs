using Helper.Api.Conversation;
using Helper.Api.Hosting;

namespace Helper.Runtime.Tests;

public sealed class EvidenceReusePolicyTests
{
    [Fact]
    public void Evaluate_ReusesPreviousSession_ForEllipticalFollowUp()
    {
        var policy = new EvidenceReusePolicy();
        var conversation = new ConversationState("conv-reuse");
        conversation.SearchSessions["main"] = new SearchSessionState(
            BranchId: "main",
            RootQuery: "latest climate pact announcement",
            LastUserQuery: "latest climate pact announcement",
            LastEffectiveQuery: "latest climate pact announcement",
            LastTurnId: "turn-1",
            UpdatedAtUtc: DateTimeOffset.UtcNow.AddMinutes(-3),
            CategoryHint: "news",
            SourceUrls: new[] { "https://reuters.com/world/climate-pact" },
            CitationLineage: new[]
            {
                new CitationLineageEntry(
                    "lin_1",
                    "reuters.com/world/climate-pact|fetched_page|passage:p1",
                    "1:p1",
                    "https://reuters.com/world/climate-pact",
                    "Leaders sign climate pact - Reuters",
                    "fetched_page",
                    "2026-03-21",
                    "p1",
                    1,
                    "turn-1",
                    "turn-1",
                    1)
            },
            ContinuationDepth: 0);

        var context = new ChatTurnContext
        {
            TurnId = "turn-2",
            Request = new ChatRequestDto("what about the Reuters coverage?", "conv-reuse", 10, null),
            Conversation = conversation,
            History = Array.Empty<ChatMessageDto>()
        };

        var decision = policy.Evaluate(context, SearchSessionStateAccessor.Get(conversation, null));

        Assert.True(decision.PreviousSessionFound);
        Assert.True(decision.ReusePreviousSession);
        Assert.Contains(decision.Reason, new[] { "citation_reference", "elliptical_followup" });
        Assert.Contains("latest climate pact announcement", decision.EffectiveQuery, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Reuters", decision.EffectiveQuery, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Evaluate_DoesNotReuse_WhenTopicShifts()
    {
        var policy = new EvidenceReusePolicy();
        var previous = new SearchSessionState(
            BranchId: "main",
            RootQuery: "latest climate pact announcement",
            LastUserQuery: "latest climate pact announcement",
            LastEffectiveQuery: "latest climate pact announcement",
            LastTurnId: "turn-1",
            UpdatedAtUtc: DateTimeOffset.UtcNow.AddMinutes(-4),
            CategoryHint: "news",
            SourceUrls: new[] { "https://reuters.com/world/climate-pact" },
            CitationLineage: Array.Empty<CitationLineageEntry>(),
            ContinuationDepth: 0);

        var context = new ChatTurnContext
        {
            TurnId = "turn-2",
            Request = new ChatRequestDto("best restaurant in Tashkent", "conv-reuse", 10, null),
            Conversation = new ConversationState("conv-reuse"),
            History = Array.Empty<ChatMessageDto>()
        };

        var decision = policy.Evaluate(context, previous);

        Assert.False(decision.ReusePreviousSession);
        Assert.Equal("topic_shift", decision.Reason);
        Assert.Equal("best restaurant in Tashkent", decision.EffectiveQuery);
    }

    [Fact]
    public void Evaluate_ReusesValidatedEvidenceMemory_ForSourceScopedFollowUp()
    {
        var policy = new EvidenceReusePolicy();
        var previous = new SearchSessionState(
            BranchId: "main",
            RootQuery: "latest climate pact announcement",
            LastUserQuery: "latest climate pact announcement",
            LastEffectiveQuery: "latest climate pact announcement",
            LastTurnId: "turn-1",
            UpdatedAtUtc: DateTimeOffset.UtcNow.AddMinutes(-4),
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
                    "Negotiators agreed on a climate pact after overnight talks.",
                    "p1",
                    1,
                    "untrusted_web_content",
                    "turn-1",
                    "turn-1",
                    1)
            },
            ContinuationDepth: 0);

        var context = new ChatTurnContext
        {
            TurnId = "turn-2",
            Request = new ChatRequestDto("what did Reuters say exactly?", "conv-reuse", 10, null),
            Conversation = new ConversationState("conv-reuse"),
            History = Array.Empty<ChatMessageDto>()
        };

        var decision = policy.Evaluate(context, previous);

        Assert.True(decision.ReusePreviousSession);
        Assert.Equal("validated_evidence_memory", decision.Reason);
        Assert.Contains("Previously validated context", decision.EffectiveQuery, StringComparison.Ordinal);
        Assert.Contains(decision.Trace, line => line.Contains("evidence_memory.selected=1", StringComparison.Ordinal));
    }
}

