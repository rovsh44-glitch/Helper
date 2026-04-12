using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Helper.Runtime.Core
{
    public record SwarmNode(string Id, string BaseUrl, SystemTier Tier, double AvailableVramGb, DateTime LastSeen, bool IsLocal = false);
    public enum SystemTier { LowEnd, MidRange, HighEnd, Enterprise }
    public record SystemCapabilities(SystemTier Tier, double VramGb, double RamGb, int CpuCores, string GpuModel, bool IsDistributedReady);

    public interface ISwarmNodeManager
    {
        Task<List<SwarmNode>> GetActiveNodesAsync(CancellationToken ct = default);
        Task RegisterNodeAsync(SwarmNode node, CancellationToken ct = default);
        Task<SwarmNode?> SelectBestNodeAsync(TaskComplexity complexity, CancellationToken ct = default);
        Task BroadcastPulseAsync(CancellationToken ct = default);
    }

    public record EvolutionState(string TaskDescription, string CurrentPhase, List<string> Findings, bool IsActive);

    public enum LearningStatus { Idle, Running, Paused, Stopping }
    public record IndexingProgress(
        int TotalFiles,
        int ProcessedFiles,
        string CurrentFile,
        LearningStatus IndexingStatus,
        LearningStatus EvolutionStatus,
        double CurrentFileProgress = 0,
        string PipelineVersion = "v1",
        string? ChunkingStrategy = null,
        string? CurrentSection = null,
        int? CurrentPageStart = null,
        int? CurrentPageEnd = null,
        string? ParserVersion = null);

    public interface ISyntheticLearningService
    {
        Task RunAdvancedLearningCycleAsync(CancellationToken ct = default, Func<string, Task>? onThought = null);
        Task<EvolutionState> GetStateAsync();
        Task<IndexingProgress> GetProgressAsync();
        
        // Indexing Control
        Task StartIndexingAsync();
        Task PauseIndexingAsync();
        void SetTargetFile(string? filePath, bool singleFileOnly = false);
        
        // Evolution Control
        Task StartEvolutionAsync();
        Task PauseEvolutionAsync();
        
        Task StopLearningAsync();
        Task ToggleAsync(bool active);
        Task ResetAsync();
        Task CleanupStuckFilesAsync(); 
        void SetTargetDomain(string? domain);
    }

    public record EngineeringPrinciple(string Title, string Description, string Rationale, List<string> Examples);

    public interface IPhilosophyEngine
    {
        Task<List<EngineeringPrinciple>> DistillPrinciplesAsync(CancellationToken ct = default);
        Task PublishManifestoAsync(CancellationToken ct = default);
    }

    public interface IModelOrchestrator
    {
        Task<IntentAnalysis> AnalyzeIntentAsync(string prompt, CancellationToken ct = default);
        Task<string> SelectOptimalModelAsync(string prompt, CancellationToken ct = default);
    }

    public record ModelRoutingRequest(
        string Prompt,
        IntentType Intent = IntentType.Unknown,
        string ExecutionMode = "balanced",
        int ContextMessageCount = 0,
        int ApproximatePromptTokens = 0,
        bool RequiresVerification = false,
        bool HasAttachments = false);

    public record ModelRoutingDecision(
        string PreferredModel,
        string RouteKey,
        IReadOnlyList<string> Reasons);

    public interface IContextAwareModelOrchestrator
    {
        Task<ModelRoutingDecision> SelectRoutingDecisionAsync(ModelRoutingRequest request, CancellationToken ct = default);
    }

    public interface IDeployService
    {
        Task<string> PrepareDeploymentAsync(string projectPath, string platform, CancellationToken ct = default);
    }

    public interface ICodeExecutor
    {
        Task<ExecutionResult> ExecuteAsync(string code, string language = "python", CancellationToken ct = default);
    }

    public interface IResearchService
    {
        Task<ResearchResult> ResearchAsync(string topic, int depth = 1, Action<string>? onProgress = null, CancellationToken ct = default);
    }

    public interface IAutoHealer
    {
        Task<List<BuildError>> HealAsync(string projectPath, List<BuildError> initialErrors, Action<string>? onProgress = null, CancellationToken ct = default);
    }

    public interface ITestGenerator
    {
        Task<List<GeneratedFile>> GenerateTestsAsync(List<GeneratedFile> sourceFiles, CancellationToken ct = default);
    }

    public interface IProjectPlanner
    {
        Task<ProjectPlan> PlanProjectAsync(string prompt, CancellationToken ct = default);
    }

    public interface ICodeGenerator
    {
        Task<GeneratedFile> GenerateFileAsync(
            FileTask task, 
            ProjectPlan context, 
            List<GeneratedFile>? previousFiles = null, 
            CancellationToken ct = default);
    }

    public interface IBuildValidator
    {
        Task<List<BuildError>> ValidateAsync(string projectPath, CancellationToken ct = default);
    }

    public interface IBuildExecutor
    {
        Task<List<BuildError>> ExecuteBuildAsync(string workingDirectory, CancellationToken ct = default);
    }

    public interface IDotnetService
    {
        Task<List<BuildError>> BuildAsync(string workingDirectory, CancellationToken ct = default);
        Task<List<BuildError>> BuildAsync(string workingDirectory, bool allowRecursiveDiscovery, CancellationToken ct = default);
        Task<List<BuildError>> BuildAsync(string workingDirectory, string targetPath, CancellationToken ct = default);
        Task<TestReport> TestAsync(string workingDirectory, CancellationToken ct = default);
        Task RestoreAsync(string workingDirectory, CancellationToken ct = default);
    }

    // --- Offline Forge Interfaces ---

}


