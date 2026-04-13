#nullable enable
// Minimal API delegate signatures still trigger a narrow set of Roslyn nullability mismatches here.
#pragma warning disable CS8600, CS8619, CS8622

using System.Text.Json;
using Helper.Api.Conversation;
using Helper.Runtime.Core;
using Helper.Runtime.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace Helper.Api.Hosting;

public static partial class EndpointRegistrationExtensions
{
	private static void MapEvolutionAndIndexingEndpoints(IEndpointRouteBuilder endpoints, ApiRuntimeConfig runtimeConfig)
	{
		endpoints.MapGet("/api/evolution/status", (Func<ILearningCoordinator, IGoalManager, Task<IResult>>)async delegate(ILearningCoordinator coordinator, IGoalManager goals)
		{
			IndexingProgress progress = await coordinator.GetProgressAsync();
			List<Goal> activeGoals = await goals.GetActiveGoalsAsync();
			bool isIndexing = progress.IndexingStatus == LearningStatus.Running;
			bool isEvolution = progress.EvolutionStatus == LearningStatus.Running;
			bool isPaused = progress.IndexingStatus == LearningStatus.Paused || progress.EvolutionStatus == LearningStatus.Paused;
			return Results.Ok(new
			{
				processedFiles = progress.ProcessedFiles,
				totalFiles = progress.TotalFiles,
				activeTask = progress.CurrentFile,
				goals = activeGoals,
				isLearning = isIndexing || isEvolution,
				isIndexing,
				isEvolution,
				currentPhase = isIndexing ? "Indexing" : (isEvolution ? "Evolution" : (isPaused ? "Paused" : "Idle")),
				fileProgress = progress.CurrentFileProgress,
				pipelineVersion = progress.PipelineVersion,
				chunkingStrategy = progress.ChunkingStrategy,
				currentSection = progress.CurrentSection,
				currentPageStart = progress.CurrentPageStart,
				currentPageEnd = progress.CurrentPageEnd,
				parserVersion = progress.ParserVersion,
				recentLearnings = Array.Empty<string>()
			});
		});

		endpoints.MapPost("/api/evolution/mutation/propose", (Func<IHealthMonitor, IEvolutionEngine, IHubContext<HelperHub>, CancellationToken, Task<IResult>>)(async (IHealthMonitor healthMonitor, IEvolutionEngine evolution, IHubContext<HelperHub> hub, CancellationToken ct) =>
		{
			HealthStatus status = await healthMonitor.DiagnoseAsync(ct);
			MutationProposal? mutation = await evolution.ProposeEvolutionAsync(status, ct);
			if (mutation is not null)
			{
				await hub.Clients.All.SendAsync("ReceiveProgress", $"[MUTATION_JSON]{JsonSerializer.Serialize(mutation)}", ct);
			}

			return Results.Ok(new
			{
				success = true,
				status,
				mutation
			});
		}));

		endpoints.MapPost("/api/evolution/challenge", (Func<ChallengeRequest, ICriticService, CancellationToken, Task<IResult>>)(async ([FromBody] ChallengeRequest req, ICriticService critic, CancellationToken ct) => Results.Ok(await critic.ChallengeAsync(req.Proposal, ct))));
		endpoints.MapGet("/api/evolution/library", (Func<IResult>)delegate
		{
			List<LibraryItemDto> value = ApiProgramHelpers.LoadLibraryQueue(runtimeConfig.IndexingQueuePath, runtimeConfig.LibraryRoot, runtimeConfig.RootPath);
			return Results.Ok(value);
		});
		endpoints.MapPost("/api/evolution/start", (Func<LearningStartRequest, ILearningCoordinator, CancellationToken, Task<IResult>>)(async ([FromBody] LearningStartRequest? req, ILearningCoordinator coordinator, CancellationToken ct) =>
		{
			await coordinator.StartEvolutionAsync(req, ct);
			return Results.Ok(new { success = true });
		}));
		endpoints.MapPost("/api/evolution/pause", (Func<ILearningCoordinator, CancellationToken, Task<IResult>>)async delegate(ILearningCoordinator coordinator, CancellationToken ct)
		{
			await coordinator.PauseEvolutionAsync(ct);
			return Results.Ok(new { success = true });
		});
		endpoints.MapPost("/api/evolution/stop", (Func<ILearningCoordinator, CancellationToken, Task<IResult>>)async delegate(ILearningCoordinator coordinator, CancellationToken ct)
		{
			await coordinator.StopEvolutionAsync(ct);
			return Results.Ok(new { success = true });
		});
		endpoints.MapPost("/api/evolution/reset", (Func<ILearningCoordinator, CancellationToken, Task<IResult>>)async delegate(ILearningCoordinator coordinator, CancellationToken ct)
		{
			await coordinator.ResetEvolutionAsync(ct);
			return Results.Ok(new { success = true });
		});
		endpoints.MapPost("/api/indexing/start", (Func<LearningStartRequest, ILearningCoordinator, CancellationToken, Task<IResult>>)(async ([FromBody] LearningStartRequest? req, ILearningCoordinator coordinator, CancellationToken ct) =>
		{
			await coordinator.StartIndexingAsync(req, ct);
			return Results.Ok(new { success = true });
		}));
		endpoints.MapPost("/api/indexing/pause", (Func<ILearningCoordinator, CancellationToken, Task<IResult>>)async delegate(ILearningCoordinator coordinator, CancellationToken ct)
		{
			await coordinator.PauseIndexingAsync(ct);
			return Results.Ok(new { success = true });
		});
		endpoints.MapPost("/api/indexing/reset", (Func<ILearningCoordinator, CancellationToken, Task<IResult>>)async delegate(ILearningCoordinator coordinator, CancellationToken ct)
		{
			await coordinator.ResetIndexingAsync(ct);
			return Results.Ok(new { success = true });
		});
	}
}

