using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Helper.Runtime.Core
{
    public record GeneratedFile(string RelativePath, string Content, string Language = "csharp");
    
    public record GenerationRequest(string Prompt, string OutputPath, Dictionary<string, string>? Metadata = null, string? SessionId = null);
    
    public record ProjectPlan(string Description, List<FileTask> PlannedFiles);

    public record DebateResult(string Summary, string FinalConsensus, List<string> Arguments);
    
    public record FileTask(string Path, string Purpose, List<string> Dependencies, string? TechnicalContract = null);
    
    public record BuildError(string File, int Line, string Code, string Message);
    
    public record GenerationResult(
        bool Success, 
        List<GeneratedFile> Files, 
        string ProjectPath, 
        List<BuildError> Errors,
        TimeSpan Duration,
        int HealAttempts = 0,
        bool IsResearch = false,
        IReadOnlyList<FailureEnvelope>? FailureEnvelopes = null);

    public enum FailureStage
    {
        Routing,
        Forge,
        Synthesis,
        Autofix,
        Build,
        Tooling,
        Unknown
    }

    public enum RootCauseClass
    {
        Timeout,
        Validation,
        Compilation,
        Permission,
        Dependency,
        ExternalService,
        Runtime,
        Unknown
    }

    public record FailureEnvelope(
        FailureStage Stage,
        string Subsystem,
        string ErrorCode,
        RootCauseClass RootCauseClass,
        bool Retryable,
        string UserAction,
        string Evidence,
        string CorrelationId);

    // --- Chat & UI Models ---
}


