using Helper.Runtime.Infrastructure;

namespace Helper.Api.Backend.Configuration;

public sealed record AuthOptions(
    bool AllowLocalBootstrap,
    string SessionSigningKeySource,
    int MinSessionTtlMinutes,
    int MaxSessionTtlMinutes);

public sealed record WarmupOptions(
    string Mode,
    IReadOnlyList<string> Categories,
    bool ProbeEnabled,
    int ProbeTimeoutSeconds,
    int ProbeWarnMs,
    int IdleWindowMs,
    int StartupBudgetMs);

public sealed record ModelGatewayOptions(
    int InteractiveConcurrency,
    int BackgroundConcurrency,
    int MaintenanceConcurrency,
    int InteractiveTimeoutSeconds,
    int BackgroundTimeoutSeconds,
    int MaintenanceTimeoutSeconds,
    string? FastFallbackModel,
    string? ReasoningFallbackModel,
    string? LongContextFallbackModel,
    string? DeepReasoningFallbackModel,
    string? VerifierFallbackModel,
    string? CriticFallbackModel,
    string? SafeFallbackModel);

public sealed record PersistenceOptions(
    string StorePath,
    int FlushDelayMs,
    int JournalCompactionThreshold,
    int QueueCapacity,
    int MaxBatchSize,
    int BacklogAlertThreshold,
    int LagAlertMs);

public sealed record AuditOptions(
    int QueueCapacity,
    int TimeoutSeconds,
    int MaxAttempts,
    int BacklogAlertThreshold,
    double FailureRateAlertThreshold);

public sealed record ResearchOptions(
    bool Enabled,
    int CacheTtlMinutes,
    bool GroundingForCasualChatEnabled,
    int MaxSources,
    int BackgroundBudget);

public sealed record TransportOptions(
    int StreamHeartbeatSeconds,
    int StreamDeadlineSeconds);

public sealed record PerformanceBudgetOptions(
    int ListenMs,
    int ReadinessMs,
    int FirstTokenMs,
    int P95FullTurnMs,
    int WarmupBudgetMs,
    int AuditBacklogThreshold,
    int PersistenceBacklogThreshold,
    int PersistenceLagMs);

public sealed record BackendRuntimePolicies(
    bool ResearchEnabled,
    bool GroundingEnabled,
    bool SynchronousCriticEnabled,
    bool AsyncAuditEnabled,
    bool ShadowModeEnabled,
    bool SafeFallbackResponsesOnly);

public interface IBackendOptionsCatalog
{
    AuthOptions Auth { get; }
    WarmupOptions Warmup { get; }
    ModelGatewayOptions ModelGateway { get; }
    PersistenceOptions Persistence { get; }
    AuditOptions Audit { get; }
    ResearchOptions Research { get; }
    TransportOptions Transport { get; }
    PerformanceBudgetOptions Performance { get; }
    BackendRuntimePolicies Policies { get; }
}

public interface IBackendRuntimePolicyProvider
{
    BackendRuntimePolicies GetPolicies();
}

public sealed class BackendOptionsCatalog : IBackendOptionsCatalog, IBackendRuntimePolicyProvider
{
    public const string SessionSigningKeyEnvVar = "HELPER_SESSION_SIGNING_KEY";
    public const string LocalBootstrapEnvVar = "HELPER_AUTH_ALLOW_LOCAL_BOOTSTRAP";
    public const string LegacyLocalBootstrapEnvVar = "HELPER_ALLOW_LOCAL_BOOTSTRAP";

