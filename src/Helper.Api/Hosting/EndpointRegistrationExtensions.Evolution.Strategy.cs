#nullable enable

using System.Text.Json;
using Helper.Api.Conversation;
using Helper.Runtime.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace Helper.Api.Hosting;

public static partial class EndpointRegistrationExtensions
{
    private static void MapStrategyAndArchitectureEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/strategy/plan", (Func<StrategicPlanRequestDto, IStrategicPlanner, IGoalManager, ITemplateRoutingService, IHubContext<HelperHub>, CancellationToken, Task<IResult>>)(async ([FromBody] StrategicPlanRequestDto dto, IStrategicPlanner planner, IGoalManager goals, ITemplateRoutingService routing, IHubContext<HelperHub> hub, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(dto.Task))
            {
                return Results.BadRequest(new
                {
                    success = false,
                    error = "Task is required."
                });
            }

            var activeGoals = await goals.GetActiveGoalsAsync(ct).ConfigureAwait(false);
            var trimmedTask = dto.Task.Trim();
            var goalsContext = string.Join("\n", activeGoals.Select(g => $"{g.Title}: {g.Description}"));
            var strategicPrompt = string.IsNullOrWhiteSpace(goalsContext)
                ? trimmedTask
                : $"CURRENT GOALS:\n{goalsContext}\n\nTASK: {trimmedTask}";
            var plan = await planner.PlanStrategyAsync(strategicPrompt, dto.Context ?? string.Empty, ct).ConfigureAwait(false);
            var route = await routing.RouteAsync(trimmedTask, ct).ConfigureAwait(false);
            await hub.Clients.All.SendAsync("ReceiveProgress", $"[STRATEGY_JSON]{JsonSerializer.Serialize(plan)}", ct).ConfigureAwait(false);
            return Results.Ok(new
            {
                plan,
                activeGoals,
                route,
                analyzedAtUtc = DateTimeOffset.UtcNow
            });
        }));

        endpoints.MapPost("/api/architecture/plan", (Func<ArchitecturePlanRequestDto, IProjectPlanner, IBlueprintEngine, ITemplateRoutingService, CancellationToken, Task<IResult>>)ExecuteArchitecturePlanAsync);
    }

    private static async Task<IResult> ExecuteArchitecturePlanAsync(
        [FromBody] ArchitecturePlanRequestDto dto,
        IProjectPlanner planner,
        IBlueprintEngine blueprints,
        ITemplateRoutingService routing,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.Prompt))
        {
            return Results.BadRequest(new
            {
                success = false,
                error = "Prompt is required."
            });
        }

        var trimmedPrompt = dto.Prompt.Trim();
        try
        {
            var targetOs = ResolveTargetOs(dto.TargetOs);
            var plan = await planner.PlanProjectAsync(trimmedPrompt, ct).ConfigureAwait(false);
            var blueprint = await blueprints.DesignBlueprintAsync(trimmedPrompt, targetOs, ct).ConfigureAwait(false);
            var blueprintValid = await blueprints.ValidateBlueprintAsync(blueprint, ct).ConfigureAwait(false);
            var route = await routing.RouteAsync(trimmedPrompt, ct).ConfigureAwait(false);

            return Results.Ok(new
            {
                plan,
                route,
                blueprintValid,
                blueprint = new
                {
                    blueprint.Name,
                    targetOs = blueprint.TargetOS.ToString(),
                    blueprint.NuGetPackages,
                    blueprint.ArchitectureReasoning,
                    files = blueprint.Files.Select(file => new
                    {
                        file.Path,
                        file.Purpose,
                        role = file.Role.ToString(),
                        methodCount = file.Methods?.Count ?? 0
                    })
                },
                analyzedAtUtc = DateTimeOffset.UtcNow
            });
        }
        catch (ProjectPlanningException ex)
        {
            return Results.UnprocessableEntity(new
            {
                success = false,
                error = "Planner returned an invalid architecture plan.",
                planningErrorCode = ex.ErrorCode,
                analyzedAtUtc = DateTimeOffset.UtcNow
            });
        }
    }
}

