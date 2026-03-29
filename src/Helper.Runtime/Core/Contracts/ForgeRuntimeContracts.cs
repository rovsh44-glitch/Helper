using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Helper.Runtime.Core
{
    public interface IProjectForgeOrchestrator
    {
        Task<GenerationResult> ForgeProjectAsync(
            string prompt,
            string templateId,
            Action<string>? onProgress = null,
            CancellationToken ct = default);
    }

    public record SystemSnapshot(string DirectoryTree, List<string> RecentErrors, PlatformCapabilities Platform);

    public interface IInternalObserver
    {
        Task<SystemSnapshot> CaptureSnapshotAsync(string workingDir, CancellationToken ct = default);
    }

    public interface IIntentBcaster
    {
        Task BroadcastIntentAsync(string action, string rationale, Action<string>? onProgress, CancellationToken ct = default);
    }

    public interface IAtomicOrchestrator
    {
        Task<GeneratedFile?> BuildAndValidateFileAsync(
            FileTask task,
            ProjectPlan context,
            List<GeneratedFile> existingFiles,
            SystemSnapshot snapshot,
            Action<string>? onProgress,
            CancellationToken ct = default);
    }

    public interface IMetacognitiveAgent
    {
        Task<bool> DebugSelfAsync(string failureReason, Action<string>? onProgress, CancellationToken ct = default);
    }

    public static class JsonDefaults
    {
        public static readonly System.Text.Json.JsonSerializerOptions Options = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            Converters = { new FileRoleJsonConverter(), new System.Text.Json.Serialization.JsonStringEnumConverter() }
        };
    }
}


