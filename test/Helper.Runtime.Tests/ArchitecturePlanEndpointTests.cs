using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Helper.Api.Hosting;
using Helper.Runtime.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Helper.Runtime.Tests;

[Trait("Lane", "Fast")]
public sealed class ArchitecturePlanEndpointTests
{
    [Fact]
    public async Task ArchitecturePlanEndpoint_ReturnsSuccessPayload_ForValidPlannerResult()
    {
        await using var app = BuildApp(
            new StubProjectPlanner(new ProjectPlan("Planner output", new List<FileTask>
            {
                new("App.csproj", "Project file", new List<string>())
            })),
            new StubBlueprintEngine(),
            new StubTemplateRoutingService());
        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync();

        try
        {
            using var client = BuildClient(app);
            using var response = await client.PostAsJsonAsync("/api/architecture/plan", new ArchitecturePlanRequestDto("Build a console app"));

            response.EnsureSuccessStatusCode();
            using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("Planner output", payload.RootElement.GetProperty("plan").GetProperty("description").GetString());
            Assert.True(payload.RootElement.GetProperty("blueprintValid").GetBoolean());
        }
        finally
        {
            await app.StopAsync();
        }
    }

    [Fact]
    public async Task ArchitecturePlanEndpoint_ReturnsUnprocessableEntity_WhenPlannerFails()
    {
        await using var app = BuildApp(
            new ThrowingProjectPlanner(new ProjectPlanningException("planner_invalid_json", "Planner returned invalid JSON.")),
            new StubBlueprintEngine(),
            new StubTemplateRoutingService());
        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync();

        try
        {
            using var client = BuildClient(app);
            using var response = await client.PostAsJsonAsync("/api/architecture/plan", new ArchitecturePlanRequestDto("Build a console app"));

            Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
            using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.False(payload.RootElement.GetProperty("success").GetBoolean());
            Assert.Equal("planner_invalid_json", payload.RootElement.GetProperty("planningErrorCode").GetString());
        }
        finally
        {
            await app.StopAsync();
        }
    }

    private static WebApplication BuildApp(
        IProjectPlanner planner,
        IBlueprintEngine blueprints,
        ITemplateRoutingService routing)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSignalR();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(planner);
        builder.Services.AddSingleton(blueprints);
        builder.Services.AddSingleton(routing);
        builder.Services.AddSingleton<IStrategicPlanner, StubStrategicPlanner>();
        builder.Services.AddSingleton<IGoalManager, EmptyGoalManager>();

        var app = builder.Build();
        var mapMethod = typeof(EndpointRegistrationExtensions).GetMethod(
            "MapStrategyAndArchitectureEndpoints",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(mapMethod);
        mapMethod!.Invoke(null, new object[] { app });
        return app;
    }

    private static HttpClient BuildClient(WebApplication app)
    {
        var addressFeature = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
        var baseAddress = new Uri(Assert.Single(addressFeature!.Addresses));
        return new HttpClient { BaseAddress = baseAddress };
    }

    private sealed class StubProjectPlanner : IProjectPlanner
    {
        private readonly ProjectPlan _plan;

        public StubProjectPlanner(ProjectPlan plan)
        {
            _plan = plan;
        }

        public Task<ProjectPlan> PlanProjectAsync(string prompt, CancellationToken ct = default)
            => Task.FromResult(_plan);
    }

    private sealed class ThrowingProjectPlanner : IProjectPlanner
    {
        private readonly Exception _exception;

        public ThrowingProjectPlanner(Exception exception)
        {
            _exception = exception;
        }

        public Task<ProjectPlan> PlanProjectAsync(string prompt, CancellationToken ct = default)
            => Task.FromException<ProjectPlan>(_exception);
    }

    private sealed class StubBlueprintEngine : IBlueprintEngine
    {
        public Task<ProjectBlueprint> DesignBlueprintAsync(string prompt, OSPlatform targetOS, CancellationToken ct = default)
        {
            return Task.FromResult(new ProjectBlueprint(
                "Sample",
                targetOS,
                new List<SwarmFileDefinition>
                {
                    new("Program.cs", "Entry point", FileRole.Logic, new List<string>())
                },
                new List<string>(),
                "Stub blueprint"));
        }

        public Task<bool> ValidateBlueprintAsync(ProjectBlueprint blueprint, CancellationToken ct = default)
            => Task.FromResult(true);
    }

    private sealed class StubTemplateRoutingService : ITemplateRoutingService
    {
        public Task<TemplateRoutingDecision> RouteAsync(string prompt, CancellationToken ct = default)
            => Task.FromResult(new TemplateRoutingDecision(false, null, 0.12, Array.Empty<string>(), "No template match"));
    }

    private sealed class StubStrategicPlanner : IStrategicPlanner
    {
        public Task<StrategicPlan> PlanStrategyAsync(string task, string availableContext, CancellationToken ct = default)
            => Task.FromResult(new StrategicPlan(
                "default",
                new List<StrategyBranch>
                {
                    new("default", "Stub strategy", 0.75, new List<string>())
                },
                "Stub reasoning",
                false,
                new List<string>()));
    }

    private sealed class EmptyGoalManager : IGoalManager
    {
        public Task<List<Goal>> GetGoalsAsync(bool includeCompleted = true, CancellationToken ct = default)
            => Task.FromResult(new List<Goal>());

        public Task<List<Goal>> GetActiveGoalsAsync(CancellationToken ct = default)
            => Task.FromResult(new List<Goal>());

        public Task AddGoalAsync(string title, string description, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<bool> UpdateGoalAsync(Guid id, string title, string description, CancellationToken ct = default)
            => Task.FromResult(false);

        public Task<bool> DeleteGoalAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult(false);

        public Task<bool> MarkGoalCompletedAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult(false);
    }
}
