using Helper.Api.Hosting;
using Helper.Api.Conversation;

namespace Helper.Runtime.Tests;

public class LoadChaosSmokeTests
{
    [Fact]
    [Trait("Category", "Load")]
    public async Task MetricsServices_HandleParallelWrites()
    {
        var requestMetrics = new RequestMetricsService();
        var conversationMetrics = new ConversationMetricsService();

        var tasks = Enumerable.Range(0, 200).Select(async i =>
        {
            requestMetrics.Record("/api/chat", i % 10 == 0 ? 500 : 200, 50 + (i % 30));
            conversationMetrics.RecordTurn(new ConversationTurnMetric(
                FirstTokenLatencyMs: 100 + (i % 5),
                FullResponseLatencyMs: 300 + (i % 9),
                ToolCallsCount: i % 3,
                IsFactualPrompt: i % 2 == 0,
                HasCitations: i % 3 != 0,
                Confidence: 0.7,
                IsSuccessful: i % 10 != 0));
            await Task.Yield();
        });

        await Task.WhenAll(tasks);

        var req = requestMetrics.GetSnapshot();
        var conv = conversationMetrics.GetSnapshot();
        Assert.True(req.TotalRequests >= 200);
        Assert.True(conv.TotalTurns >= 200);
    }

    [Theory]
    [InlineData(100)]
    [InlineData(300)]
    [InlineData(500)]
    [Trait("Category", "Load")]
    public async Task StreamingResume_HandlesReconnectStorms(int concurrency)
    {
        var store = new InMemoryConversationStore();
        var state = store.GetOrCreate("load-conv");
        store.AddMessage(state, new ChatMessageDto(
            "assistant",
            new string('x', 5000),
            DateTimeOffset.UtcNow,
            "turn-load",
            1,
            "main"));

        var replaySuccess = 0;
        var splitChunks = 0;
        var tasks = Enumerable.Range(0, concurrency).Select(async i =>
        {
            var cursor = i % 2500;
            var ok = ChatStreamResumeHelper.TryBuildReplayResponse(
                store,
                "load-conv",
                new ChatStreamResumeRequestDto(CursorOffset: cursor, TurnId: "turn-load"),
                out var response);
            if (ok && response is not null)
            {
                Interlocked.Increment(ref replaySuccess);
                foreach (var _ in ChatStreamResumeHelper.SplitRemainingResponse(response.Response, cursor, chunkSize: 128))
                {
                    Interlocked.Increment(ref splitChunks);
                }
            }

            await Task.Yield();
        });

        await Task.WhenAll(tasks);

        Assert.True(replaySuccess >= concurrency * 0.95, $"Replay success too low: {replaySuccess}/{concurrency}");
        Assert.True(splitChunks > 0);
    }

    [Fact]
    [Trait("Category", "Load")]
    public async Task StreamingResume_PartialOutageSimulation_KeepsHighSuccessRate()
    {
        var store = new InMemoryConversationStore();
        var state = store.GetOrCreate("chaos-conv");
        store.AddMessage(state, new ChatMessageDto(
            "assistant",
            "chaos response payload " + new string('y', 3000),
            DateTimeOffset.UtcNow,
            "turn-chaos",
            1,
            "main"));

        var concurrency = 300;
        var totalExpectedSuccess = 0;
        var actualSuccess = 0;
        var tasks = Enumerable.Range(0, concurrency).Select(async i =>
        {
            var simulateOutage = i % 5 == 0;
            var conversationId = simulateOutage ? "missing-conversation" : "chaos-conv";
            if (!simulateOutage)
            {
                Interlocked.Increment(ref totalExpectedSuccess);
            }

            var ok = ChatStreamResumeHelper.TryBuildReplayResponse(
                store,
                conversationId,
                new ChatStreamResumeRequestDto(CursorOffset: i % 400, TurnId: "turn-chaos"),
                out _);

            if (ok)
            {
                Interlocked.Increment(ref actualSuccess);
            }

            await Task.Yield();
        });

        await Task.WhenAll(tasks);

        var successRate = totalExpectedSuccess == 0 ? 0 : actualSuccess / (double)totalExpectedSuccess;
        Assert.True(successRate >= 0.95, $"Partial outage success rate too low: {successRate:P2}");
    }
}

