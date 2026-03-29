using Helper.Runtime.Generation;

namespace Helper.Runtime.Tests;

[Collection("ProcessEnvironment")]
public class GenerationStageTimeoutPolicyTests
{
    [Fact]
    public void Resolve_UsesGlobalTimeoutAsDefaultSynthesisBudget()
    {
        using var scope = new EnvironmentVariableScope(new Dictionary<string, string?>
        {
            ["HELPER_CREATE_TIMEOUT_SEC"] = null,
            ["HELPER_CREATE_TIMEOUT_SYNTHESIS_SEC"] = null
        });

        var policy = new GenerationStageTimeoutPolicy();
        var timeout = policy.Resolve(GenerationTimeoutStage.Synthesis);

        Assert.Equal(TimeSpan.FromSeconds(900), timeout);
    }
}

