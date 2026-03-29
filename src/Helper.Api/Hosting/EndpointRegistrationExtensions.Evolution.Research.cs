#nullable enable
#pragma warning disable CS8600, CS8601, CS8602, CS8603, CS8604, CS8619, CS8622, CS8632

using System.Text.Json;
using Helper.Runtime.Core;
using Helper.Runtime.Infrastructure;
using Helper.Runtime.Knowledge;
using Microsoft.AspNetCore.Mvc;

namespace Helper.Api.Hosting;

public static partial class EndpointRegistrationExtensions
{
	private static void MapRagGoalAndResearchEndpoints(IEndpointRouteBuilder endpoints)
	{
		endpoints.MapPost("/api/rag/search", (Func<SearchRequestDto, IRetrievalContextAssembler, CancellationToken, Task<IResult>>)(async ([FromBody] SearchRequestDto dto, IRetrievalContextAssembler assembler, CancellationToken ct) =>
		{
			string pipelineVersion = KnowledgeCollectionNaming.NormalizePipelineVersion(dto.PipelineVersion ?? "v2");
			return Results.Ok(await assembler.AssembleAsync(
				dto.Query,
				dto.Domain,
				dto.Limit ?? 5,
				pipelineVersion,
				dto.IncludeContext.GetValueOrDefault(true),
				ct));
		}));
		endpoints.MapPost("/api/rag/ingest", (Func<RagIngestRequestDto, IVectorStore, AILink, ISafetyGuard, CancellationToken, Task<IResult>>)(async ([FromBody] RagIngestRequestDto dto, IVectorStore memory, AILink ai, ISafetyGuard safety, CancellationToken ct) =>
		{
			if (string.IsNullOrWhiteSpace(dto.Content))
			{
				return Results.BadRequest(new
				{
					success = false,
					error = "Content is required."
				});
			}
			if (!(await safety.ValidateOperationAsync("INGEST", HelperKnowledgeCollections.CanonicalIngestScope, dto.Content)))
			{
				return Results.Json(new
				{
					success = false,
					error = "Safety guard rejected ingest operation."
				}, (JsonSerializerOptions?)null, (string?)null, (int?)403);
			}
			KnowledgeChunk chunk = new(Embedding: await ai.EmbedAsync(dto.Content, ct), Id: Guid.NewGuid().ToString(), Content: dto.Content, Metadata: new Dictionary<string, string>
			{
				{ "title", string.IsNullOrWhiteSpace(dto.Title) ? "Untitled" : dto.Title },
				{ "source", "api_ingest" },
				{ "created_at", DateTime.UtcNow.ToString("O") }
			});
			await memory.UpsertAsync(chunk, ct);
			return Results.Ok(new { success = true });
		}));

		endpoints.MapGet("/api/goals", (Func<bool?, IGoalManager, Task<IResult>>)(async ([FromQuery] bool? includeCompleted, IGoalManager goals) =>
		{
			bool includeClosed = includeCompleted.GetValueOrDefault(false);
			return Results.Ok(await goals.GetGoalsAsync(includeClosed));
		}));
		endpoints.MapPost("/api/goals", (Func<AddGoalDto, IGoalManager, Task<IResult>>)(async ([FromBody] AddGoalDto dto, IGoalManager goals) =>
		{
			await goals.AddGoalAsync(dto.Title, dto.Description);
			return Results.Ok(new { success = true });
		}));
		endpoints.MapPut("/api/goals/{goalId:guid}", (Func<Guid, UpdateGoalDto, IGoalManager, Task<IResult>>)(async (Guid goalId, [FromBody] UpdateGoalDto dto, IGoalManager goals) =>
		{
			bool updated = await goals.UpdateGoalAsync(goalId, dto.Title, dto.Description);
			return updated ? Results.Ok(new { success = true, goalId }) : Results.NotFound(new { success = false, error = "Goal not found." });
		}));
		endpoints.MapPost("/api/goals/{goalId:guid}/complete", (Func<Guid, IGoalManager, Task<IResult>>)(async (Guid goalId, IGoalManager goals) =>
		{
			bool completed = await goals.MarkGoalCompletedAsync(goalId);
			return completed ? Results.Ok(new { success = true, goalId }) : Results.NotFound(new { success = false, error = "Goal not found." });
		}));
		endpoints.MapDelete("/api/goals/{goalId:guid}", (Func<Guid, IGoalManager, Task<IResult>>)(async (Guid goalId, IGoalManager goals) =>
		{
			bool deleted = await goals.DeleteGoalAsync(goalId);
			return deleted ? Results.Ok(new { success = true, goalId }) : Results.NotFound(new { success = false, error = "Goal not found." });
		}));

		var researchHandler = (Func<ResearchRequest, IResearchService, CancellationToken, Task<IResult>>)(async ([FromBody] ResearchRequest req, IResearchService research, CancellationToken ct) => Results.Ok(await research.ResearchAsync(req.Topic, 1, null, ct)));
		endpoints.MapPost("/api/helper/research", researchHandler);
	}
}

