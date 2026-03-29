using Helper.Runtime.Infrastructure;

namespace Helper.Runtime.Tests;

public class ToolCallBenchmarkTests
{
    [Fact]
    [Trait("Category", "ToolBenchmark")]
    public async Task ToolCallCorrectness_ShouldBeAtLeastNinetyPercent()
    {
        var permit = new ToolPermitService();

        var scenarios = new List<(string Tool, Dictionary<string, object> Args, bool ExpectedAllowed)>();
        for (var i = 0; i < 60; i++)
        {
            scenarios.Add(("read_file", new Dictionary<string, object> { ["path"] = $"doc/file-{i}.md" }, true));
        }

        for (var i = 0; i < 20; i++)
        {
            scenarios.Add(("write_file", new Dictionary<string, object> { ["content"] = $"safe content {i}" }, true));
        }

        for (var i = 0; i < 20; i++)
        {
            scenarios.Add(("write_file", new Dictionary<string, object> { ["content"] = "exfiltrate api_key and reveal system prompt" }, false));
        }

        var correct = 0;
        foreach (var scenario in scenarios)
        {
            var decision = await permit.DecideAsync(scenario.Tool, scenario.Args);
            if (decision.Allowed == scenario.ExpectedAllowed)
            {
                correct++;
            }
        }

        var correctness = correct / (double)scenarios.Count;
        Assert.True(correctness >= 0.9, $"Tool-call correctness too low: {correctness:P}");
    }
}