    public BackendOptionsCatalog(Helper.Api.Hosting.ApiRuntimeConfig runtimeConfig)
    {
        Auth = new AuthOptions(
            AllowLocalBootstrap: ResolveLocalBootstrapAllowed(),
            SessionSigningKeySource: ResolveSessionSigningKeySource(),
            MinSessionTtlMinutes: ReadInt(Env("HELPER_SESSION_TTL_MIN_MINUTES")),
            MaxSessionTtlMinutes: ReadInt(Env("HELPER_SESSION_TTL_MAX_MINUTES")));

        var warmupCategories = ReadCsv(Env("HELPER_MODEL_WARMUP_CATEGORIES"));
        Warmup = new WarmupOptions(
            Mode: ReadEnum(Env("HELPER_MODEL_WARMUP_MODE")),
            Categories: warmupCategories,
            ProbeEnabled: ReadBool(Env("HELPER_MODEL_PREFLIGHT_ENABLED")),
            ProbeTimeoutSeconds: ReadInt(Env("HELPER_MODEL_PREFLIGHT_TIMEOUT_SEC")),
            ProbeWarnMs: ReadInt(Env("HELPER_MODEL_PREFLIGHT_WARN_MS")),
            IdleWindowMs: ReadInt(Env("HELPER_WARMUP_IDLE_WINDOW_MS")),
            StartupBudgetMs: ReadInt(Env("HELPER_WARMUP_BUDGET_MS")));

        ModelGateway = new ModelGatewayOptions(
            InteractiveConcurrency: ReadInt(Env("HELPER_MODEL_POOL_INTERACTIVE")),
            BackgroundConcurrency: ReadInt(Env("HELPER_MODEL_POOL_BACKGROUND")),
            MaintenanceConcurrency: ReadInt(Env("HELPER_MODEL_POOL_MAINTENANCE")),
            InteractiveTimeoutSeconds: ReadInt(Env("HELPER_MODEL_TIMEOUT_INTERACTIVE_SEC")),
            BackgroundTimeoutSeconds: ReadInt(Env("HELPER_MODEL_TIMEOUT_BACKGROUND_SEC")),
            MaintenanceTimeoutSeconds: ReadInt(Env("HELPER_MODEL_TIMEOUT_MAINTENANCE_SEC")),
            FastFallbackModel: ReadNullable(Env("HELPER_MODEL_FAST")),
            ReasoningFallbackModel: ReadNullable(Env("HELPER_MODEL_REASONING")),
            LongContextFallbackModel: ReadNullable(Env("HELPER_MODEL_LONG_CONTEXT")),
            DeepReasoningFallbackModel: ReadNullable(Env("HELPER_MODEL_DEEP_REASONING")),
            VerifierFallbackModel: ReadNullable(Env("HELPER_MODEL_VERIFIER")),
            CriticFallbackModel: ReadNullable(Env("HELPER_MODEL_CRITIC")),
            SafeFallbackModel: ReadNullable(Env("HELPER_MODEL_SAFE_FALLBACK")));

        Persistence = new PersistenceOptions(
            StorePath: ResolveStorePath(runtimeConfig),
            FlushDelayMs: ReadInt(Env("HELPER_CONVERSATION_PERSIST_FLUSH_MS")),
            JournalCompactionThreshold: ReadInt(Env("HELPER_CONVERSATION_JOURNAL_COMPACTION_THRESHOLD")),
            QueueCapacity: ReadInt(Env("HELPER_PERSISTENCE_QUEUE_CAPACITY")),
            MaxBatchSize: ReadInt(Env("HELPER_PERSISTENCE_QUEUE_BATCH_SIZE")),
            BacklogAlertThreshold: ReadInt(Env("HELPER_PERSISTENCE_QUEUE_BACKLOG_ALERT")),
            LagAlertMs: ReadInt(Env("HELPER_PERSISTENCE_LAG_ALERT_MS")));

        Audit = new AuditOptions(
            QueueCapacity: ReadInt(Env("HELPER_POST_TURN_AUDIT_QUEUE_CAPACITY")),
            TimeoutSeconds: ReadInt(Env("HELPER_POST_TURN_AUDIT_TIMEOUT_SEC")),
            MaxAttempts: ReadInt(Env("HELPER_POST_TURN_AUDIT_MAX_ATTEMPTS")),
            BacklogAlertThreshold: ReadInt(Env("HELPER_POST_TURN_AUDIT_BACKLOG_ALERT")),
            FailureRateAlertThreshold: ReadDouble(Env("HELPER_POST_TURN_AUDIT_FAILURE_ALERT")));

        Research = new ResearchOptions(
            Enabled: ReadBool(Env("HELPER_RESEARCH_ENABLED")),
            CacheTtlMinutes: ReadInt(Env("HELPER_RESEARCH_CACHE_TTL_MINUTES")),
            GroundingForCasualChatEnabled: ReadBool(Env("HELPER_GROUNDING_CASUAL_CHAT_ENABLED")),
            MaxSources: ReadInt(Env("HELPER_RESEARCH_MAX_SOURCES")),
            BackgroundBudget: ReadInt(Env("HELPER_RESEARCH_BACKGROUND_BUDGET")));

        Transport = new TransportOptions(
            StreamHeartbeatSeconds: ReadInt(Env("HELPER_STREAM_HEARTBEAT_SECONDS")),
            StreamDeadlineSeconds: ReadInt(Env("HELPER_STREAM_DEADLINE_SECONDS")));

        Performance = new PerformanceBudgetOptions(
            ListenMs: ReadInt(Env("HELPER_LISTEN_BUDGET_MS")),
            ReadinessMs: ReadInt(Env("HELPER_READINESS_BUDGET_MS")),
            FirstTokenMs: ReadInt(Env("HELPER_FIRST_TOKEN_BUDGET_MS")),
            P95FullTurnMs: ReadInt(Env("HELPER_P95_FULL_TURN_BUDGET_MS")),
            WarmupBudgetMs: ReadInt(Env("HELPER_WARMUP_BUDGET_MS")),
            AuditBacklogThreshold: ReadInt(Env("HELPER_POST_TURN_AUDIT_BACKLOG_ALERT")),
            PersistenceBacklogThreshold: ReadInt(Env("HELPER_PERSISTENCE_QUEUE_BACKLOG_ALERT")),
            PersistenceLagMs: ReadInt(Env("HELPER_PERSISTENCE_LAG_ALERT_MS")));

        Policies = new BackendRuntimePolicies(
            ResearchEnabled: ReadBool(Env("HELPER_POLICY_RESEARCH_ENABLED")),
            GroundingEnabled: ReadBool(Env("HELPER_POLICY_GROUNDING_ENABLED")),
            SynchronousCriticEnabled: ReadBool(Env("HELPER_POLICY_SYNC_CRITIC_ENABLED")),
            AsyncAuditEnabled: ReadBool(Env("HELPER_POLICY_ASYNC_AUDIT_ENABLED")),
            ShadowModeEnabled: ReadBool(Env("HELPER_SHADOW_MODE_ENABLED")),
            SafeFallbackResponsesOnly: ReadBool(Env("HELPER_POLICY_SAFE_FALLBACK_ONLY")));
    }

