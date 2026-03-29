using System.Globalization;
using Helper.Api.Hosting;

namespace Helper.Api.Hosting;

public sealed record HumanLikeConversationDashboardSummary(
    int StyleTurns,
    double RepeatedPhraseRate,
    double MixedLanguageRate,
    int ClarificationTurns,
    int HelpfulClarificationTurns,
    int ClarificationRepairEscalations,
    double ClarificationHelpfulnessRate,
    int RepairAttempts,
    int RepairSucceeded,
    double RepairSuccessRate,
    int StyleFeedbackVotes,
    double StyleFeedbackAverageRating,
    double StyleLowRatingRate);

public sealed record HumanLikeConversationDashboardTrendPoint(
    string DateUtc,
    int StyleTurns,
    double RepeatedPhraseRate,
    double MixedLanguageRate,
    int ClarificationTurns,
    int HelpfulClarificationTurns,
    double ClarificationHelpfulnessRate,
    int RepairAttempts,
    double RepairSuccessRate,
    int StyleFeedbackVotes,
    double StyleFeedbackAverageRating);

public sealed record HumanLikeConversationDashboardSnapshot(
    DateTimeOffset GeneratedAtUtc,
    int WindowDays,
    HumanLikeConversationDashboardSummary Summary,
    IReadOnlyList<HumanLikeConversationDashboardTrendPoint> Trend,
    IReadOnlyList<string> Alerts);

public interface IHumanLikeConversationDashboardService
{
    void RecordUserTurn(string? conversationId, DateTimeOffset recordedAtUtc);
    void RecordAssistantTurn(string? conversationId, ChatResponseDto response, DateTimeOffset recordedAtUtc);
    void RecordRepairAttempt(string? conversationId, string? targetTurnId, DateTimeOffset recordedAtUtc);
    void RecordRepairOutcome(bool succeeded, DateTimeOffset recordedAtUtc);
    void RecordFeedback(string? conversationId, string? turnId, int rating, IReadOnlyList<string>? tags, string? comment, DateTimeOffset recordedAtUtc);
    HumanLikeConversationDashboardSnapshot GetSnapshot(int days = 7);
}

public sealed class HumanLikeConversationDashboardService : IHumanLikeConversationDashboardService
{
    private const int MaxRetainedDays = 35;

    private readonly object _gate = new();
    private readonly TimeProvider _timeProvider;
    private readonly Dictionary<DateOnly, HumanLikeConversationDayBucket> _dailyBuckets = new();
    private readonly Dictionary<string, TurnSurfaceRecord> _turns = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _pendingClarifications = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> _leadPhraseCounts = new(StringComparer.OrdinalIgnoreCase);

    public HumanLikeConversationDashboardService()
        : this(TimeProvider.System)
    {
    }

