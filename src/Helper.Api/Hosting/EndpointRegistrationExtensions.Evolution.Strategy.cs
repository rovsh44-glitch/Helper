#nullable enable
#pragma warning disable CS8600, CS8601, CS8602, CS8603, CS8604, CS8619, CS8622, CS8632

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

			List<Goal> activeGoals = await goals.GetActiveGoalsAsync(ct);
			string goalsContext = string.Join("\n", activeGoals.Select(g => $"{g.Title}: {g.Description}"));
			string strategicPrompt = string.IsNullOrWhiteSpace(goalsContext)
				? dto.Task.Trim()
				: $"CURRENT GOALS:\n{goalsContext}\n\nTASK: {dto.Task.Trim()}";
			StrategicPlan plan = await planner.PlanStrategyAsync(strategicPrompt, dto.Context ?? string.Empty, ct);
			TemplateRoutingDecision route = await routing.RouteAsync(dto.Task.Trim(), ct);
			await hub.Clients.All.SendAsync("ReceiveProgress", $"[STRATEGY_JSON]{JsonSerializer.Serialize(plan)}", ct);
			return Results.Ok(new
			{
				plan,
				activeGoals,
				route,
				analyzedAtUtc = DateTimeOffset.UtcNow
			});
		}));

		endpoints.MapPost("/api/architecture/plan", (Func<ArchitecturePlanRequestDto, IProjectPlanner, IBlueprintEngine, ITemplateRoutingService, CancellationToken, Task<IResult>>)(async ([FromBody] ArchitecturePlanRequestDto dto, IProjectPlanner planner, IBlueprintEngine blueprints, ITemplateRoutingService routing, CancellationToken ct) =>
		{
			if (string.IsNullOrWhiteSpace(dto.Prompt))
			{
				return Results.BadRequest(new
				{
					success = false,
					error = "Prompt is required."
				});
			}

			OSPlatform targetOs = ResolveTargetOs(dto.TargetOs);
			ProjectPlan plan = await planner.PlanProjectAsync(dto.Prompt.Trim(), ct);
			ProjectBlueprint blueprint = await blueprints.DesignBlueprintAsync(dto.Prompt.Trim(), targetOs, ct);
			bool blueprintValid = await blueprints.ValidateBlueprintAsync(blueprint, ct);
			TemplateRoutingDecision route = await routing.RouteAsync(dto.Prompt.Trim(), ct);
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
		}));
	}
}

