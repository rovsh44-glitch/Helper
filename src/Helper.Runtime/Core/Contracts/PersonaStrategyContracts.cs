using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Helper.Runtime.Core
{
    public record CritiqueResult(bool IsApproved, string Feedback, string? CorrectedContent);

    public enum PersonaType { Cynic, Emergent, Historian, Assistant }

    public record PersonaOpinion(PersonaType Persona, string Opinion, string AlternativeProposal, double CriticalScore);

    public record ShadowRoundtableReport(
        string OriginalProposal,
        List<PersonaOpinion> Opinions,
        string SynthesizedAdvice,
        double ConflictLevel,
        int TokensUsed = 0);

    public interface IPersonaOrchestrator
    {
        Task<ShadowRoundtableReport> ConductRoundtableAsync(string proposal, CancellationToken ct = default);
    }

    public interface IKnowledgePruner
    {
        Task<int> PruneDeadKnowledgeAsync(string collection, int thresholdDays = 30, CancellationToken ct = default);
    }

    public interface ICriticService
    {
        Task<CritiqueResult> AnalyzeAsync(string content, CancellationToken ct = default);
        Task<CritiqueResult> CritiqueAsync(string sourceData, string draft, string context, CancellationToken ct = default);
        Task<ShadowRoundtableReport> ChallengeAsync(string proposal, CancellationToken ct = default);
    }
    
    public record Goal(Guid Id, string Title, string Description, bool IsCompleted, DateTime CreatedAt);

    public record MutationProposal(
        Guid Id, 
        string FilePath, 
        string OriginalCode, 
        string ProposedCode, 
        string Reason, 
        DateTime Timestamp);

    public record HealthStatus(bool IsHealthy, List<string> Issues, double ErrorRate, double CurrentVramAvailableGb = 0);
    public record TestReport(bool AllPassed, int Passed, int Failed, List<string> Logs);

    public record StrategyBranch(string Id, string Description, double ConfidenceScore, List<string> Risks, List<string>? SuggestedTools = null);
    public record StrategicPlan(
        string SelectedStrategyId, 
        List<StrategyBranch> Options, 
        string Reasoning, 
        bool RequiresMoreInfo,
        List<string>? ClarifyingQuestions = null);

    public record WebSearchResult(string Url, string Title, string Content, bool IsDeepScan = false);

    public enum IntentType { Generate, Research, Ingest, Unknown }
    public record IntentAnalysis(IntentType Intent, string Model, string? TargetPath = null);

    // --- Personality & Drift Protection Models ---

    public record PersonalityProfile(
        string Id,
        string Name,
        string SystemInstruction,
        float[]? BaselineEmbedding = null,
        Dictionary<string, string>? Traits = null);

    public record DriftAudit(
        double Similarity, 
        bool IsDriftDetected, 
        string CurrentPersonaId,
        DateTime Timestamp);

    // --- Swarm & Forge Models (Consolidated) ---

}


