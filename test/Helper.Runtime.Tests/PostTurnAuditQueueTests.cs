using Helper.Api.Conversation;

namespace Helper.Runtime.Tests;

public class PostTurnAuditQueueTests
{
    [Fact]
    public async Task PostTurnAuditQueue_EnqueueDequeueAndSnapshot()
    {
        var queue = new PostTurnAuditQueue();
        var item = new PostTurnAuditItem(
            "conv-1",
            "turn-1",
            "what is .net",
            "answer",
            true,
            new List<string> { "https://example.org" },
            DateTimeOffset.UtcNow);

        var accepted = queue.Enqueue(item);
        Assert.True(accepted);

        var read = await queue.DequeueAsync(CancellationToken.None);
        Assert.Equal("turn-1", read.TurnId);

        queue.RecordProcessed(120, success: true);
        var snapshot = queue.GetSnapshot();

        Assert.Equal(0, snapshot.Pending);
        Assert.Equal(1, snapshot.Enqueued);
        Assert.Equal(1, snapshot.Processed);
        Assert.Equal(0, snapshot.Failed);
        Assert.True(snapshot.AvgProcessingMs >= 100);
    }
}

