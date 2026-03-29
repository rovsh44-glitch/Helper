namespace Helper.Runtime.Tests;

public class UnitTest1
{
    [Fact]
    public void FeatureFlags_DefaultToEnabled()
    {
        var flags = new Helper.Api.Hosting.FeatureFlags();
        Assert.True(flags.AttachmentsEnabled);
        Assert.True(flags.RegenerateEnabled);
        Assert.True(flags.BranchingEnabled);
    }
}