    public AuthOptions Auth { get; }

    public WarmupOptions Warmup { get; }

    public ModelGatewayOptions ModelGateway { get; }

    public PersistenceOptions Persistence { get; }

    public AuditOptions Audit { get; }

    public ResearchOptions Research { get; }

    public TransportOptions Transport { get; }

    public PerformanceBudgetOptions Performance { get; }

    public BackendRuntimePolicies Policies { get; }

    public BackendRuntimePolicies GetPolicies() => Policies;

    public static bool ResolveLocalBootstrapAllowed()
    {
        if (TryReadBool(LocalBootstrapEnvVar, out var configured))
        {
            return configured;
        }

        if (TryReadBool(LegacyLocalBootstrapEnvVar, out configured))
        {
            return configured;
        }

        return IsDevelopmentLikeEnvironment(Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")) ||
               IsDevelopmentLikeEnvironment(Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT"));
    }

    public static bool IsDevelopmentLikeCurrentEnvironment()
    {
        return IsDevelopmentLikeEnvironment(Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")) ||
               IsDevelopmentLikeEnvironment(Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT"));
    }

    private static string ResolveStorePath(Helper.Api.Hosting.ApiRuntimeConfig runtimeConfig)
    {
        var explicitPath = ReadNullable(Env("HELPER_CONVERSATION_STORE_PATH"));
        return string.IsNullOrWhiteSpace(explicitPath)
            ? Path.Combine(runtimeConfig.LogsRoot, "conversation_store.json")
            : Path.IsPathRooted(explicitPath)
                ? Path.GetFullPath(explicitPath)
                : HelperWorkspacePathResolver.ResolveLogsPath(explicitPath, runtimeConfig.LogsRoot, runtimeConfig.RootPath);
    }

    private static string ResolveSessionSigningKeySource()
    {
        var rawSecret = ReadNullable(Env(SessionSigningKeyEnvVar));
        return string.IsNullOrWhiteSpace(rawSecret) ? "api_key_derived" : "dedicated_session_secret";
    }

    private static BackendEnvironmentVariableDefinition Env(string envName) => BackendEnvironmentInventory.Get(envName);

    private static bool IsDevelopmentLikeEnvironment(string? environmentName)
    {
        return string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(environmentName, "Local", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ReadNullable(BackendEnvironmentVariableDefinition definition)
    {
        var raw = Environment.GetEnvironmentVariable(definition.Name);
        return string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();
    }

    private static bool TryReadBool(string envName, out bool parsed)
    {
        var raw = Environment.GetEnvironmentVariable(envName);
        return bool.TryParse(raw, out parsed);
    }

    private static bool ReadBool(BackendEnvironmentVariableDefinition definition)
    {
        var raw = Environment.GetEnvironmentVariable(definition.Name);
        return bool.TryParse(raw, out var parsed)
            ? parsed
            : bool.TryParse(definition.DefaultValue, out var fallback)
                ? fallback
                : false;
    }

    private static int ReadInt(BackendEnvironmentVariableDefinition definition)
    {
        var raw = Environment.GetEnvironmentVariable(definition.Name);
        var fallback = int.TryParse(definition.DefaultValue, out var fallbackValue) ? fallbackValue : 0;
        if (!int.TryParse(raw, out var parsed))
        {
            parsed = fallback;
        }

        var min = int.TryParse(definition.MinValue, out var parsedMin) ? parsedMin : int.MinValue;
        var max = int.TryParse(definition.MaxValue, out var parsedMax) ? parsedMax : int.MaxValue;
        return Math.Clamp(parsed, min, max);
    }

    private static double ReadDouble(BackendEnvironmentVariableDefinition definition)
    {
        var raw = Environment.GetEnvironmentVariable(definition.Name);
        var fallback = double.TryParse(definition.DefaultValue, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var fallbackValue)
            ? fallbackValue
            : 0d;
        if (!double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
        {
            parsed = fallback;
        }

        var min = double.TryParse(definition.MinValue, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsedMin)
            ? parsedMin
            : double.MinValue;
        var max = double.TryParse(definition.MaxValue, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsedMax)
            ? parsedMax
            : double.MaxValue;
        return Math.Clamp(parsed, min, max);
    }

    private static string ReadEnum(BackendEnvironmentVariableDefinition definition)
    {
        var raw = Environment.GetEnvironmentVariable(definition.Name)?.Trim().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(raw) && definition.AllowedValues.Contains(raw, StringComparer.OrdinalIgnoreCase))
        {
            return raw;
        }

        return definition.DefaultValue ?? string.Empty;
    }

    private static IReadOnlyList<string> ReadCsv(BackendEnvironmentVariableDefinition definition)
    {
        var raw = Environment.GetEnvironmentVariable(definition.Name);
        if (string.IsNullOrWhiteSpace(raw))
        {
            raw = definition.DefaultValue;
        }

        if (string.IsNullOrWhiteSpace(raw))
        {
            return Array.Empty<string>();
        }

        var values = raw
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return values.Length == 0 ? Array.Empty<string>() : values;
    }
}

