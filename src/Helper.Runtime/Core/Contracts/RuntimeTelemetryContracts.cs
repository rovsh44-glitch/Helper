using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Helper.Runtime.Core
{
    public static class RouteTelemetryChannels
    {
        public const string Chat = "chat";
        public const string Generation = "generation";
    }

    public static class RouteTelemetryOperationKinds
    {
        public const string ChatTurn = "chat_turn";
        public const string TemplateRouting = "template_routing";
        public const string GenerationRun = "generation_run";
    }

    public static class RouteTelemetryQualities
    {
        public const string High = "high";
        public const string Medium = "medium";
        public const string Low = "low";
        public const string Degraded = "degraded";
        public const string Failed = "failed";
        public const string Blocked = "blocked";
        public const string Unknown = "unknown";
    }

    public static class RouteTelemetryOutcomes
    {
        public const string Selected = "selected";
        public const string Completed = "completed";
        public const string Clarification = "clarification";
        public const string Degraded = "degraded";
        public const string Failed = "failed";
        public const string Blocked = "blocked";
    }

    public sealed record RouteTelemetryEvent(
        DateTimeOffset RecordedAtUtc,
        string Channel,
        string OperationKind,
        string RouteKey,
        string Quality,
        string Outcome,
        double? Confidence = null,
        string? ModelRoute = null,
        string? CorrelationId = null,
        string? IntentSource = null,
        string? ExecutionMode = null,
        string? BudgetProfile = null,
        string? WorkloadClass = null,
        string? DegradationReason = null,
        bool RouteMatched = false,
        bool RequiresClarification = false,
        bool BudgetExceeded = false,
        bool? CompileGatePassed = null,
        bool? ArtifactValidationPassed = null,
        bool? SmokePassed = null,
        bool? GoldenTemplateEligible = null,
        bool? GoldenTemplateMatched = null,
        IReadOnlyList<string>? Signals = null);

    public sealed record RouteTelemetryBucket(string Key, int Count);

    public sealed record RouteTelemetrySnapshot(
        int SchemaVersion,
        DateTimeOffset GeneratedAtUtc,
        long TotalEvents,
        IReadOnlyList<RouteTelemetryBucket> Channels,
        IReadOnlyList<RouteTelemetryBucket> OperationKinds,
        IReadOnlyList<RouteTelemetryBucket> Routes,
        IReadOnlyList<RouteTelemetryBucket> Qualities,
        IReadOnlyList<RouteTelemetryBucket> ModelRoutes,
        IReadOnlyList<RouteTelemetryEvent> Recent,
        IReadOnlyList<string> Alerts);

    public interface IRouteTelemetryService
    {
        void Record(RouteTelemetryEvent entry);
        RouteTelemetrySnapshot GetSnapshot();
    }
}

