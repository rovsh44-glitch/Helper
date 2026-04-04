using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Helper.Runtime.Core
{
    public record ProjectTemplate(
        string Id,
        string Name,
        string Description,
        string Language,
        string RootPath,
        IReadOnlyList<string>? Tags = null,
        string? Version = null,
        bool Deprecated = false,
        IReadOnlyList<string>? Capabilities = null,
        IReadOnlyList<string>? Constraints = null,
        string? ProjectType = null,
        string? Platform = null,
        bool Certified = false,
        bool HasCriticalAlerts = false);

    public enum TemplateAvailabilityState
    {
        Available,
        Missing,
        BlockedByCriticalAlerts,
        BlockedByCertificationRequirement
    }

    public record TemplateAvailabilityResolution(
        string TemplateId,
        ProjectTemplate? Template,
        string? TemplateRootPath,
        bool ExistsOnDisk,
        TemplateAvailabilityState State,
        string Reason,
        string? CertificationReportPath = null,
        IReadOnlyList<string>? CriticalAlerts = null);

    public record TemplateRoutingDecision(
        bool Matched,
        string? TemplateId,
        double Confidence,
        IReadOnlyList<string> Candidates,
        string Reason);

    public record TemplateVersionInfo(
        string Version,
        bool Deprecated,
        bool IsActive,
        string Path);

    public record TemplateVersionActivationResult(
        bool Success,
        string TemplateId,
        string? ActiveVersion,
        string Message);

    public record TemplateProcurementStrategy(
        string TemplateId, 
        string Language,
        List<string> Commands, 
        string VerificationCommand,
        string Description);

    public interface ITemplateManager
    {
        Task<List<ProjectTemplate>> GetAvailableTemplatesAsync(CancellationToken ct = default);
        Task<ProjectTemplate?> GetTemplateByIdAsync(string id, CancellationToken ct = default);
        Task<TemplateAvailabilityResolution> ResolveTemplateAvailabilityAsync(string id, CancellationToken ct = default);
        Task<string> CloneTemplateAsync(string templateId, string targetPath, CancellationToken ct = default);
    }

    public interface ITemplateFactory
    {
        Task<bool> ProcureTemplateAsync(string request, Action<string>? onProgress = null, CancellationToken ct = default);
    }

    public interface ITemplateRoutingService
    {
        Task<TemplateRoutingDecision> RouteAsync(string prompt, CancellationToken ct = default);
    }

    public interface ITemplateLifecycleService
    {
        Task<IReadOnlyList<TemplateVersionInfo>> GetVersionsAsync(string templateId, CancellationToken ct = default);
        Task<TemplateVersionActivationResult> ActivateVersionAsync(string templateId, string version, CancellationToken ct = default);
        Task<TemplateVersionActivationResult> RollbackAsync(string templateId, CancellationToken ct = default);
    }

    public interface IFailureEnvelopeFactory
    {
        IReadOnlyList<FailureEnvelope> FromBuildErrors(
            FailureStage stage,
            string subsystem,
            IReadOnlyList<BuildError> errors,
            string? correlationId = null);

        FailureEnvelope FromException(
            FailureStage stage,
            string subsystem,
            Exception exception,
            string? correlationId = null);
    }

    public record ParityCertificationReport(
        DateTimeOffset GeneratedAtUtc,
        string ReportPath,
        long TotalRuns,
        double GoldenHitRate,
        double GenerationSuccessRate,
        double P95ReadySeconds,
        double UnknownErrorRate,
        double ToolSuccessRatio,
        IReadOnlyList<string> Alerts,
        long GoldenAttempts = 0,
        long GoldenHits = 0,
        int MinGoldenAttemptsRequired = 0,
        bool GoldenSampleInsufficient = false);

    public interface IParityCertificationService
    {
        Task<ParityCertificationReport> GenerateAsync(string? reportPath = null, CancellationToken ct = default);
    }

    public record ForgeVerificationResult(bool Success, string Reason);

    public interface IForgeArtifactValidator
    {
        Task<ForgeVerificationResult> ValidateAsync(string projectPath, IReadOnlyList<BuildError> buildErrors, CancellationToken ct = default);
    }

    public interface IConstitutionGuard
    {
        Task<CritiqueResult> ValidateComplianceAsync(string response, string personaId, CancellationToken ct = default);
    }

}


