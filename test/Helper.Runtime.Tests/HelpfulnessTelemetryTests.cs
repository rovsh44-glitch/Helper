using Helper.Api.Hosting;

namespace Helper.Runtime.Tests;

public class HelpfulnessTelemetryTests
{
    [Fact]
    public void HelpfulnessTelemetry_ComputesAverage()
    {
        var telemetry = new HelpfulnessTelemetryService();
        telemetry.Record("conv-1", "t1", 5, null, null);
        telemetry.Record("conv-1", "t2", 4, null, null);
        telemetry.Record("conv-2", "t3", 3, null, null);

        var global = telemetry.GetGlobalSnapshot();
        var conv = telemetry.GetConversationSnapshot("conv-1");

        Assert.Equal(3, global.TotalVotes);
        Assert.True(global.AverageRating > 3.9);
        Assert.Equal(2, conv.TotalVotes);
    }
}

