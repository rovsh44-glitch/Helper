using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Helper.Runtime.Core
{
    public interface ICodeSanitizer
    {
        string Sanitize(string input, string language = "csharp");
        string FixCsproj(string xml);
    }

    public interface IVectorStore
    {
        Task UpsertAsync(KnowledgeChunk chunk, CancellationToken ct = default);
        Task<List<KnowledgeChunk>> SearchAsync(float[] queryEmbedding, string collection = HelperKnowledgeCollections.CanonicalDefault, int limit = 5, CancellationToken ct = default);
        Task<List<KnowledgeChunk>> SearchMetadataAsync(string key, string value, string collection, CancellationToken ct = default);
        Task<List<KnowledgeChunk>> ScrollMetadataAsync(string collection, int limit = 100, string? offset = null, CancellationToken ct = default);
        Task EnsureCollectionExistsAsync(string name, int dimensions, CancellationToken ct = default);
        Task DeletePointsAsync(List<string> ids, string collection, CancellationToken ct = default);
        Task DeleteByMetadataAsync(string key, string value, string collection, CancellationToken ct = default);
        Task DeleteCollectionAsync(string collection, CancellationToken ct = default);
    }

    public record EngineeringLesson(
        string ErrorPattern, 
        string Context, 
        string Solution, 
        string Principle,
        DateTime CreatedAt);

    public interface IReflectionService
    {
        Task<EngineeringLesson?> ConductPostMortemAsync(string originalPrompt, List<BuildError> errors, string finalCode, CancellationToken ct = default);
        Task<EngineeringLesson?> ConductSuccessReviewAsync(string originalPrompt, string finalCode, CancellationToken ct = default);
        Task IngestLessonAsync(EngineeringLesson lesson, CancellationToken ct = default);
        Task<List<EngineeringLesson>> SearchLessonsAsync(string context, int limit = 3, CancellationToken ct = default);
    }

    public enum TaskStatus { Pending, InProgress, Completed, Failed, Blocked }

    public record TaskNode(
        string Id, 
        string Name, 
        string Description, 
        string AgentRole, 
        List<string> Dependencies,
        TaskStatus Status = TaskStatus.Pending,
        string? Result = null);

    public record TaskGraph(List<TaskNode> Nodes, string Objective);

    public interface IGraphOrchestrator
    {
        Task<GenerationResult> ExecuteGraphAsync(string prompt, string outputPath, Action<string>? onProgress = null, CancellationToken ct = default);
    }

    public interface IVisualInspector
    {
        Task<bool> InspectProjectAsync(string projectPath, Action<string>? onProgress = null, CancellationToken ct = default);
        Task<bool> InspectProjectScreenshotAsync(string imagePath, Action<string>? onProgress = null, CancellationToken ct = default);
    }

    public interface IContextDistiller
    {
        string DistillCode(string code);
        string DistillPrompt(string prompt);
    }

    public record ToolDefinition(string Name, string Description, Dictionary<string, string> Parameters);
    public record ToolExecutionResult(bool Success, string Output, string? Error = null);

    // --- MCP (Model Context Protocol) Models ---
    public record McpTool(string Name, string Description, object InputSchema);
    public record McpRequest(string Jsonrpc, string Method, Dictionary<string, object>? Params, string Id);
    public record McpResponse(string Jsonrpc, string Id, object? Result, object? Error);

    public interface IToolService
    {
        Task<List<ToolDefinition>> GetAvailableToolsAsync(CancellationToken ct = default);
        Task<ToolExecutionResult> ExecuteToolAsync(string name, Dictionary<string, object> arguments, CancellationToken ct = default);
    }

    public interface ITemplateGeneralizer
    {
        Task<ProjectTemplate?> GeneralizeProjectAsync(string projectPath, string targetTemplateId, CancellationToken ct = default);
    }

    public enum TaskComplexity { Simple, Standard, Hard, Reasoning, Visual }

    public interface IComplexityAnalyzer
    {
        Task<TaskComplexity> AnalyzeComplexityAsync(string taskDescription, CancellationToken ct = default);
    }

    public interface IGoalManager
    {
        Task<List<Goal>> GetGoalsAsync(bool includeCompleted = true, CancellationToken ct = default);
        Task<List<Goal>> GetActiveGoalsAsync(CancellationToken ct = default);
        Task AddGoalAsync(string title, string description, CancellationToken ct = default);
        Task<bool> UpdateGoalAsync(Guid id, string title, string description, CancellationToken ct = default);
        Task<bool> DeleteGoalAsync(Guid id, CancellationToken ct = default);
        Task<bool> MarkGoalCompletedAsync(Guid id, CancellationToken ct = default);
    }

    public interface IFileSystemGuard
    {
        string GetFullPath(string relativePath);
        void EnsureSafePath(string path);
    }

    public interface IProcessGuard
    {
        void EnsureSafeCommand(string command, string? workingDir = null, List<Goal>? activeGoals = null);
        Task EnsureSafeCommandAsync(string command, string? workingDir = null, List<Goal>? activeGoals = null, CancellationToken ct = default);
    }

    public interface IDocumentParser
    {
        bool CanParse(string extension);
        Task<string> ParseAsync(string filePath, CancellationToken ct = default);
        // Added for massive files
        Task ParseStreamingAsync(string filePath, Func<string, Task> onChunk, CancellationToken ct = default);
    }

    public interface IStructuredDocumentParser
    {
        bool CanParse(string extension);
        string ParserVersion { get; }
        Task<DocumentParseResult> ParseStructuredAsync(string filePath, Func<double, Task>? onProgress = null, CancellationToken ct = default);
    }

    public interface IDocumentNormalizer
    {
        Task<DocumentParseResult> NormalizeAsync(DocumentParseResult document, CancellationToken ct = default);
    }

    public interface IStructureRecoveryService
    {
        Task<DocumentParseResult> RecoverAsync(DocumentParseResult document, CancellationToken ct = default);
    }

    public interface IChunkingStrategyResolver
    {
        Task<ChunkPlan> ResolveAsync(DocumentParseResult document, string domain, CancellationToken ct = default);
    }

    public interface IChunkBuilder
    {
        bool CanBuild(ChunkStrategyType strategy);
        Task<IReadOnlyList<StructuredChunk>> BuildChunksAsync(DocumentParseResult document, ChunkPlan plan, CancellationToken ct = default);
    }

    public interface IStructuredVectorStore
    {
        Task<IReadOnlyList<string>> ListCollectionsAsync(string? prefix = null, CancellationToken ct = default);
        Task<int?> GetCollectionPointCountAsync(string collection, CancellationToken ct = default);
        Task<List<KnowledgeChunk>> SearchByMetadataAsync(IReadOnlyDictionary<string, string> filters, string collection, int limit = 10, CancellationToken ct = default);
        Task<KnowledgeChunk?> GetChunkByChunkIdAsync(string collection, string chunkId, CancellationToken ct = default);
        Task<IReadOnlyList<KnowledgeChunk>> GetChunksByChunkIdsAsync(string collection, IEnumerable<string> chunkIds, CancellationToken ct = default);
        Task<IReadOnlyList<KnowledgeChunk>> GetDocumentLocalGroupAsync(string collection, string documentId, string? sectionPath = null, int? pageStart = null, int limit = 8, CancellationToken ct = default);
    }

    public interface IIndexingTelemetrySink
    {
        void Report(IndexingTelemetry telemetry);
        IndexingTelemetry Snapshot();
        void Reset();
    }

    public interface IWebSearcher
    {
        Task<List<WebSearchResult>> SearchAsync(string query, CancellationToken ct = default);
    }

    public interface ILibrarianAgent
    {
        Task<string> IndexFileAsync(string filePath, Func<double, Task>? onProgress = null, CancellationToken ct = default);
        Task CleanupFileArtifactsAsync(string filePath, CancellationToken ct = default);
    }

    public interface IStrategicPlanner
    {
        Task<StrategicPlan> PlanStrategyAsync(string task, string availableContext, CancellationToken ct = default);
    }

    public interface IHealthMonitor
    {
        Task<HealthStatus> DiagnoseAsync(CancellationToken ct = default);
    }

    public interface IRecursiveTester
    {
        Task<TestReport> RunSelfTestsAsync(string componentName, CancellationToken ct = default);
        Task<GeneratedFile> GenerateTestForComponentAsync(string sourceCode, string componentName, CancellationToken ct = default);
    }

    public interface IEvolutionEngine
    {
        Task<MutationProposal?> ProposeEvolutionAsync(HealthStatus status, CancellationToken ct = default);
        Task<bool> ApplyMutationAsync(MutationProposal mutation, CancellationToken ct = default);
    }

    public interface ISurgeonAgent
    {
        Task<bool> EvolveSelfAsync(string relativePath, string instruction, bool commit = false, CancellationToken ct = default);
    }

    public interface IResearchEngine
    {
        Task<GenerationResult> HandleResearchModeAsync(GenerationRequest request, IntentAnalysis analysis, Action<string>? onProgress, CancellationToken ct);
        Task<ResearchResult> ConductResearchAsync(string topic, int depth = 1, Action<string>? onProgress = null, CancellationToken ct = default);
    }

    public interface IMaintenanceService
    {
        Task RunPrometheusLoopAsync(CancellationToken ct, Func<string, Task>? onThought = null);
        Task ConsolidateMemoryAsync(CancellationToken ct = default);
    }

    public interface IHelperOrchestrator
    {
        IProjectForgeOrchestrator Forge { get; }
        Task<GenerationResult> GenerateProjectAsync(
            GenerationRequest request, 
            bool includeTests = true, 
            Action<string>? onProgress = null,
            CancellationToken ct = default);

        Task<ResearchResult> ConductResearchAsync(string topic, int depth = 1, Action<string>? onProgress = null, CancellationToken ct = default);
        
        Task<string> DeployProjectAsync(string projectPath, string platform, Action<string>? onProgress = null, CancellationToken ct = default);
        
        // Prometheus Control
        Task RunPrometheusLoopAsync(CancellationToken ct, Func<string, Task>? onThought = null);
        Task ConsolidateMemoryAsync(CancellationToken ct = default);
    }

}


