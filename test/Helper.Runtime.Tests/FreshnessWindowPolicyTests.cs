using Helper.Api.Conversation;

namespace Helper.Runtime.Tests;

public sealed class FreshnessWindowPolicyTests
{
    [Fact]
    public void Assess_AssignsShorterFreshnessWindow_ToVolatileQueries()
    {
        var policy = new FreshnessWindowPolicy();
        var storedAt = DateTimeOffset.UtcNow.AddMinutes(-45);

        var volatileAssessment = policy.Assess("What is the current price of BTC today?", storedAt);
        var softwareAssessment = policy.Assess("What is the latest .NET SDK version?", storedAt);

        Assert.Equal("volatile", volatileAssessment.Category);
        Assert.Equal(WebEvidenceFreshnessState.Stale, volatileAssessment.State);
        Assert.Equal("software", softwareAssessment.Category);
        Assert.NotEqual(WebEvidenceFreshnessState.Stale, softwareAssessment.State);
    }
}

