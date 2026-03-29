using System.Collections.Concurrent;

namespace Helper.Api.Hosting;

public interface IHelpfulnessTelemetryService
{
    void Record(string conversationId, string? turnId, int rating, IReadOnlyList<string>? tags, string? comment);
    ConversationFeedbackSnapshot GetGlobalSnapshot();
    ConversationFeedbackSnapshot GetConversationSnapshot(string conversationId);
}

public sealed class HelpfulnessTelemetryService : IHelpfulnessTelemetryService
{
    private readonly ConcurrentDictionary<string, FeedbackAccumulator> _byConversation = new(StringComparer.OrdinalIgnoreCase);

    public void Record(string conversationId, string? turnId, int rating, IReadOnlyList<string>? tags, string? comment)
    {
        var normalizedConversationId = string.IsNullOrWhiteSpace(conversationId) ? "global" : conversationId.Trim();
        var clamped = Math.Clamp(rating, 1, 5);
        var entry = _byConversation.GetOrAdd(normalizedConversationId, _ => new FeedbackAccumulator());
        entry.Add(clamped);
    }

    public ConversationFeedbackSnapshot GetGlobalSnapshot()
    {
        var all = _byConversation.Values.ToList();
        if (all.Count == 0)
        {
            return new ConversationFeedbackSnapshot(0, 0, 0, Array.Empty<string>());
        }

        var totalVotes = all.Sum(x => x.TotalVotes);
        var totalRating = all.Sum(x => x.TotalRating);
        return BuildSnapshot(totalVotes, totalRating);
    }

    public ConversationFeedbackSnapshot GetConversationSnapshot(string conversationId)
    {
        if (!_byConversation.TryGetValue(conversationId, out var bucket))
        {
            return new ConversationFeedbackSnapshot(0, 0, 0, Array.Empty<string>());
        }

        return BuildSnapshot(bucket.TotalVotes, bucket.TotalRating);
    }

    private static ConversationFeedbackSnapshot BuildSnapshot(int totalVotes, int totalRating)
    {
        var average = totalVotes == 0 ? 0 : (double)totalRating / totalVotes;
        var helpfulnessScore = totalVotes == 0 ? 0 : average / 5.0;
        var alerts = new List<string>();
        if (totalVotes >= 10 && average < 4.3)
        {
            alerts.Add("User helpfulness dropped below 4.3/5.");
        }

        return new ConversationFeedbackSnapshot(totalVotes, average, helpfulnessScore, alerts);
    }

    private sealed class FeedbackAccumulator
    {
        private int _votes;
        private int _totalRating;

        public int TotalVotes => Volatile.Read(ref _votes);
        public int TotalRating => Volatile.Read(ref _totalRating);

        public void Add(int rating)
        {
            Interlocked.Increment(ref _votes);
            Interlocked.Add(ref _totalRating, rating);
        }
    }
}

