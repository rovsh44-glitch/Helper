#nullable enable
// Minimal API delegate signatures still trigger a narrow set of Roslyn nullability mismatches here.
#pragma warning disable CS8600, CS8619, CS8622

using System.Text.Json;
using Helper.Api.Conversation;
using Helper.Runtime.Core;
using Helper.Runtime.Generation;
using Helper.Runtime.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace Helper.Api.Hosting;

public static partial class EndpointRegistrationExtensions
{
	private static void MapGenerationTemplateAndBuildEndpoints(IEndpointRouteBuilder endpoints, ApiRuntimeConfig runtimeConfig)
	{
		var generateHandler = (Func<GenerationRequestDto, IHelperOrchestrator, IFileSystemGuard, ISafetyGuard, IHubContext<HelperHub>, Task<IResult>>)(async ([FromBody] GenerationRequestDto dto, IHelperOrchestrator orchestrator, IFileSystemGuard guard, ISafetyGuard safety, IHubContext<HelperHub> hub) =>
		{
			if (string.IsNullOrWhiteSpace(dto.Prompt))
			{
				return Results.BadRequest(new { success = false, error = "Prompt is required." });
			}
			string outputDir = dto.OutputPath ?? Path.Combine(runtimeConfig.ProjectsRoot, Guid.NewGuid().ToString("N").Substring(0, 8));
			try
			{
				guard.EnsureSafePath(outputDir);
			}
			catch (UnauthorizedAccessException ex)
			{
				return Results.Json(new { success = false, error = ex.Message }, (JsonSerializerOptions?)null, (string?)null, (int?)403);
			}
			if (!(await safety.ValidateOperationAsync("GENERATE", outputDir, dto.Prompt)))
			{
				return Results.Json(new { success = false, error = "Safety guard rejected generation request." }, (JsonSerializerOptions?)null, (string?)null, (int?)403);
			}
			GenerationRequest request = new(dto.Prompt, outputDir);
			Action<string> onProgress = message => { _ = hub.Clients.All.SendAsync("ReceiveProgress", message, CancellationToken.None); };
			return Results.Ok(await orchestrator.GenerateProjectAsync(request, true, onProgress));
		});
		endpoints.MapPost("/api/helper/generate", generateHandler);

		endpoints.MapGet("/api/templates", (Func<ITemplateManager, CancellationToken, Task<IResult>>)(async (ITemplateManager templateManager, CancellationToken ct) => Results.Ok((await templateManager.GetAvailableTemplatesAsync(ct)).Select((ProjectTemplate t) => new
		{
			id = t.Id,
			name = t.Name,
			description = t.Description,
			language = t.Language,
			version = t.Version,
			deprecated = t.Deprecated,
			tags = t.Tags ?? Array.Empty<string>(),
			rootPath = t.RootPath
		}))));
		endpoints.MapGet("/api/templates/promotion-profile", (Func<ITemplatePromotionFeatureProfileService, IResult>)delegate(ITemplatePromotionFeatureProfileService profileService)
		{
			TemplatePromotionFeatureProfile current = profileService.GetCurrent();
			return Results.Ok(new
			{
				runtimePromotionEnabled = current.RuntimePromotionEnabled,
				autoActivateEnabled = current.AutoActivateEnabled,
				postActivationFullRecertifyEnabled = current.PostActivationFullRecertifyEnabled,
				formatMode = current.FormatMode.ToString(),
				routerV2Enabled = current.RouterV2Enabled,
				routerMinConfidence = current.RouterMinConfidence
			});
		});
		endpoints.MapGet("/api/templates/{templateId}/versions", (Func<string, ITemplateLifecycleService, CancellationToken, Task<IResult>>)async delegate(string templateId, ITemplateLifecycleService lifecycle, CancellationToken ct)
		{
			IReadOnlyList<TemplateVersionInfo> versions = await lifecycle.GetVersionsAsync(templateId, ct);
			return versions.Count == 0 ? Results.NotFound(new { success = false, error = "Template '" + templateId + "' not found or has no versions." }) : Results.Ok(new { success = true, templateId, versions });
		});
		endpoints.MapPost("/api/templates/{templateId}/activate/{version}", (Func<string, string, ITemplateLifecycleService, CancellationToken, Task<IResult>>)async delegate(string templateId, string version, ITemplateLifecycleService lifecycle, CancellationToken ct)
		{
			TemplateVersionActivationResult activation = await lifecycle.ActivateVersionAsync(templateId, version, ct);
			return activation.Success ? Results.Ok(new { success = true, templateId = activation.TemplateId, activeVersion = activation.ActiveVersion, message = activation.Message }) : Results.Json(new { success = false, templateId = activation.TemplateId, activeVersion = activation.ActiveVersion, error = activation.Message }, (JsonSerializerOptions?)null, (string?)null, (int?)400);
		});
		endpoints.MapPost("/api/templates/{templateId}/rollback", (Func<string, ITemplateLifecycleService, CancellationToken, Task<IResult>>)async delegate(string templateId, ITemplateLifecycleService lifecycle, CancellationToken ct)
		{
			TemplateVersionActivationResult rollback = await lifecycle.RollbackAsync(templateId, ct);
			return rollback.Success ? Results.Ok(new { success = true, templateId = rollback.TemplateId, activeVersion = rollback.ActiveVersion, message = rollback.Message }) : Results.Json(new { success = false, templateId = rollback.TemplateId, activeVersion = rollback.ActiveVersion, error = rollback.Message }, (JsonSerializerOptions?)null, (string?)null, (int?)400);
		});
		endpoints.MapPost("/api/templates/{templateId}/certify/{version}", (Func<string, string, string, ITemplateCertificationService, CancellationToken, Task<IResult>>)(async (string templateId, string version, [FromQuery] string? reportPath, ITemplateCertificationService certification, CancellationToken ct) =>
		{
			TemplateCertificationReport report = await certification.CertifyAsync(templateId, version, reportPath, null, ct);
			return Results.Ok(new
			{
				success = report.Passed,
				TemplateId = report.TemplateId,
				Version = report.Version,
				TemplatePath = report.TemplatePath,
				MetadataSchemaPassed = report.MetadataSchemaPassed,
				CompileGatePassed = report.CompileGatePassed,
				ArtifactValidationPassed = report.ArtifactValidationPassed,
				SmokePassed = report.SmokePassed,
				Passed = report.Passed,
				Errors = report.Errors,
				ReportPath = report.ReportPath
			});
		}));
		endpoints.MapPost("/api/templates/certification-gate", (Func<string, ITemplateCertificationService, CancellationToken, Task<IResult>>)(async ([FromQuery] string? reportPath, ITemplateCertificationService certification, CancellationToken ct) =>
		{
			TemplateCertificationGateReport gate = await certification.EvaluateGateAsync(reportPath, ct);
			return Results.Ok(new { success = gate.Passed, ReportPath = gate.ReportPath, CertifiedCount = gate.CertifiedCount, FailedCount = gate.FailedCount, Violations = gate.Violations });
		}));
		endpoints.MapPost("/api/build", (Func<BuildRequestDto, IDotnetService, IFileSystemGuard, ISafetyGuard, CancellationToken, Task<IResult>>)(async ([FromBody] BuildRequestDto dto, IDotnetService dotnet, IFileSystemGuard guard, ISafetyGuard safety, CancellationToken ct) =>
		{
			try
			{
				guard.EnsureSafePath(dto.ProjectPath);
			}
			catch (UnauthorizedAccessException ex)
			{
				return Results.Json(new { success = false, error = ex.Message }, (JsonSerializerOptions?)null, (string?)null, (int?)403);
			}
			if (!(await safety.ValidateOperationAsync("BUILD", dto.ProjectPath)))
			{
				return Results.Json(new { success = false, error = "Safety guard rejected build request." }, (JsonSerializerOptions?)null, (string?)null, (int?)403);
			}
			List<BuildError> errors = await dotnet.BuildAsync(dto.ProjectPath, allowRecursiveDiscovery: true, ct);
			return Results.Ok(new { success = errors.Count == 0, errors });
		}));
	}
}

