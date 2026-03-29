namespace Helper.RuntimeSlice.Contracts;

public sealed record RuntimeSliceAboutDto(
    string ProductName,
    string SliceName,
    string Status,
    bool FixtureMode,
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<string> PublicBoundaries);

public sealed record StartupReadinessSnapshot(
    string Status,
    string Phase,
    string LifecycleState,
    bool ReadyForChat,
    bool Listening,
    string WarmupMode,
    DateTimeOffset? LastTransitionUtc,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? ListeningAtUtc,
    DateTimeOffset? MinimalReadyAtUtc,
    DateTimeOffset? WarmReadyAtUtc,
    long? TimeToListeningMs,
    long? TimeToReadyMs,
    long? TimeToWarmReadyMs,
    IReadOnlyList<string> Alerts);

public sealed record RuntimeLogSourceDto(
    string Id,
    string Label,
    string DisplayPath,
    long SizeBytes,
    DateTimeOffset? LastWriteTimeUtc,
    int TotalLines,
    bool IsPrimary);

public sealed record RuntimeLogSemanticsDto(
    string Scope,
    string Domain,
    string OperationKind,
    string Summary,
    string? Route = null,
    string? CorrelationId = null,
    int? LatencyMs = null,
    string? LatencyBucket = null,
    string? DegradationReason = null,
    IReadOnlyList<string>? Markers = null,
    bool Structured = true);

public sealed record RuntimeLogEntryDto(
    string SourceId,
    int LineNumber,
    string Text,
    string Severity,
    string? TimestampLabel,
    bool IsContinuation,
    RuntimeLogSemanticsDto? Semantics = null);

public sealed record RuntimeLogsSnapshotDto(
    int SchemaVersion,
    string SemanticsVersion,
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<RuntimeLogSourceDto> Sources,
    IReadOnlyList<RuntimeLogEntryDto> Entries,
    IReadOnlyList<string> Alerts);

public sealed record RuntimeSliceGoalDto(string Title, string Description);

public sealed record EvolutionStatusSnapshotDto(
    int ProcessedFiles,
    int TotalFiles,
    string? ActiveTask,
    IReadOnlyList<RuntimeSliceGoalDto> Goals,
    bool IsLearning,
    bool IsIndexing,
    bool IsEvolution,
    string CurrentPhase,
    double? FileProgress,
    string? PipelineVersion,
    string? ChunkingStrategy,
    string? CurrentSection,
    int? CurrentPageStart,
    int? CurrentPageEnd,
    string? ParserVersion,
    IReadOnlyList<string> RecentLearnings,
    IReadOnlyList<string> Alerts);

public sealed record LibraryItemDto(string Path, string Name, string Folder, string Status);

public static class RouteTelemetryChannels
{
    public const string Chat = "chat";
    public const string Generation = "generation";
    public const string RuntimeReview = "runtime_review";
}

public static class RouteTelemetryOperationKinds
{
    public const string ChatTurn = "chat_turn";
    public const string TemplateRouting = "template_routing";
    public const string GenerationRun = "generation_run";
    public const string RuntimeReview = "runtime_review";
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