    public HumanLikeConversationDashboardService(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public void RecordUserTurn(string? conversationId, DateTimeOffset recordedAtUtc)
    {
        var normalizedConversationId = NormalizeConversationId(conversationId);
        if (normalizedConversationId is null)
        {
            return;
        }

        lock (_gate)
        {
            TrimOldData(ToDay(recordedAtUtc));
            if (!_pendingClarifications.Remove(normalizedConversationId, out var clarificationTurnId))
            {
                return;
            }

            if (!_turns.TryGetValue(clarificationTurnId, out var turn) || !turn.IsClarificationTurn)
            {
                return;
            }

            turn.ResolvedByFollowUp = true;
            MarkClarificationHelpful(turn);
        }
    }

    public void RecordAssistantTurn(string? conversationId, ChatResponseDto response, DateTimeOffset recordedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(response);

        var normalizedConversationId = NormalizeConversationId(response.ConversationId) ?? NormalizeConversationId(conversationId);
        var day = ToDay(recordedAtUtc);

        lock (_gate)
        {
            TrimOldData(day);
            var bucket = GetOrCreateBucket(day);
            var turnId = string.IsNullOrWhiteSpace(response.TurnId)
                ? null
                : response.TurnId.Trim();
            var style = response.StyleTelemetry;

            var isClarificationTurn = string.Equals(response.GroundingStatus, "clarification_required", StringComparison.OrdinalIgnoreCase);
            if (style is not null)
            {
                bucket.StyleTurns++;

                if (style.MixedLanguageDetected)
                {
                    bucket.MixedLanguageTurns++;
                }

                if (!string.IsNullOrWhiteSpace(style.LeadPhraseFingerprint))
                {
                    var fingerprint = style.LeadPhraseFingerprint.Trim();
                    var seenCount = _leadPhraseCounts.TryGetValue(fingerprint, out var currentCount)
                        ? currentCount + 1
                        : 1;
                    _leadPhraseCounts[fingerprint] = seenCount;
                    if (seenCount > 1)
                    {
                        bucket.RepeatedPhraseTurns++;
                    }
                }
            }

            if (isClarificationTurn)
            {
                bucket.ClarificationTurns++;
            }

            if (string.IsNullOrWhiteSpace(turnId))
            {
                return;
            }

            var turn = new TurnSurfaceRecord(turnId, normalizedConversationId, day, isClarificationTurn);
            _turns[turnId] = turn;

            if (isClarificationTurn && normalizedConversationId is not null)
            {
                _pendingClarifications[normalizedConversationId] = turnId;
            }
        }
    }

    public void RecordRepairAttempt(string? conversationId, string? targetTurnId, DateTimeOffset recordedAtUtc)
    {
        var normalizedConversationId = NormalizeConversationId(conversationId);
        var normalizedTargetTurnId = string.IsNullOrWhiteSpace(targetTurnId)
            ? null
            : targetTurnId.Trim();
        var day = ToDay(recordedAtUtc);

        lock (_gate)
        {
            TrimOldData(day);
            var bucket = GetOrCreateBucket(day);
            bucket.RepairAttempts++;

            if (normalizedConversationId is null || normalizedTargetTurnId is null)
            {
                return;
            }

            if (_pendingClarifications.TryGetValue(normalizedConversationId, out var pendingTurnId) &&
                string.Equals(pendingTurnId, normalizedTargetTurnId, StringComparison.OrdinalIgnoreCase) &&
                _turns.TryGetValue(normalizedTargetTurnId, out var turn) &&
                turn.IsClarificationTurn)
            {
                bucket.ClarificationRepairEscalations++;
                turn.EscalatedToRepair = true;
                _pendingClarifications.Remove(normalizedConversationId);
            }
        }
    }

    public void RecordRepairOutcome(bool succeeded, DateTimeOffset recordedAtUtc)
    {
        if (!succeeded)
        {
            return;
        }

        var day = ToDay(recordedAtUtc);
        lock (_gate)
        {
            TrimOldData(day);
            GetOrCreateBucket(day).RepairSucceeded++;
        }
    }

    public void RecordFeedback(string? conversationId, string? turnId, int rating, IReadOnlyList<string>? tags, string? comment, DateTimeOffset recordedAtUtc)
    {
        var normalizedTurnId = string.IsNullOrWhiteSpace(turnId)
            ? null
            : turnId.Trim();
        if (normalizedTurnId is null)
        {
            return;
        }

        var clampedRating = Math.Clamp(rating, 1, 5);
        var day = ToDay(recordedAtUtc);

        lock (_gate)
        {
            TrimOldData(day);
            if (!_turns.TryGetValue(normalizedTurnId, out var turn))
            {
                return;
            }

            var bucket = GetOrCreateBucket(day);
            bucket.StyleFeedbackVotes++;
            bucket.StyleFeedbackRatingSum += clampedRating;
            if (clampedRating <= 2)
            {
                bucket.StyleLowRatingVotes++;
            }

            turn.HasFeedback = true;
            if (clampedRating >= 4)
            {
                turn.PositiveFeedback = true;
                MarkClarificationHelpful(turn);
            }
        }
    }

    public HumanLikeConversationDashboardSnapshot GetSnapshot(int days = 7)
    {
        var windowDays = Math.Clamp(days, 1, MaxRetainedDays);
        var now = _timeProvider.GetUtcNow();
        var today = ToDay(now);

        lock (_gate)
        {
            TrimOldData(today);
            var selectedDays = Enumerable.Range(0, windowDays)
                .Select(offset => today.AddDays(-(windowDays - offset - 1)))
                .ToArray();
            var trend = selectedDays
                .Select(day => _dailyBuckets.TryGetValue(day, out var bucket)
                    ? bucket
                    : new HumanLikeConversationDayBucket(day))
                .Select(ToTrendPoint)
                .ToArray();

            var summaryTurns = trend.Sum(point => point.StyleTurns);
            var clarificationTurns = trend.Sum(point => point.ClarificationTurns);
            var helpfulClarificationTurns = trend.Sum(point => point.HelpfulClarificationTurns);
            var repairAttempts = trend.Sum(point => point.RepairAttempts);
            var repairSucceeded = trend.Sum(point => _dailyBuckets.TryGetValue(ParseDay(point.DateUtc), out var bucket) ? bucket.RepairSucceeded : 0);
            var feedbackVotes = trend.Sum(point => point.StyleFeedbackVotes);
            var feedbackRatingSum = selectedDays.Sum(day => _dailyBuckets.TryGetValue(day, out var bucket) ? bucket.StyleFeedbackRatingSum : 0);
            var lowRatingVotes = selectedDays.Sum(day => _dailyBuckets.TryGetValue(day, out var bucket) ? bucket.StyleLowRatingVotes : 0);
            var clarificationRepairEscalations = selectedDays.Sum(day => _dailyBuckets.TryGetValue(day, out var bucket) ? bucket.ClarificationRepairEscalations : 0);

            var summary = new HumanLikeConversationDashboardSummary(
                StyleTurns: summaryTurns,
                RepeatedPhraseRate: Rate(selectedDays.Sum(day => _dailyBuckets.TryGetValue(day, out var bucket) ? bucket.RepeatedPhraseTurns : 0), summaryTurns),
                MixedLanguageRate: Rate(selectedDays.Sum(day => _dailyBuckets.TryGetValue(day, out var bucket) ? bucket.MixedLanguageTurns : 0), summaryTurns),
                ClarificationTurns: clarificationTurns,
                HelpfulClarificationTurns: helpfulClarificationTurns,
                ClarificationRepairEscalations: clarificationRepairEscalations,
                ClarificationHelpfulnessRate: Rate(helpfulClarificationTurns, clarificationTurns),
                RepairAttempts: repairAttempts,
                RepairSucceeded: repairSucceeded,
                RepairSuccessRate: Rate(repairSucceeded, repairAttempts),
                StyleFeedbackVotes: feedbackVotes,
                StyleFeedbackAverageRating: Rate(feedbackRatingSum, feedbackVotes),
                StyleLowRatingRate: Rate(lowRatingVotes, feedbackVotes));

            return new HumanLikeConversationDashboardSnapshot(
                GeneratedAtUtc: now,
                WindowDays: windowDays,
                Summary: summary,
                Trend: trend,
                Alerts: BuildAlerts(summary));
        }
    }

    private void MarkClarificationHelpful(TurnSurfaceRecord turn)
    {
        if (!turn.IsClarificationTurn || turn.ClarificationHelpfulCounted)
        {
            return;
        }

        turn.ClarificationHelpfulCounted = true;
        GetOrCreateBucket(turn.Day).HelpfulClarificationTurns++;
    }

    private HumanLikeConversationDayBucket GetOrCreateBucket(DateOnly day)
    {
        if (_dailyBuckets.TryGetValue(day, out var existing))
        {
            return existing;
        }

        var created = new HumanLikeConversationDayBucket(day);
        _dailyBuckets[day] = created;
        return created;
    }

    private void TrimOldData(DateOnly today)
    {
        var cutoff = today.AddDays(-(MaxRetainedDays - 1));
        foreach (var staleDay in _dailyBuckets.Keys.Where(day => day < cutoff).ToArray())
        {
            _dailyBuckets.Remove(staleDay);
        }

        foreach (var staleTurn in _turns.Values.Where(turn => turn.Day < cutoff).Select(turn => turn.TurnId).ToArray())
        {
            _turns.Remove(staleTurn);
        }

        foreach (var pending in _pendingClarifications.Where(pair => !_turns.ContainsKey(pair.Value)).Select(pair => pair.Key).ToArray())
        {
            _pendingClarifications.Remove(pending);
        }
    }

    private static HumanLikeConversationDashboardTrendPoint ToTrendPoint(HumanLikeConversationDayBucket bucket)
    {
        return new HumanLikeConversationDashboardTrendPoint(
            DateUtc: FormatDay(bucket.Day),
            StyleTurns: bucket.StyleTurns,
            RepeatedPhraseRate: Rate(bucket.RepeatedPhraseTurns, bucket.StyleTurns),
            MixedLanguageRate: Rate(bucket.MixedLanguageTurns, bucket.StyleTurns),
            ClarificationTurns: bucket.ClarificationTurns,
            HelpfulClarificationTurns: bucket.HelpfulClarificationTurns,
            ClarificationHelpfulnessRate: Rate(bucket.HelpfulClarificationTurns, bucket.ClarificationTurns),
            RepairAttempts: bucket.RepairAttempts,
            RepairSuccessRate: Rate(bucket.RepairSucceeded, bucket.RepairAttempts),
            StyleFeedbackVotes: bucket.StyleFeedbackVotes,
            StyleFeedbackAverageRating: Rate(bucket.StyleFeedbackRatingSum, bucket.StyleFeedbackVotes));
    }

    private static IReadOnlyList<string> BuildAlerts(HumanLikeConversationDashboardSummary summary)
    {
        var alerts = new List<string>();
        if (summary.StyleTurns >= 10 && summary.RepeatedPhraseRate > 0.18)
        {
            alerts.Add("Repeated phrase rate is above 18%; templating drift is visible in live turns.");
        }

        if (summary.StyleTurns >= 5 && summary.MixedLanguageRate > 0.0)
        {
            alerts.Add("Mixed language rate is above 0%; language-lock regressions are surfacing in production turns.");
        }

        if (summary.ClarificationTurns >= 3 && summary.ClarificationHelpfulnessRate < 0.60)
        {
            alerts.Add("Clarification helpfulness dropped below 60%; clarification prompts are not converting into clean follow-ups.");
        }

        if (summary.RepairAttempts >= 3 && summary.RepairSuccessRate < 0.75)
        {
            alerts.Add("Repair success rate dropped below 75%; misunderstanding recovery is degrading.");
        }

        if (summary.StyleFeedbackVotes >= 5 && summary.StyleFeedbackAverageRating < 4.30)
        {
            alerts.Add("Style feedback average dropped below 4.3/5; users are rating the assistant surface lower than target.");
        }

        return alerts;
    }

    private static string? NormalizeConversationId(string? conversationId)
    {
        return string.IsNullOrWhiteSpace(conversationId)
            ? null
            : conversationId.Trim();
    }

    private static DateOnly ToDay(DateTimeOffset recordedAtUtc)
    {
        return DateOnly.FromDateTime(recordedAtUtc.UtcDateTime.Date);
    }

    private static string FormatDay(DateOnly day)
    {
        return day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private static DateOnly ParseDay(string value)
    {
        return DateOnly.ParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private static double Rate(int numerator, int denominator)
    {
        return denominator <= 0 ? 0 : (double)numerator / denominator;
    }

    private sealed class HumanLikeConversationDayBucket
    {
        public HumanLikeConversationDayBucket(DateOnly day)
        {
            Day = day;
        }

        public DateOnly Day { get; }
        public int StyleTurns { get; set; }
        public int RepeatedPhraseTurns { get; set; }
        public int MixedLanguageTurns { get; set; }
        public int ClarificationTurns { get; set; }
        public int HelpfulClarificationTurns { get; set; }
        public int ClarificationRepairEscalations { get; set; }
        public int RepairAttempts { get; set; }
        public int RepairSucceeded { get; set; }
        public int StyleFeedbackVotes { get; set; }
        public int StyleFeedbackRatingSum { get; set; }
        public int StyleLowRatingVotes { get; set; }
    }

    private sealed class TurnSurfaceRecord
    {
        public TurnSurfaceRecord(string turnId, string? conversationId, DateOnly day, bool isClarificationTurn)
        {
            TurnId = turnId;
            ConversationId = conversationId;
            Day = day;
            IsClarificationTurn = isClarificationTurn;
        }

        public string TurnId { get; }
        public string? ConversationId { get; }
        public DateOnly Day { get; }
        public bool IsClarificationTurn { get; }
        public bool HasFeedback { get; set; }
        public bool PositiveFeedback { get; set; }
        public bool ResolvedByFollowUp { get; set; }
        public bool EscalatedToRepair { get; set; }
        public bool ClarificationHelpfulCounted { get; set; }
    }
}

