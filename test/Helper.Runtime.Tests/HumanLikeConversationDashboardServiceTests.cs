using Helper.Api.Hosting;

namespace Helper.Runtime.Tests;

public sealed class HumanLikeConversationDashboardServiceTests
{
    [Fact]
    public void HumanLikeConversationDashboardService_ComputesRatesAndDayTrend()
    {
        var dayOne = new DateTimeOffset(2026, 03, 20, 09, 00, 00, TimeSpan.Zero);
        var dayTwo = dayOne.AddDays(1);
        var service = new HumanLikeConversationDashboardService(new FixedTimeProvider(dayTwo.AddHours(1)));

        service.RecordAssistantTurn("conv-hlc", BuildResponse(
            conversationId: "conv-hlc",
            turnId: "turn-1",
            leadPhraseFingerprint: "let's break this down",
            mixedLanguageDetected: false,
            groundingStatus: null), dayOne);
        service.RecordFeedback("conv-hlc", "turn-1", 2, new[] { "ui_quick_rating" }, null, dayOne);

        service.RecordAssistantTurn("conv-hlc", BuildResponse(
            conversationId: "conv-hlc",
            turnId: "turn-2",
            leadPhraseFingerprint: "let's break this down",
            mixedLanguageDetected: true,
            groundingStatus: "clarification_required"), dayTwo);
        service.RecordUserTurn("conv-hlc", dayTwo.AddMinutes(5));
        service.RecordRepairAttempt("conv-hlc", "turn-2", dayTwo.AddMinutes(10));
        service.RecordRepairOutcome(succeeded: true, dayTwo.AddMinutes(10));
        service.RecordFeedback("conv-hlc", "turn-2", 5, new[] { "ui_quick_rating" }, null, dayTwo.AddMinutes(12));

        var snapshot = service.GetSnapshot(days: 2);

        Assert.Equal(2, snapshot.WindowDays);
        Assert.Equal(2, snapshot.Trend.Count);
        Assert.Equal(2, snapshot.Summary.StyleTurns);
        Assert.Equal(0.5, snapshot.Summary.RepeatedPhraseRate, 3);
        Assert.Equal(0.5, snapshot.Summary.MixedLanguageRate, 3);
        Assert.Equal(1, snapshot.Summary.ClarificationTurns);
        Assert.Equal(1, snapshot.Summary.HelpfulClarificationTurns);
        Assert.Equal(1.0, snapshot.Summary.ClarificationHelpfulnessRate, 3);
        Assert.Equal(1, snapshot.Summary.RepairAttempts);
        Assert.Equal(1, snapshot.Summary.RepairSucceeded);
        Assert.Equal(1.0, snapshot.Summary.RepairSuccessRate, 3);
        Assert.Equal(2, snapshot.Summary.StyleFeedbackVotes);
        Assert.Equal(3.5, snapshot.Summary.StyleFeedbackAverageRating, 3);
        Assert.Equal(0.5, snapshot.Summary.StyleLowRatingRate, 3);

        var dayOneTrend = snapshot.Trend[0];
        var dayTwoTrend = snapshot.Trend[1];
        Assert.Equal("2026-03-20", dayOneTrend.DateUtc);
        Assert.Equal(0.0, dayOneTrend.RepeatedPhraseRate, 3);
        Assert.Equal("2026-03-21", dayTwoTrend.DateUtc);
        Assert.Equal(1.0, dayTwoTrend.RepeatedPhraseRate, 3);
        Assert.Equal(1.0, dayTwoTrend.MixedLanguageRate, 3);
        Assert.Equal(1.0, dayTwoTrend.ClarificationHelpfulnessRate, 3);
        Assert.Equal(1.0, dayTwoTrend.RepairSuccessRate, 3);
    }

    [Fact]
    public void HumanLikeConversationDashboardService_CountsHelpfulClarificationOnlyOnce()
    {
        var recordedAt = new DateTimeOffset(2026, 03, 21, 08, 30, 00, TimeSpan.Zero);
        var service = new HumanLikeConversationDashboardService(new FixedTimeProvider(recordedAt.AddHours(1)));

        service.RecordAssistantTurn("conv-clarify", BuildResponse(
            conversationId: "conv-clarify",
            turnId: "clarify-1",
            leadPhraseFingerprint: "could you narrow that down",
            mixedLanguageDetected: false,
            groundingStatus: "clarification_required"), recordedAt);
        service.RecordFeedback("conv-clarify", "clarify-1", 5, new[] { "ui_quick_rating" }, null, recordedAt.AddMinutes(1));
        service.RecordUserTurn("conv-clarify", recordedAt.AddMinutes(2));

        var snapshot = service.GetSnapshot(days: 1);

        Assert.Equal(1, snapshot.Summary.ClarificationTurns);
        Assert.Equal(1, snapshot.Summary.HelpfulClarificationTurns);
        Assert.Equal(1.0, snapshot.Summary.ClarificationHelpfulnessRate, 3);
    }

    private static ChatResponseDto BuildResponse(
        string conversationId,
        string turnId,
        string? leadPhraseFingerprint,
        bool mixedLanguageDetected,
        string? groundingStatus)
    {
        return new ChatResponseDto(
            ConversationId: conversationId,
            Response: "answer",
            Messages: Array.Empty<ChatMessageDto>(),
            Timestamp: DateTimeOffset.UtcNow,
            Confidence: 0.82,
            Sources: Array.Empty<string>(),
            TurnId: turnId,
            ToolCalls: Array.Empty<string>(),
            RequiresConfirmation: false,
            NextStep: null,
            GroundingStatus: groundingStatus,
            CitationCoverage: 0.0,
            VerifiedClaims: 0,
            TotalClaims: 0,
            UncertaintyFlags: Array.Empty<string>(),
            StyleTelemetry: new ConversationStyleTelemetryDto(
                LeadPhraseFingerprint: leadPhraseFingerprint,
                MixedLanguageDetected: mixedLanguageDetected,
                GenericClarificationDetected: string.Equals(groundingStatus, "clarification_required", StringComparison.OrdinalIgnoreCase),
                GenericNextStepDetected: false,
                MemoryAckTemplateDetected: false,
                SourceFingerprint: null));
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}

