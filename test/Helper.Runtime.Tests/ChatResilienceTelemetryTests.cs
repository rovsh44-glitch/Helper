using Helper.Api.Hosting;
using Helper.Api.Conversation;
using Microsoft.Extensions.Logging.Abstractions;

namespace Helper.Runtime.Tests;

public class ChatResilienceTelemetryTests
{
    [Fact]
    public void ChatResilienceTelemetryService_TracksRetriesCircuitAndFallback()
    {
        var telemetry = new ChatResilienceTelemetryService();

        telemetry.RecordAttempt("llm.ask");
        telemetry.RecordRetry("llm.ask");
        telemetry.RecordFailure("llm.ask", openedCircuit: true);
        telemetry.RecordFallback("critic_unavailable");
        telemetry.RecordAttempt("llm.ask");
        telemetry.RecordSuccess("llm.ask");

        var snapshot = telemetry.GetSnapshot();

        Assert.Equal(2, snapshot.TotalAttempts);
        Assert.Equal(1, snapshot.TotalSuccesses);
        Assert.Equal(1, snapshot.TotalRetries);
        Assert.Equal(1, snapshot.TotalFailures);
        Assert.Equal(1, snapshot.TotalCircuitOpenEvents);
        Assert.Equal(1, snapshot.TotalFallbacks);
        Assert.NotEmpty(snapshot.Alerts);
    }

    [Fact]
    public async Task ChatResiliencePolicy_StreamsAndAggregatesTokens()
    {
        var telemetry = new ChatResilienceTelemetryService();
        var policy = new ChatResiliencePolicy(NullLogger<ChatResiliencePolicy>.Instance, telemetry);
        var emitted = new List<string>();

        static async IAsyncEnumerable<string> TokenStream()
        {
            yield return "hel";
            await Task.Delay(1);
            yield return "lo";
        }

        await foreach (var token in policy.ExecuteStreamingAsync(
                           "llm.ask.stream",
                           _ => TokenStream(),
                           CancellationToken.None))
        {
            emitted.Add(token);
        }

        Assert.Equal("hello", string.Concat(emitted));
        Assert.Equal(new[] { "hel", "lo" }, emitted);
        var snapshot = telemetry.GetSnapshot();
        Assert.True(snapshot.TotalAttempts >= 1);
        Assert.True(snapshot.TotalSuccesses >= 1);
    }
}

