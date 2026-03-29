using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Helper.Runtime.Core
{
    public record GrepResult(string FilePath, int LineNumber, string Content);
    public record ReplaceRequest(string FilePath, string OldText, string NewString, int ContextLines = 3);

    public interface ISurgicalToolbox
    {
        Task<List<GrepResult>> GrepAsync(string directory, string pattern, string include = "*.cs", CancellationToken ct = default);
        Task<bool> ReplaceAsync(ReplaceRequest request, CancellationToken ct = default);
        Task<string> GetDirectoryTreeAsync(string path, int depth = 3, CancellationToken ct = default);
    }

    public enum OSPlatform { Windows, MacOS, Linux }
    public record PlatformCapabilities(OSPlatform OS, char Slash, string PreferredUI, string PreferredShell, List<string> ForbiddenTech);

    public interface IPlatformGuard
    {
        PlatformCapabilities DetectPlatform();
        void ValidateTechStack(string tech, OSPlatform targetOS);
    }

    public record ProjectBlueprint(
        string Name,
        OSPlatform TargetOS,
        List<SwarmFileDefinition> Files,
        List<string> NuGetPackages,
        string ArchitectureReasoning);

    public interface IBlueprintEngine
    {
        Task<ProjectBlueprint> DesignBlueprintAsync(string prompt, OSPlatform targetOS, CancellationToken ct = default);
        Task<bool> ValidateBlueprintAsync(ProjectBlueprint blueprint, CancellationToken ct = default);
    }

    public record ExecutionResult(
        bool Success,
        string Output,
        string Error,
        List<string> GeneratedArtifacts);
}


