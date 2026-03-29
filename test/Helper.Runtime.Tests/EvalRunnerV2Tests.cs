using Helper.Api.Conversation;

namespace Helper.Runtime.Tests;

public class EvalRunnerV2Tests
{
    [Fact]
    [Trait("Category", "EvalV2")]
    public async Task EvalRunnerV2_PreparesAtLeastThousandScenarioRuns()
    {
        using var dataset = EvalTestPackageFactory.CreateEndToEndDataset();
        var runner = new EvalRunnerV2();

        var prepared = await runner.PrepareRunAsync(
            dataset.PrimaryPath,
            new EvalRunOptions(MinScenarioRuns: 1000, UseRealModel: false, Seed: 17),
            CancellationToken.None);

        Assert.True(prepared.Scenarios.Count >= 1000);
        Assert.True(prepared.Summary.TotalScenarios >= 1000);
        Assert.True(prepared.Summary.LanguageDistribution.ContainsKey("ru"));
        Assert.True(prepared.Summary.LanguageDistribution.ContainsKey("en"));
    }

    [Fact]
    [Trait("Category", "EvalV2")]
    public async Task EvalRunnerV2_MaintainsEndToEndRatioAtLeastSixtyPercent()
    {
        using var dataset = EvalTestPackageFactory.CreateEndToEndDataset();
        var runner = new EvalRunnerV2();

        var prepared = await runner.PrepareRunAsync(
            dataset.PrimaryPath,
            new EvalRunOptions(MinScenarioRuns: 1000, UseRealModel: false, Seed: 23),
            CancellationToken.None);

        Assert.True(prepared.Summary.EndToEndRatio >= 0.6, $"EndToEndRatio too low: {prepared.Summary.EndToEndRatio:P2}");
    }
}

