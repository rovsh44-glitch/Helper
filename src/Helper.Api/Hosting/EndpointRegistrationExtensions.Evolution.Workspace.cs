#nullable enable
// Minimal API delegate signatures still trigger a narrow set of Roslyn nullability mismatches here.
#pragma warning disable CS8600, CS8619, CS8622

using System.Text.Json;
using Helper.Runtime.Core;
using Helper.Runtime.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Helper.Api.Hosting;

public static partial class EndpointRegistrationExtensions
{
	private static void MapWorkspaceEndpoints(IEndpointRouteBuilder endpoints)
	{
		endpoints.MapPost("/api/fs/write", (Func<FileWriteRequestDto, IFileSystemGuard, ISafetyGuard, CancellationToken, Task<IResult>>)(async ([FromBody] FileWriteRequestDto dto, IFileSystemGuard guard, ISafetyGuard safety, CancellationToken ct) =>
		{
			if (string.IsNullOrWhiteSpace(dto.Path))
			{
				return Results.BadRequest(new { success = false, error = "Path is required." });
			}
			try
			{
				guard.EnsureSafePath(dto.Path);
			}
			catch (UnauthorizedAccessException ex)
			{
				return Results.Json(new { success = false, error = ex.Message }, (JsonSerializerOptions?)null, (string?)null, (int?)403);
			}
			if (!(await safety.ValidateOperationAsync("WRITE", dto.Path, dto.Content ?? string.Empty)))
			{
				return Results.Json(new { success = false, error = "Safety guard rejected modification." }, (JsonSerializerOptions?)null, (string?)null, (int?)403);
			}
			string fullPath = Path.GetFullPath(dto.Path);
			string? directory = Path.GetDirectoryName(fullPath);
			if (!string.IsNullOrWhiteSpace(directory))
			{
				Directory.CreateDirectory(directory);
			}
			await File.WriteAllTextAsync(fullPath, dto.Content ?? string.Empty, ct);
			return Results.Ok(new { success = true });
		}));

		endpoints.MapPost("/api/fs/write-relative", (Func<FileWriteRequestDto, IFileSystemGuard, ISafetyGuard, CancellationToken, Task<IResult>>)(async ([FromBody] FileWriteRequestDto dto, IFileSystemGuard guard, ISafetyGuard safety, CancellationToken ct) =>
		{
			if (string.IsNullOrWhiteSpace(dto.Path))
			{
				return Results.BadRequest(new { success = false, error = "Path is required." });
			}
			if (Path.IsPathRooted(dto.Path))
			{
				return Results.BadRequest(new { success = false, error = "Path must be relative to workspace root." });
			}
			string fullPath;
			try
			{
				fullPath = guard.GetFullPath(dto.Path);
			}
			catch (UnauthorizedAccessException ex)
			{
				return Results.Json(new { success = false, error = ex.Message }, (JsonSerializerOptions?)null, (string?)null, (int?)403);
			}
			if (!(await safety.ValidateOperationAsync("WRITE", fullPath, dto.Content ?? string.Empty)))
			{
				return Results.Json(new { success = false, error = "Safety guard rejected modification." }, (JsonSerializerOptions?)null, (string?)null, (int?)403);
			}
			string? directory = Path.GetDirectoryName(fullPath);
			if (!string.IsNullOrWhiteSpace(directory))
			{
				Directory.CreateDirectory(directory);
			}
			await File.WriteAllTextAsync(fullPath, dto.Content ?? string.Empty, ct);
			return Results.Ok(new { success = true, path = fullPath });
		}));

		endpoints.MapPost("/api/workspace/open", (Func<WorkspaceProjectRequestDto, IFileSystemGuard, IResult>)(([FromBody] WorkspaceProjectRequestDto dto, IFileSystemGuard guard) =>
		{
			if (string.IsNullOrWhiteSpace(dto.ProjectPath))
			{
				return Results.BadRequest(new { success = false, error = "ProjectPath is required." });
			}
			try
			{
				string projectRoot = WorkspacePathAccess.ResolveProjectRoot(dto.ProjectPath, guard);
				return Results.Ok(new { success = true, project = WorkspaceTreeBuilder.BuildProject(projectRoot) });
			}
			catch (DirectoryNotFoundException ex)
			{
				return Results.NotFound(new { success = false, error = ex.Message });
			}
			catch (UnauthorizedAccessException ex)
			{
				return Results.Json(new { success = false, error = ex.Message }, (JsonSerializerOptions?)null, (string?)null, (int?)403);
			}
		}));

		endpoints.MapPost("/api/workspace/file/read", (Func<WorkspaceNodeRequestDto, IFileSystemGuard, CancellationToken, Task<IResult>>)(async ([FromBody] WorkspaceNodeRequestDto dto, IFileSystemGuard guard, CancellationToken ct) =>
		{
			if (string.IsNullOrWhiteSpace(dto.ProjectPath) || string.IsNullOrWhiteSpace(dto.RelativePath))
			{
				return Results.BadRequest(new { success = false, error = "ProjectPath and RelativePath are required." });
			}
			try
			{
				string projectRoot = WorkspacePathAccess.ResolveProjectRoot(dto.ProjectPath, guard);
				string filePath = WorkspacePathAccess.ResolveUnderProject(projectRoot, dto.RelativePath);
				if (!File.Exists(filePath))
				{
					return Results.NotFound(new { success = false, error = "Workspace file not found." });
				}
				return Results.Ok(new { success = true, path = WorkspacePathAccess.GetRelativePath(projectRoot, filePath), content = await File.ReadAllTextAsync(filePath, ct) });
			}
			catch (UnauthorizedAccessException ex)
			{
				return Results.Json(new { success = false, error = ex.Message }, (JsonSerializerOptions?)null, (string?)null, (int?)403);
			}
		}));

		endpoints.MapPost("/api/workspace/node/create", (Func<WorkspaceCreateRequestDto, IFileSystemGuard, ISafetyGuard, CancellationToken, Task<IResult>>)(async ([FromBody] WorkspaceCreateRequestDto dto, IFileSystemGuard guard, ISafetyGuard safety, CancellationToken ct) =>
		{
			if (string.IsNullOrWhiteSpace(dto.ProjectPath) || string.IsNullOrWhiteSpace(dto.Name))
			{
				return Results.BadRequest(new { success = false, error = "ProjectPath and Name are required." });
			}
			try
			{
				string projectRoot = WorkspacePathAccess.ResolveProjectRoot(dto.ProjectPath, guard);
				string parentDirectory = WorkspacePathAccess.ResolveUnderProject(projectRoot, dto.ParentRelativePath);
				if (!Directory.Exists(parentDirectory))
				{
					return Results.NotFound(new { success = false, error = "Parent directory not found." });
				}
				string targetPath = Path.GetFullPath(Path.Combine(parentDirectory, dto.Name));
				guard.EnsureSafePath(targetPath);
				if (!HelperWorkspacePathResolver.IsPathUnderRoot(targetPath, projectRoot))
				{
					return Results.Json(new { success = false, error = "Workspace node escapes the selected project root." }, (JsonSerializerOptions?)null, (string?)null, (int?)403);
				}
				if (!(await safety.ValidateOperationAsync("WRITE", targetPath, string.Empty)))
				{
					return Results.Json(new { success = false, error = "Safety guard rejected workspace mutation." }, (JsonSerializerOptions?)null, (string?)null, (int?)403);
				}
				if (Directory.Exists(targetPath) || File.Exists(targetPath))
				{
					return Results.Conflict(new { success = false, error = "Workspace node already exists." });
				}
				if (dto.IsFolder)
				{
					Directory.CreateDirectory(targetPath);
				}
				else
				{
					await File.WriteAllTextAsync(targetPath, string.Empty, ct);
				}
				return Results.Ok(new { success = true, path = WorkspacePathAccess.GetRelativePath(projectRoot, targetPath) });
			}
			catch (UnauthorizedAccessException ex)
			{
				return Results.Json(new { success = false, error = ex.Message }, (JsonSerializerOptions?)null, (string?)null, (int?)403);
			}
		}));

		endpoints.MapPost("/api/workspace/node/rename", (Func<WorkspaceRenameRequestDto, IFileSystemGuard, ISafetyGuard, CancellationToken, Task<IResult>>)(async ([FromBody] WorkspaceRenameRequestDto dto, IFileSystemGuard guard, ISafetyGuard safety, CancellationToken ct) =>
		{
			if (string.IsNullOrWhiteSpace(dto.ProjectPath) || string.IsNullOrWhiteSpace(dto.RelativePath) || string.IsNullOrWhiteSpace(dto.NewName))
			{
				return Results.BadRequest(new { success = false, error = "ProjectPath, RelativePath, and NewName are required." });
			}
			try
			{
				string projectRoot = WorkspacePathAccess.ResolveProjectRoot(dto.ProjectPath, guard);
				string currentPath = WorkspacePathAccess.ResolveUnderProject(projectRoot, dto.RelativePath);
				if (string.Equals(currentPath, projectRoot, StringComparison.OrdinalIgnoreCase))
				{
					return Results.BadRequest(new { success = false, error = "Project root cannot be renamed from the workspace API." });
				}
				bool isDirectory = Directory.Exists(currentPath);
				bool isFile = File.Exists(currentPath);
				if (!isDirectory && !isFile)
				{
					return Results.NotFound(new { success = false, error = "Workspace node not found." });
				}
				string parentDirectory = Path.GetDirectoryName(currentPath) ?? projectRoot;
				string renamedPath = Path.GetFullPath(Path.Combine(parentDirectory, dto.NewName));
				guard.EnsureSafePath(renamedPath);
				if (!(await safety.ValidateOperationAsync("WRITE", renamedPath, string.Empty)))
				{
					return Results.Json(new { success = false, error = "Safety guard rejected workspace rename." }, (JsonSerializerOptions?)null, (string?)null, (int?)403);
				}
				if (Directory.Exists(renamedPath) || File.Exists(renamedPath))
				{
					return Results.Conflict(new { success = false, error = "A node with the requested name already exists." });
				}
				if (isDirectory)
				{
					Directory.Move(currentPath, renamedPath);
				}
				else
				{
					File.Move(currentPath, renamedPath);
				}
				return Results.Ok(new { success = true, path = WorkspacePathAccess.GetRelativePath(projectRoot, renamedPath) });
			}
			catch (UnauthorizedAccessException ex)
			{
				return Results.Json(new { success = false, error = ex.Message }, (JsonSerializerOptions?)null, (string?)null, (int?)403);
			}
		}));

		endpoints.MapPost("/api/workspace/node/delete", (Func<WorkspaceDeleteRequestDto, IFileSystemGuard, ISafetyGuard, CancellationToken, Task<IResult>>)(async ([FromBody] WorkspaceDeleteRequestDto dto, IFileSystemGuard guard, ISafetyGuard safety, CancellationToken ct) =>
		{
			if (string.IsNullOrWhiteSpace(dto.ProjectPath) || string.IsNullOrWhiteSpace(dto.RelativePath))
			{
				return Results.BadRequest(new { success = false, error = "ProjectPath and RelativePath are required." });
			}
			try
			{
				string projectRoot = WorkspacePathAccess.ResolveProjectRoot(dto.ProjectPath, guard);
				string targetPath = WorkspacePathAccess.ResolveUnderProject(projectRoot, dto.RelativePath);
				if (string.Equals(targetPath, projectRoot, StringComparison.OrdinalIgnoreCase))
				{
					return Results.BadRequest(new { success = false, error = "Project root cannot be deleted from the workspace API." });
				}
				if (!(await safety.ValidateOperationAsync("WRITE", targetPath, string.Empty)))
				{
					return Results.Json(new { success = false, error = "Safety guard rejected workspace delete." }, (JsonSerializerOptions?)null, (string?)null, (int?)403);
				}
				if (Directory.Exists(targetPath))
				{
					Directory.Delete(targetPath, recursive: true);
				}
				else if (File.Exists(targetPath))
				{
					File.Delete(targetPath);
				}
				else
				{
					return Results.NotFound(new { success = false, error = "Workspace node not found." });
				}
				await Task.CompletedTask;
				return Results.Ok(new { success = true });
			}
			catch (UnauthorizedAccessException ex)
			{
				return Results.Json(new { success = false, error = ex.Message }, (JsonSerializerOptions?)null, (string?)null, (int?)403);
			}
		}));
	}
}

