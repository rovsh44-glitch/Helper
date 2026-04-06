using Helper.Api.Hosting;

namespace Helper.Runtime.Tests;

public sealed class WebResearchTelemetryServiceTests
{
    [Fact]
    public void RecordResponse_AggregatesQueriesPagesPassagesBlockedFetches_AndStaleDisclosures()
    {
        var telemetry = new WebResearchTelemetryService();
        var response = new ChatResponseDto(
            ConversationId: "conv-1",
            Response: "Done",
            Messages: Array.Empty<ChatMessageDto>(),
            Timestamp: DateTimeOffset.UtcNow,
            EpistemicAnswerMode: "needs_verification",
            RepairDriver: "interaction",
            EpistemicRisk: new EpistemicRiskSnapshotDto(
                AnswerMode: "needs_verification",
                GroundingStatus: "grounded_with_contradictions",
                CitationCoverage: 0.42,
                VerifiedClaimRatio: 0.5,
                HasContradictions: true,
                HasWeakEvidence: true,
                HighRiskDomain: true,
                FreshnessSensitive: true,
                ConfidenceCeiling: 0.42,
                CalibrationThreshold: 0.78,
                AbstentionRecommended: true),
            InteractionState: new InteractionStateSnapshotDto(
                FrustrationLevel: "moderate",
                UrgencyLevel: "low",
                OverloadRisk: "low",
                ReassuranceNeed: "high",
                ClarificationToleranceShift: -1,
                AssistantPressureRisk: "moderate"),
            SearchTrace: new SearchTraceDto(
                RequestedMode: "force_search",
                ResolvedRequirement: "web_required",
                Reason: "currentness",
                Status: "executed_live_web",
                Events: new[]
                {
                    "web_search.iteration_count=2",
                    "web_page_fetch.extracted_count=2",
                    "web_page_fetch.passage_count=5",
                    "web_fetch.blocked reason=private_or_loopback_address target=http://127.0.0.1",
                    "web_cache.state=stale"
                },
                Sources: new[]
                {
                    new SearchTraceSourceDto(1, "Doc 1", "https://example.com/1", PassageCount: 3),
                    new SearchTraceSourceDto(2, "Doc 2", "https://example.com/2", PassageCount: 2)
                }),
            UncertaintyFlags: new[] { "web_cache_refresh_failed_fallback" });

        telemetry.RecordResponse(response);
        var snapshot = telemetry.GetSnapshot();

        Assert.Equal(1, snapshot.Turns);
        Assert.Equal(1, snapshot.LiveWebTurns);
        Assert.Equal(1, snapshot.ForceSearchTurns);
        Assert.Equal(1, snapshot.TotalBlockedFetches);
        Assert.Equal(2, snapshot.AvgQueriesPerTurn);
        Assert.Equal(2, snapshot.AvgFetchedPagesPerTurn);
        Assert.Equal(5, snapshot.AvgPassagesPerTurn);
        Assert.Equal(1, snapshot.StaleDisclosureTurns);
        Assert.Equal(1, snapshot.NeedsVerificationTurns);
        Assert.Equal(1, snapshot.AbstentionRecommendedTurns);
        Assert.Equal(1, snapshot.ContradictionRiskTurns);
        Assert.Equal(1, snapshot.InteractionPressureTurns);
        Assert.Equal(1, snapshot.ReassuranceNeedTurns);
        Assert.Equal(1, snapshot.InteractionDrivenRepairTurns);
    }

    [Fact]
    public void RecordResponse_TracksCachedTurns_WithoutCountingIdleTraces()
    {
        var telemetry = new WebResearchTelemetryService();

        telemetry.RecordResponse(new ChatResponseDto(
            ConversationId: "conv-2",
            Response: "Cached answer",
            Messages: Array.Empty<ChatMessageDto>(),
            Timestamp: DateTimeOffset.UtcNow,
            EpistemicAnswerMode: "abstain",
            RepairDriver: "epistemic",
            EpistemicRisk: new EpistemicRiskSnapshotDto(
                AnswerMode: "abstain",
                GroundingStatus: "unverified",
                CitationCoverage: 0.1,
                VerifiedClaimRatio: 0.0,
                HasContradictions: false,
                HasWeakEvidence: true,
                HighRiskDomain: true,
                FreshnessSensitive: true,
                ConfidenceCeiling: 0.4,
                CalibrationThreshold: 0.78,
                AbstentionRecommended: true),
            SearchTrace: new SearchTraceDto(
                RequestedMode: "auto",
                ResolvedRequirement: "web_helpful",
                Reason: "follow_up",
                Status: "used_cached_web_result",
                Events: new[] { "web_search.iteration_count=1" })));

        telemetry.RecordResponse(new ChatResponseDto(
            ConversationId: "conv-3",
            Response: "No web needed",
            Messages: Array.Empty<ChatMessageDto>(),
            Timestamp: DateTimeOffset.UtcNow,
            SearchTrace: new SearchTraceDto(
                RequestedMode: "auto",
                ResolvedRequirement: "no_web_needed",
                Reason: null,
                Status: "not_needed")));

        var snapshot = telemetry.GetSnapshot();

        Assert.Equal(1, snapshot.Turns);
        Assert.Equal(1, snapshot.CachedWebTurns);
        Assert.Equal(0, snapshot.LiveWebTurns);
        Assert.Equal(1, snapshot.AbstainTurns);
        Assert.Equal(1, snapshot.EpistemicDrivenRepairTurns);
    }
}

