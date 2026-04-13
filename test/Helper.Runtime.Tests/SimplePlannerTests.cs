using Helper.Runtime.Core;
using Helper.Runtime.Infrastructure;

namespace Helper.Runtime.Tests;

public sealed class SimplePlannerTests
{
    [Fact]
    public async Task PlanProjectAsync_ParsesValidJsonFence()
    {
        var ai = new StubPlannerAiLink("""
            ```json
            {
              "Description": "Sample project",
              "PlannedFiles": [
                {
                  "Path": "Sample.csproj",
                  "Purpose": "Project file",
                  "Dependencies": []
                }
              ]
            }
            ```
            """);
        var planner = new SimplePlanner(ai);

        var plan = await planner.PlanProjectAsync("sample prompt");

        Assert.Equal("Sample project", plan.Description);
        Assert.Single(plan.PlannedFiles);
        Assert.Equal("Sample.csproj", plan.PlannedFiles[0].Path);
    }

    [Fact]
    public async Task PlanProjectAsync_ThrowsProjectPlanningException_WhenJsonIsInvalid()
    {
        var ai = new StubPlannerAiLink("not-json");
        var planner = new SimplePlanner(ai);

        var ex = await Assert.ThrowsAsync<ProjectPlanningException>(() => planner.PlanProjectAsync("sample prompt"));

        Assert.Equal("planner_invalid_json", ex.ErrorCode);
    }

    [Fact]
    public async Task PlanProjectAsync_ThrowsProjectPlanningException_WhenPlanHasNoFiles()
    {
        var ai = new StubPlannerAiLink("""
            {
              "Description": "Empty plan",
              "PlannedFiles": []
            }
            """);
        var planner = new SimplePlanner(ai);

        var ex = await Assert.ThrowsAsync<ProjectPlanningException>(() => planner.PlanProjectAsync("sample prompt"));

        Assert.Equal("planner_empty_plan", ex.ErrorCode);
    }

    private sealed class StubPlannerAiLink : AILink
    {
        private readonly string _response;

        public StubPlannerAiLink(string response)
            : base("http://localhost:11434", "qwen2.5-coder:14b")
        {
            _response = response;
        }

        public override Task<string> AskAsync(string prompt, CancellationToken ct, string? overrideModel = null, string? base64Image = null, int keepAliveSeconds = 300, string? systemInstruction = null)
            => Task.FromResult(_response);
    }
}
