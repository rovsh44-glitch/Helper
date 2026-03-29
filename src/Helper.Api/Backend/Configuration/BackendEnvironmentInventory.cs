using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Helper.Api.Backend.Configuration;

public sealed class BackendEnvironmentVariableDefinition
{
    public BackendEnvironmentVariableDefinition(
        string name,
        string category,
        string scope,
        string valueType,
        string description,
        string? defaultValue = null,
        string? exampleValue = null,
        bool secret = false,
        bool includeInLocalExample = false,
        bool commentOutInLocalExample = false,
        bool deprecated = false,
        string? replacement = null,
        IReadOnlyList<string>? allowedValues = null,
        string? minValue = null,
        string? maxValue = null,
        IReadOnlyList<string>? consumers = null,
        string? notes = null)
    {
        Name = name;
        Category = category;
        Scope = scope;
        ValueType = valueType;
        Description = description;
        DefaultValue = defaultValue;
        ExampleValue = exampleValue;
        Secret = secret;
        IncludeInLocalExample = includeInLocalExample;
        CommentOutInLocalExample = commentOutInLocalExample;
        Deprecated = deprecated;
        Replacement = replacement;
        AllowedValues = allowedValues ?? Array.Empty<string>();
        MinValue = minValue;
        MaxValue = maxValue;
        Consumers = consumers ?? Array.Empty<string>();
        Notes = notes;
    }

    public string Name { get; }
    public string Category { get; }
    public string Scope { get; }
    public string ValueType { get; }
    public string Description { get; }
    public string? DefaultValue { get; }
    public string? ExampleValue { get; }
    public bool Secret { get; }
    public bool IncludeInLocalExample { get; }
    public bool CommentOutInLocalExample { get; }
    public bool Deprecated { get; }
    public string? Replacement { get; }
    public IReadOnlyList<string> AllowedValues { get; }
    public string? MinValue { get; }
    public string? MaxValue { get; }
    public IReadOnlyList<string> Consumers { get; }
    public string? Notes { get; }
}

public sealed class BackendEnvironmentGovernanceSnapshot
{
    public BackendEnvironmentGovernanceSnapshot(
        IReadOnlyList<string> knownVariables,
        IReadOnlyList<string> deprecatedVariables,
        IReadOnlyList<string> unknownVariables)
    {
        KnownVariables = knownVariables;
        DeprecatedVariables = deprecatedVariables;
        UnknownVariables = unknownVariables;
    }

    public IReadOnlyList<string> KnownVariables { get; }
    public IReadOnlyList<string> DeprecatedVariables { get; }
    public IReadOnlyList<string> UnknownVariables { get; }
    public bool IsClean => DeprecatedVariables.Count == 0 && UnknownVariables.Count == 0;
}

public static class BackendEnvironmentInventory
{
    private static readonly IReadOnlyList<BackendEnvironmentVariableDefinition> Definitions = BuildDefinitions();
    private static readonly IReadOnlyDictionary<string, BackendEnvironmentVariableDefinition> DefinitionsByName =
        Definitions.ToDictionary(def => def.Name, StringComparer.OrdinalIgnoreCase);
    private static readonly IReadOnlyList<string> GovernedScriptFiles = new[]
    {
        "scripts/ci_gate.ps1",
        "scripts/check_backend_control_plane.ps1",
        "scripts/check_latency_budget.ps1",
        "scripts/check_remediation_freeze.ps1",
        "scripts/ui_perf_regression.ps1",
        "scripts/run_ui_perf_live.ps1",
        "scripts/cutover_to_v2.ps1",
        "scripts/reset_library_index_safe.ps1",
        "scripts/monitor_library_indexing_supervisor.ps1",
        "scripts/write_chunking_post_cutover_validation.ps1",
        "scripts/run_ordered_library_indexing.ps1",
        "scripts/run_v2_pilot_reindex.ps1"
    };

    public static IReadOnlyList<BackendEnvironmentVariableDefinition> GetDefinitions() => Definitions;

    public static IReadOnlyList<string> GetGovernedScriptFiles() => GovernedScriptFiles;

    public static BackendEnvironmentVariableDefinition Get(string name)
    {
        if (!DefinitionsByName.TryGetValue(name, out var definition))
        {
            throw new KeyNotFoundException($"Unknown governed environment variable '{name}'.");
        }

        return definition;
    }

    public static bool TryGet(string name, out BackendEnvironmentVariableDefinition? definition)
    {
        return DefinitionsByName.TryGetValue(name, out definition);
    }

    public static BackendEnvironmentGovernanceSnapshot EvaluateNames(IEnumerable<string> names)
    {
        var known = new List<string>();
        var deprecated = new List<string>();
        var unknown = new List<string>();

        foreach (var name in names
                     .Where(name => !string.IsNullOrWhiteSpace(name))
                     .Select(name => name.Trim())
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
        {
            if (DefinitionsByName.TryGetValue(name, out var definition))
            {
                known.Add(definition.Name);
                if (definition.Deprecated)
                {
                    deprecated.Add(definition.Name);
                }
            }
            else
            {
                unknown.Add(name);
            }
        }

        return new BackendEnvironmentGovernanceSnapshot(known, deprecated, unknown);
    }

    public static string RenderMarkdown(DateTimeOffset generatedAtUtc)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# HELPER Environment Reference");
        builder.AppendLine();
        builder.AppendLine($"Generated: `{generatedAtUtc:yyyy-MM-dd HH:mm:ss} UTC`");
        builder.AppendLine("Source of truth: `src/Helper.Api/Backend/Configuration/BackendEnvironmentInventory.cs`");
        builder.AppendLine();
        builder.AppendLine("This reference covers the governed configuration surface for:");
        builder.AppendLine();
        builder.AppendLine("1. backend bootstrap and runtime options");
        builder.AppendLine("2. local frontend bootstrap variables");
        builder.AppendLine("3. active operator / CI script variables that are intentionally governed");
        builder.AppendLine();
        builder.AppendLine("Unknown names in `.env.local.example` are treated as repo drift. Deprecated names remain documented here until consumers are migrated off them.");
        builder.AppendLine();

        foreach (var category in Definitions.Select(def => def.Category).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            builder.AppendLine($"## {category}");
            builder.AppendLine();
            builder.AppendLine("| Name | Type | Default | Scope | Notes |");
            builder.AppendLine("| --- | --- | --- | --- | --- |");

            foreach (var definition in Definitions.Where(def => string.Equals(def.Category, category, StringComparison.OrdinalIgnoreCase)))
            {
                builder.Append("| `");
                builder.Append(definition.Name);
                builder.Append("` | `");
                builder.Append(definition.ValueType);
                builder.Append("` | ");
                builder.Append(string.IsNullOrWhiteSpace(definition.DefaultValue) ? "none" : $"`{definition.DefaultValue}`");
                builder.Append(" | `");
                builder.Append(definition.Scope);
                builder.Append("` | ");
                builder.Append(RenderNotes(definition));
                builder.AppendLine(" |");
            }

            builder.AppendLine();
        }

        builder.AppendLine("## Local Template Rules");
        builder.AppendLine();
        builder.AppendLine("1. `.env.local.example` is generated from the same inventory and must not grow ad hoc keys.");
        builder.AppendLine("2. Deprecated keys stay out of the local template even if runtime still accepts them for compatibility.");
        builder.AppendLine("3. Surface-specific browser scopes use `VITE_HELPER_SESSION_SCOPES_<SURFACE>`; the legacy single `VITE_HELPER_SESSION_SCOPES` key is deprecated.");
        builder.AppendLine();
        builder.AppendLine("## Governed Script Files");
        builder.AppendLine();
        foreach (var file in GovernedScriptFiles)
        {
            builder.AppendLine($"- `{file}`");
        }

        return builder.ToString().TrimEnd() + Environment.NewLine;
    }

    public static string RenderLocalEnvironmentTemplate()
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Local development configuration template");
        builder.AppendLine("# Generated from src/Helper.Api/Backend/Configuration/BackendEnvironmentInventory.cs");
        builder.AppendLine("# Copy secrets from your secret manager; do not commit real values.");
        builder.AppendLine();

        foreach (var category in Definitions
                     .Where(def => def.IncludeInLocalExample)
                     .Select(def => def.Category)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            builder.AppendLine($"# {category}");
            foreach (var definition in Definitions.Where(def => def.IncludeInLocalExample &&
                                                                string.Equals(def.Category, category, StringComparison.OrdinalIgnoreCase)))
            {
                builder.AppendLine($"# {definition.Description}");
                if (!string.IsNullOrWhiteSpace(definition.Notes))
                {
                    builder.AppendLine($"# {definition.Notes}");
                }

                var exampleValue = string.IsNullOrWhiteSpace(definition.ExampleValue)
                    ? definition.DefaultValue ?? string.Empty
                    : definition.ExampleValue;
                var line = $"{definition.Name}={exampleValue}";
                builder.AppendLine(definition.CommentOutInLocalExample ? $"# {line}" : line);
                builder.AppendLine();
            }
        }

        return builder.ToString().TrimEnd() + Environment.NewLine;
    }

    private static string RenderNotes(BackendEnvironmentVariableDefinition definition)
    {
        var notes = new List<string> { definition.Description };

        if (definition.Secret)
        {
            notes.Add("secret");
        }

        if (definition.Deprecated)
        {
            notes.Add(string.IsNullOrWhiteSpace(definition.Replacement)
                ? "deprecated"
                : $"deprecated -> `{definition.Replacement}`");
        }

        if (definition.AllowedValues.Count > 0)
        {
            notes.Add("allowed: " + string.Join(", ", definition.AllowedValues.Select(value => $"`{value}`")));
        }

        if (!string.IsNullOrWhiteSpace(definition.MinValue) || !string.IsNullOrWhiteSpace(definition.MaxValue))
        {
            notes.Add($"range: `{definition.MinValue ?? "?"}`..`{definition.MaxValue ?? "?"}`");
        }

        if (!string.IsNullOrWhiteSpace(definition.Notes))
        {
            notes.Add(definition.Notes!);
        }

        return string.Join("; ", notes);
    }

    private static IReadOnlyList<BackendEnvironmentVariableDefinition> BuildDefinitions()
    {
        var definitions = new List<BackendEnvironmentVariableDefinition>();
        AddRuntimePathDefinitions(definitions);
        AddAuthAndBootstrapDefinitions(definitions);
        AddModelWarmupDefinitions(definitions);
        AddModelGatewayDefinitions(definitions);
        AddPersistenceDefinitions(definitions);
        AddAuditDefinitions(definitions);
        AddResearchDefinitions(definitions);
        AddTransportDefinitions(definitions);
        AddPerformanceDefinitions(definitions);
        AddPolicyDefinitions(definitions);
        AddKnowledgeAndIndexingDefinitions(definitions);
        AddFrontendDefinitions(definitions);
        AddOperatorScriptDefinitions(definitions);
        return definitions;
    }

    private static BackendEnvironmentVariableDefinition Def(
        string name,
        string category,
        string scope,
        string valueType,
        string description,
        string? defaultValue = null,
        string? exampleValue = null,
        bool secret = false,
        bool includeInLocalExample = false,
        bool commentOutInLocalExample = false,
        bool deprecated = false,
        string? replacement = null,
        IReadOnlyList<string>? allowedValues = null,
        string? minValue = null,
        string? maxValue = null,
        IReadOnlyList<string>? consumers = null,
        string? notes = null)
    {
        return new BackendEnvironmentVariableDefinition(
            name,
            category,
            scope,
            valueType,
            description,
            defaultValue,
            exampleValue,
            secret,
            includeInLocalExample,
            commentOutInLocalExample,
            deprecated,
            replacement,
            allowedValues,
            minValue,
            maxValue,
            consumers,
            notes);
    }

    private static IReadOnlyList<string> Values(params string[] values) => values;

    private static void AddRuntimePathDefinitions(ICollection<BackendEnvironmentVariableDefinition> definitions)
    {
        definitions.Add(Def("HELPER_API_KEY", "Runtime Paths And Bootstrap", "backend_runtime", "secret", "Primary backend bootstrap key.", secret: true, includeInLocalExample: true, exampleValue: "<set-me>", consumers: Values("src/Helper.Api/Program.cs")));
        definitions.Add(Def("HELPER_ROOT", "Runtime Paths And Bootstrap", "backend_runtime", "path", "Optional explicit helper root. Usually auto-discovered.", exampleValue: "C:\\HELPER", commentOutInLocalExample: true, includeInLocalExample: true, consumers: Values("src/Helper.Api/Program.cs"), notes: "Keep unset unless auto-discovery is wrong."));
        definitions.Add(Def("HELPER_DATA_ROOT", "Runtime Paths And Bootstrap", "backend_runtime", "path", "Runtime data root. Must live outside the repository source tree.", includeInLocalExample: true, exampleValue: "<path-outside-repo>", consumers: Values("src/Helper.Api/Program.cs")));
        definitions.Add(Def("HELPER_PROJECTS_ROOT", "Runtime Paths And Bootstrap", "backend_runtime", "path", "Workspace/project output root. Defaults under HELPER_DATA_ROOT.", includeInLocalExample: true, exampleValue: "<path-outside-repo>\\PROJECTS", consumers: Values("src/Helper.Api/Program.cs")));
        definitions.Add(Def("HELPER_LIBRARY_ROOT", "Runtime Paths And Bootstrap", "backend_runtime", "path", "Library root. Defaults under HELPER_DATA_ROOT.", includeInLocalExample: true, exampleValue: "<path-outside-repo>\\library", consumers: Values("src/Helper.Api/Program.cs")));
        definitions.Add(Def("HELPER_LOGS_ROOT", "Runtime Paths And Bootstrap", "backend_runtime", "path", "Logs root. Defaults under HELPER_DATA_ROOT.", includeInLocalExample: true, exampleValue: "<path-outside-repo>\\LOG", consumers: Values("src/Helper.Api/Program.cs")));
        definitions.Add(Def("HELPER_TEMPLATES_ROOT", "Runtime Paths And Bootstrap", "backend_runtime", "path", "Template library root. Defaults under HELPER_LIBRARY_ROOT\\forge_templates.", includeInLocalExample: true, exampleValue: "<path-outside-repo>\\library\\forge_templates", consumers: Values("src/Helper.Api/Program.cs")));
        definitions.Add(Def("HELPER_CONVERSATION_STORE_PATH", "Runtime Paths And Bootstrap", "backend_runtime", "path", "Optional explicit conversation store file path.", consumers: Values("src/Helper.Api/Backend/Configuration/BackendOptions.cs")));
    }

    private static void AddAuthAndBootstrapDefinitions(ICollection<BackendEnvironmentVariableDefinition> definitions)
    {
        definitions.Add(Def("HELPER_SESSION_SIGNING_KEY", "Auth And Bootstrap", "backend_runtime", "secret", "Dedicated session signing secret for issued browser tokens.", secret: true, includeInLocalExample: true, exampleValue: "<set-a-long-random-secret>", consumers: Values("src/Helper.Api/Backend/Configuration/BackendOptions.cs")));
        definitions.Add(Def("HELPER_AUTH_ALLOW_LOCAL_BOOTSTRAP", "Auth And Bootstrap", "backend_runtime", "bool", "Allow local `/api/auth/session` bootstrap outside the default Development/Local inference.", defaultValue: "environment-driven", includeInLocalExample: true, commentOutInLocalExample: true, exampleValue: "true", consumers: Values("src/Helper.Api/Backend/Configuration/BackendOptions.cs"), notes: "Leave unset for normal local development."));
        definitions.Add(Def("HELPER_ALLOW_LOCAL_BOOTSTRAP", "Auth And Bootstrap", "compatibility", "bool", "Legacy alias for HELPER_AUTH_ALLOW_LOCAL_BOOTSTRAP.", deprecated: true, replacement: "HELPER_AUTH_ALLOW_LOCAL_BOOTSTRAP", consumers: Values("src/Helper.Api/Backend/Configuration/BackendOptions.cs")));
        definitions.Add(Def("HELPER_SESSION_TTL_MIN_MINUTES", "Auth And Bootstrap", "backend_runtime", "int", "Lower bound for issued session token TTL.", defaultValue: "2", minValue: "1", maxValue: "60", consumers: Values("src/Helper.Api/Backend/Configuration/BackendOptions.cs")));
        definitions.Add(Def("HELPER_SESSION_TTL_MAX_MINUTES", "Auth And Bootstrap", "backend_runtime", "int", "Upper bound for issued session token TTL.", defaultValue: "480", minValue: "5", maxValue: "1440", consumers: Values("src/Helper.Api/Backend/Configuration/BackendOptions.cs")));
        definitions.Add(Def("HELPER_SESSION_TOKEN_TTL_MINUTES", "Auth And Bootstrap", "compatibility", "int", "Legacy default session TTL override.", deprecated: true, replacement: "HELPER_SESSION_TTL_MIN_MINUTES/HELPER_SESSION_TTL_MAX_MINUTES", consumers: Values("src/Helper.Api/Hosting/EndpointRegistrationExtensions.cs")));
        definitions.Add(Def("HELPER_LOCAL_BOOTSTRAP_SCOPES", "Auth And Bootstrap", "backend_runtime", "csv", "Optional local bootstrap scope override. Values are intersected with the allowed local scope bundle.", exampleValue: "chat:read,chat:write", consumers: Values("src/Helper.Api/Hosting/EndpointRegistrationExtensions.cs")));
        definitions.Add(Def("HELPER_AUTH_KEYS_PATH", "Auth And Bootstrap", "backend_runtime", "path", "Optional explicit auth key store path.", exampleValue: "<path-outside-repo>\\auth_keys.json", consumers: Values("src/Helper.Api/Hosting/AuthKeysStore.cs"), notes: "Must not point inside src/."));
        definitions.Add(Def("HELPER_AUTH_KEYS_JSON", "Auth And Bootstrap", "backend_runtime", "json", "Inline auth key bootstrap payload. Prefer HELPER_AUTH_KEYS_PATH for persisted local setups.", secret: true, consumers: Values("src/Helper.Api/Hosting/AuthKeysStore.cs")));
    }

    private static void AddModelWarmupDefinitions(ICollection<BackendEnvironmentVariableDefinition> definitions)
    {
        definitions.Add(Def("HELPER_MODEL_WARMUP_MODE", "Model Warmup", "backend_runtime", "enum", "Warmup intensity at startup.", defaultValue: "minimal", includeInLocalExample: true, exampleValue: "minimal", allowedValues: Values("disabled", "minimal", "full"), consumers: Values("src/Helper.Api/Backend/Configuration/BackendOptions.cs")));
        definitions.Add(Def("HELPER_MODEL_WARMUP_CATEGORIES", "Model Warmup", "backend_runtime", "csv", "Model categories to warm when warmup is enabled.", defaultValue: "fast,reasoning,coder", consumers: Values("src/Helper.Api/Backend/Configuration/BackendOptions.cs")));
        definitions.Add(Def("HELPER_MODEL_PREFLIGHT_ENABLED", "Model Warmup", "backend_runtime", "bool", "Enable model preflight probes during startup.", defaultValue: "false", includeInLocalExample: true, commentOutInLocalExample: true, exampleValue: "false", consumers: Values("src/Helper.Api/Backend/Configuration/BackendOptions.cs")));
        definitions.Add(Def("HELPER_MODEL_PREFLIGHT_TIMEOUT_SEC", "Model Warmup", "backend_runtime", "int", "Per-probe preflight timeout.", defaultValue: "20", minValue: "3", maxValue: "120", consumers: Values("src/Helper.Api/Backend/Configuration/BackendOptions.cs")));
        definitions.Add(Def("HELPER_MODEL_PREFLIGHT_WARN_MS", "Model Warmup", "backend_runtime", "int", "Warn threshold for preflight duration.", defaultValue: "12000", minValue: "500", maxValue: "180000", consumers: Values("src/Helper.Api/Backend/Configuration/BackendOptions.cs")));
        definitions.Add(Def("HELPER_WARMUP_IDLE_WINDOW_MS", "Model Warmup", "backend_runtime", "int", "Idle spacing between warmup calls.", defaultValue: "1200", minValue: "250", maxValue: "5000", consumers: Values("src/Helper.Api/Backend/Configuration/BackendOptions.cs")));
        definitions.Add(Def("HELPER_WARMUP_BUDGET_MS", "Model Warmup", "backend_runtime", "int", "Startup warmup budget.", defaultValue: "30000", minValue: "1000", maxValue: "300000", consumers: Values("src/Helper.Api/Backend/Configuration/BackendOptions.cs")));
    }

    private static void AddModelGatewayDefinitions(ICollection<BackendEnvironmentVariableDefinition> definitions)
    {
        definitions.Add(Def("HELPER_MODEL_POOL_INTERACTIVE", "Model Gateway", "backend_runtime", "int", "Interactive pool concurrency.", defaultValue: "2", minValue: "1", maxValue: "32", consumers: Values("src/Helper.Api/Backend/Configuration/BackendOptions.cs")));
        definitions.Add(Def("HELPER_MODEL_POOL_BACKGROUND", "Model Gateway", "backend_runtime", "int", "Background pool concurrency.", defaultValue: "1", minValue: "1", maxValue: "32", consumers: Values("src/Helper.Api/Backend/Configuration/BackendOptions.cs")));
        definitions.Add(Def("HELPER_MODEL_POOL_MAINTENANCE", "Model Gateway", "backend_runtime", "int", "Maintenance pool concurrency.", defaultValue: "1", minValue: "1", maxValue: "16", consumers: Values("src/Helper.Api/Backend/Configuration/BackendOptions.cs")));
        definitions.Add(Def("HELPER_MODEL_TIMEOUT_INTERACTIVE_SEC", "Model Gateway", "backend_runtime", "int", "Interactive call timeout.", defaultValue: "25", minValue: "3", maxValue: "300", consumers: Values("src/Helper.Api/Backend/Configuration/BackendOptions.cs")));
        definitions.Add(Def("HELPER_MODEL_TIMEOUT_BACKGROUND_SEC", "Model Gateway", "backend_runtime", "int", "Background call timeout.", defaultValue: "45", minValue: "3", maxValue: "600", consumers: Values("src/Helper.Api/Backend/Configuration/BackendOptions.cs")));
        definitions.Add(Def("HELPER_MODEL_TIMEOUT_MAINTENANCE_SEC", "Model Gateway", "backend_runtime", "int", "Maintenance call timeout.", defaultValue: "60", minValue: "3", maxValue: "600", consumers: Values("src/Helper.Api/Backend/Configuration/BackendOptions.cs")));
        definitions.Add(Def("HELPER_MODEL_FAST", "Model Gateway", "backend_runtime", "string", "Optional fast-model override / fallback route.", consumers: Values("src/Helper.Api/Backend/Configuration/BackendOptions.cs")));
        definitions.Add(Def("HELPER_MODEL_REASONING", "Model Gateway", "backend_runtime", "string", "Optional reasoning-model override / fallback route.", consumers: Values("src/Helper.Api/Backend/Configuration/BackendOptions.cs")));
        definitions.Add(Def("HELPER_MODEL_LONG_CONTEXT", "Model Gateway", "backend_runtime", "string", "Optional long-context reasoning model override.", consumers: Values("src/Helper.Api/Backend/Configuration/BackendOptions.cs", "src/Helper.Runtime/SupportingImplementations.cs")));
        definitions.Add(Def("HELPER_MODEL_DEEP_REASONING", "Model Gateway", "backend_runtime", "string", "Optional deep-reasoning model override.", consumers: Values("src/Helper.Api/Backend/Configuration/BackendOptions.cs", "src/Helper.Runtime/SupportingImplementations.cs")));
        definitions.Add(Def("HELPER_MODEL_VERIFIER", "Model Gateway", "backend_runtime", "string", "Optional verifier / critic model override for reasoning checks.", consumers: Values("src/Helper.Api/Backend/Configuration/BackendOptions.cs", "src/Helper.Runtime/SupportingImplementations.cs")));
        definitions.Add(Def("HELPER_MODEL_CRITIC", "Model Gateway", "backend_runtime", "string", "Optional critic-model override / fallback route.", consumers: Values("src/Helper.Api/Backend/Configuration/BackendOptions.cs")));
        definitions.Add(Def("HELPER_MODEL_SAFE_FALLBACK", "Model Gateway", "backend_runtime", "string", "Optional safe fallback model name.", consumers: Values("src/Helper.Api/Backend/Configuration/BackendOptions.cs")));
    }

    private static void AddPersistenceDefinitions(ICollection<BackendEnvironmentVariableDefinition> definitions)
    {
        definitions.Add(Def("HELPER_CONVERSATION_PERSIST_FLUSH_MS", "Persistence", "backend_runtime", "int", "Write-behind flush delay.", defaultValue: "1500", minValue: "0", maxValue: "30000", consumers: Values("src/Helper.Api/Backend/Configuration/BackendOptions.cs")));
        definitions.Add(Def("HELPER_CONVERSATION_JOURNAL_COMPACTION_THRESHOLD", "Persistence", "backend_runtime", "int", "Journal compaction threshold.", defaultValue: "25", minValue: "5", maxValue: "500", consumers: Values("src/Helper.Api/Backend/Configuration/BackendOptions.cs")));
        definitions.Add(Def("HELPER_PERSISTENCE_QUEUE_CAPACITY", "Persistence", "backend_runtime", "int", "Write-behind queue capacity.", defaultValue: "1024", minValue: "64", maxValue: "16384", consumers: Values("src/Helper.Api/Backend/Configuration/BackendOptions.cs")));
        definitions.Add(Def("HELPER_PERSISTENCE_QUEUE_BATCH_SIZE", "Persistence", "backend_runtime", "int", "Write-behind batch size.", defaultValue: "32", minValue: "1", maxValue: "1024", consumers: Values("src/Helper.Api/Backend/Configuration/BackendOptions.cs")));
        definitions.Add(Def("HELPER_PERSISTENCE_QUEUE_BACKLOG_ALERT", "Persistence", "backend_runtime", "int", "Persistence backlog alert threshold.", defaultValue: "128", minValue: "8", maxValue: "16384", consumers: Values("src/Helper.Api/Backend/Configuration/BackendOptions.cs")));
        definitions.Add(Def("HELPER_PERSISTENCE_LAG_ALERT_MS", "Persistence", "backend_runtime", "int", "Persistence lag alert threshold.", defaultValue: "10000", minValue: "250", maxValue: "300000", consumers: Values("src/Helper.Api/Backend/Configuration/BackendOptions.cs")));
    }

    private static void AddAuditDefinitions(ICollection<BackendEnvironmentVariableDefinition> definitions)
    {
        definitions.Add(Def("HELPER_POST_TURN_AUDIT_QUEUE_CAPACITY", "Post-Turn Audit", "backend_runtime", "int", "Audit queue capacity.", defaultValue: "512", minValue: "64", maxValue: "8192", consumers: Values("src/Helper.Api/Backend/Configuration/BackendOptions.cs")));
        definitions.Add(Def("HELPER_POST_TURN_AUDIT_TIMEOUT_SEC", "Post-Turn Audit", "backend_runtime", "int", "Audit worker timeout.", defaultValue: "8", minValue: "2", maxValue: "120", consumers: Values("src/Helper.Api/Backend/Configuration/BackendOptions.cs")));
        definitions.Add(Def("HELPER_POST_TURN_AUDIT_MAX_ATTEMPTS", "Post-Turn Audit", "backend_runtime", "int", "Maximum audit retry attempts.", defaultValue: "2", minValue: "1", maxValue: "10", consumers: Values("src/Helper.Api/Backend/Configuration/BackendOptions.cs")));
        definitions.Add(Def("HELPER_POST_TURN_AUDIT_BACKLOG_ALERT", "Post-Turn Audit", "backend_runtime", "int", "Audit backlog alert threshold.", defaultValue: "96", minValue: "8", maxValue: "8192", consumers: Values("src/Helper.Api/Backend/Configuration/BackendOptions.cs")));
        definitions.Add(Def("HELPER_POST_TURN_AUDIT_FAILURE_ALERT", "Post-Turn Audit", "backend_runtime", "double", "Audit failure-rate alert threshold.", defaultValue: "0.20", minValue: "0.01", maxValue: "1.0", consumers: Values("src/Helper.Api/Backend/Configuration/BackendOptions.cs")));
    }

    private static void AddResearchDefinitions(ICollection<BackendEnvironmentVariableDefinition> definitions)
    {
        definitions.Add(Def("HELPER_RESEARCH_ENABLED", "Research", "backend_runtime", "bool", "Enable research routing.", defaultValue: "true", consumers: Values("src/Helper.Api/Backend/Configuration/BackendOptions.cs")));
        definitions.Add(Def("HELPER_RESEARCH_CACHE_TTL_MINUTES", "Research", "backend_runtime", "int", "Research cache TTL in minutes.", defaultValue: "20", minValue: "1", maxValue: "720", consumers: Values("src/Helper.Api/Backend/Configuration/BackendOptions.cs")));
        definitions.Add(Def("HELPER_GROUNDING_CASUAL_CHAT_ENABLED", "Research", "backend_runtime", "bool", "Allow grounding for casual chat.", defaultValue: "false", consumers: Values("src/Helper.Api/Backend/Configuration/BackendOptions.cs")));
        definitions.Add(Def("HELPER_RESEARCH_MAX_SOURCES", "Research", "backend_runtime", "int", "Maximum grounded sources per response.", defaultValue: "8", minValue: "1", maxValue: "64", consumers: Values("src/Helper.Api/Backend/Configuration/BackendOptions.cs")));
        definitions.Add(Def("HELPER_RESEARCH_BACKGROUND_BUDGET", "Research", "backend_runtime", "int", "Background research parallelism budget.", defaultValue: "1", minValue: "0", maxValue: "16", consumers: Values("src/Helper.Api/Backend/Configuration/BackendOptions.cs")));
        definitions.Add(Def("HELPER_WEB_SEARCH_LOCAL_URL", "Research", "backend_runtime", "url", "Primary local web-search endpoint used by the local provider adapter.", defaultValue: "http://localhost:8080", consumers: Values("src/Helper.Runtime.WebResearch/Providers/WebSearchProviderSettings.cs")));
        definitions.Add(Def("HELPER_WEB_SEARCH_SEARX_URL", "Research", "backend_runtime", "url", "Optional secondary Searx-compatible endpoint used for graceful failover.", consumers: Values("src/Helper.Runtime.WebResearch/Providers/WebSearchProviderSettings.cs")));
        definitions.Add(Def("HELPER_WEB_SEARCH_PROVIDER_ORDER", "Research", "backend_runtime", "csv", "Ordered provider IDs for web-search failover. Supported values currently include `local,searx`.", defaultValue: "local,searx", consumers: Values("src/Helper.Runtime.WebResearch/Providers/WebSearchProviderSettings.cs", "src/Helper.Runtime.WebResearch/Providers/WebSearchProviderMux.cs")));
        definitions.Add(Def("HELPER_WEB_SEARCH_PROVIDER_TIMEOUT_SEC", "Research", "backend_runtime", "int", "Per-provider web-search timeout before mux failover.", defaultValue: "4", minValue: "1", maxValue: "30", consumers: Values("src/Helper.Runtime.WebResearch/Providers/WebSearchProviderSettings.cs")));
        definitions.Add(Def("HELPER_WEB_SEARCH_COST_BUDGET_UNITS", "Research", "backend_runtime", "int", "Base cost budget units for provider selection per search turn before policy adjustments by search mode.", defaultValue: "3", minValue: "0", maxValue: "8", consumers: Values("src/Helper.Runtime.WebResearch/Providers/WebSearchProviderSettings.cs", "src/Helper.Runtime.WebResearch/Providers/SearchCostBudgetPolicy.cs")));
        definitions.Add(Def("HELPER_WEB_SEARCH_LATENCY_BUDGET_MS", "Research", "backend_runtime", "int", "Base total latency budget for provider selection per search turn before policy adjustments by search mode.", defaultValue: "3500", minValue: "250", maxValue: "30000", consumers: Values("src/Helper.Runtime.WebResearch/Providers/WebSearchProviderSettings.cs", "src/Helper.Runtime.WebResearch/Providers/TurnLatencyBudgetPolicy.cs")));
        definitions.Add(Def("HELPER_WEB_SEARCH_PROVIDER_COOLDOWN_SEC", "Research", "backend_runtime", "int", "Cooldown window applied after consecutive provider timeouts or errors trip health thresholds.", defaultValue: "45", minValue: "5", maxValue: "600", consumers: Values("src/Helper.Runtime.WebResearch/Providers/WebSearchProviderSettings.cs", "src/Helper.Runtime.WebResearch/Providers/WebProviderHealthState.cs")));
        definitions.Add(Def("HELPER_WEB_SEARCH_PROVIDER_MAX_CONSECUTIVE_TIMEOUTS", "Research", "backend_runtime", "int", "Consecutive timeout threshold before a provider enters cooldown.", defaultValue: "2", minValue: "1", maxValue: "10", consumers: Values("src/Helper.Runtime.WebResearch/Providers/WebSearchProviderSettings.cs", "src/Helper.Runtime.WebResearch/Providers/WebProviderHealthState.cs")));
        definitions.Add(Def("HELPER_WEB_SEARCH_PROVIDER_MAX_CONSECUTIVE_ERRORS", "Research", "backend_runtime", "int", "Consecutive error threshold before a provider enters cooldown.", defaultValue: "2", minValue: "1", maxValue: "10", consumers: Values("src/Helper.Runtime.WebResearch/Providers/WebSearchProviderSettings.cs", "src/Helper.Runtime.WebResearch/Providers/WebProviderHealthState.cs")));
        definitions.Add(Def("HELPER_WEB_SEARCH_PROVIDER_SLOW_LATENCY_MS", "Research", "backend_runtime", "int", "Rolling latency threshold after which a provider is considered degraded for selection scoring.", defaultValue: "1200", minValue: "100", maxValue: "30000", consumers: Values("src/Helper.Runtime.WebResearch/Providers/WebSearchProviderSettings.cs", "src/Helper.Runtime.WebResearch/Providers/WebProviderHealthState.cs")));
        definitions.Add(Def("HELPER_WEB_SEARCH_MAX_ITERATIONS", "Research", "backend_runtime", "int", "Maximum iterative web-search passes per request.", defaultValue: "3", minValue: "1", maxValue: "3", consumers: Values("src/Helper.Runtime.WebResearch/SearchIterationPolicy.cs")));
        definitions.Add(Def("HELPER_WEB_FETCH_MAX_REDIRECTS", "Research", "backend_runtime", "int", "Maximum redirect hops allowed during provider/page fetch before the request is blocked.", defaultValue: "3", minValue: "0", maxValue: "5", consumers: Values("src/Helper.Runtime.WebResearch/Providers/WebSearchProviderSettings.cs", "src/Helper.Runtime.WebResearch/Fetching/RedirectGuard.cs")));
        definitions.Add(Def("HELPER_WEB_FETCH_USE_PROXY", "Research", "backend_runtime", "bool", "Whether outbound page/document fetches should honor the system proxy configuration. Defaults to false to avoid broken local proxy interception in offline/dev runtimes.", defaultValue: "false", consumers: Values("src/Helper.Runtime.WebResearch/Fetching/HttpFetchSupport.cs")));
        definitions.Add(Def("HELPER_WEB_PAGE_FETCH_TIMEOUT_SEC", "Research", "backend_runtime", "int", "Per-page fetch timeout for full-page evidence retrieval.", defaultValue: "6", minValue: "1", maxValue: "20", consumers: Values("src/Helper.Runtime.WebResearch/Fetching/WebPageFetchSettings.cs", "src/Helper.Runtime.WebResearch/Fetching/WebPageFetcher.cs")));
        definitions.Add(Def("HELPER_WEB_PAGE_MAX_BYTES", "Research", "backend_runtime", "int", "Maximum bytes admitted for a single fetched page before extraction is aborted.", defaultValue: "400000", minValue: "16384", maxValue: "2000000", consumers: Values("src/Helper.Runtime.WebResearch/Fetching/WebPageFetchSettings.cs", "src/Helper.Runtime.WebResearch/Extraction/ContentTypeAdmissionPolicy.cs")));
        definitions.Add(Def("HELPER_WEB_PAGE_MAX_FETCHES_PER_SEARCH", "Research", "backend_runtime", "int", "Maximum fetched pages per search session after search hits are selected.", defaultValue: "3", minValue: "1", maxValue: "6", consumers: Values("src/Helper.Runtime.WebResearch/Fetching/WebPageFetchSettings.cs", "src/Helper.Runtime.WebResearch/WebSearchSessionCoordinator.cs")));
        definitions.Add(Def("HELPER_WEB_RENDER_ENABLED", "Research", "backend_runtime", "bool", "Enables isolated browser-render fallback for JS-heavy pages after normal HTTP extraction is insufficient.", defaultValue: "true", consumers: Values("src/Helper.Runtime.WebResearch/Rendering/RenderedPageBudgetPolicy.cs")));
        definitions.Add(Def("HELPER_WEB_RENDER_MAX_PAGES_PER_SEARCH", "Research", "backend_runtime", "int", "Maximum pages per search session that may use browser-render fallback.", defaultValue: "1", minValue: "0", maxValue: "3", consumers: Values("src/Helper.Runtime.WebResearch/Rendering/RenderedPageBudgetPolicy.cs", "src/Helper.Runtime.WebResearch/WebSearchSessionCoordinator.cs")));
        definitions.Add(Def("HELPER_WEB_RENDER_TIMEOUT_SEC", "Research", "backend_runtime", "int", "Timeout for a single browser-render fallback page load.", defaultValue: "8", minValue: "2", maxValue: "20", consumers: Values("src/Helper.Runtime.WebResearch/Rendering/RenderedPageBudgetPolicy.cs", "src/Helper.Runtime.WebResearch/Rendering/BrowserRenderFallbackService.cs")));
        definitions.Add(Def("HELPER_WEB_RENDER_MAX_HTML_CHARS", "Research", "backend_runtime", "int", "Maximum rendered HTML characters retained from browser-render fallback before extraction.", defaultValue: "300000", minValue: "16384", maxValue: "1000000", consumers: Values("src/Helper.Runtime.WebResearch/Rendering/RenderedPageBudgetPolicy.cs", "src/Helper.Runtime.WebResearch/Rendering/BrowserRenderFallbackService.cs")));
        definitions.Add(Def("HELPER_WEB_EVIDENCE_GENERAL_STALE_MINUTES", "Research", "backend_runtime", "int", "Maximum age before general web evidence is treated as stale and refreshed or disclosed.", defaultValue: "20", minValue: "1", maxValue: "1440", consumers: Values("src/Helper.Api/Conversation/FreshnessWindowPolicy.cs")));
        definitions.Add(Def("HELPER_WEB_EVIDENCE_VOLATILE_STALE_MINUTES", "Research", "backend_runtime", "int", "Maximum age before volatile categories such as finance/news/weather are treated as stale.", defaultValue: "30", minValue: "1", maxValue: "240", consumers: Values("src/Helper.Api/Conversation/FreshnessWindowPolicy.cs")));
        definitions.Add(Def("HELPER_WEB_EVIDENCE_SOFTWARE_STALE_MINUTES", "Research", "backend_runtime", "int", "Maximum age before software-version or release evidence is treated as stale.", defaultValue: "360", minValue: "5", maxValue: "10080", consumers: Values("src/Helper.Api/Conversation/FreshnessWindowPolicy.cs")));
    }

    private static void AddTransportDefinitions(ICollection<BackendEnvironmentVariableDefinition> definitions)
    {
        definitions.Add(Def("HELPER_STREAM_HEARTBEAT_SECONDS", "Transport", "backend_runtime", "int", "SSE heartbeat interval in seconds.", defaultValue: "10", minValue: "2", maxValue: "60", consumers: Values("src/Helper.Api/Backend/Configuration/BackendOptions.cs")));
        definitions.Add(Def("HELPER_STREAM_DEADLINE_SECONDS", "Transport", "backend_runtime", "int", "SSE request deadline in seconds.", defaultValue: "90", minValue: "10", maxValue: "1800", consumers: Values("src/Helper.Api/Backend/Configuration/BackendOptions.cs")));
        definitions.Add(Def("HELPER_STREAM_HEARTBEAT_MS", "Transport", "compatibility", "int", "Legacy raw heartbeat interval override in milliseconds.", deprecated: true, replacement: "HELPER_STREAM_HEARTBEAT_SECONDS", consumers: Values("src/Helper.Api/Hosting/EndpointRegistrationExtensions.cs")));
    }

    private static void AddPerformanceDefinitions(ICollection<BackendEnvironmentVariableDefinition> definitions)
    {
        definitions.Add(Def("HELPER_LISTEN_BUDGET_MS", "Performance Budgets", "backend_runtime", "int", "Startup listen budget.", defaultValue: "5000", minValue: "500", maxValue: "60000", consumers: Values("src/Helper.Api/Backend/Configuration/BackendOptions.cs")));
        definitions.Add(Def("HELPER_READINESS_BUDGET_MS", "Performance Budgets", "backend_runtime", "int", "Startup readiness budget.", defaultValue: "30000", minValue: "1000", maxValue: "300000", consumers: Values("src/Helper.Api/Backend/Configuration/BackendOptions.cs")));
        definitions.Add(Def("HELPER_FIRST_TOKEN_BUDGET_MS", "Performance Budgets", "backend_runtime", "int", "First-token latency budget.", defaultValue: "1200", minValue: "100", maxValue: "60000", consumers: Values("src/Helper.Api/Backend/Configuration/BackendOptions.cs")));
        definitions.Add(Def("HELPER_P95_FULL_TURN_BUDGET_MS", "Performance Budgets", "backend_runtime", "int", "P95 full-turn latency budget.", defaultValue: "4000", minValue: "250", maxValue: "300000", consumers: Values("src/Helper.Api/Backend/Configuration/BackendOptions.cs")));
    }

    private static void AddPolicyDefinitions(ICollection<BackendEnvironmentVariableDefinition> definitions)
    {
        definitions.Add(Def("HELPER_POLICY_RESEARCH_ENABLED", "Runtime Policies", "backend_runtime", "bool", "Policy gate for research routing.", defaultValue: "true", consumers: Values("src/Helper.Api/Backend/Configuration/BackendOptions.cs")));
        definitions.Add(Def("HELPER_POLICY_GROUNDING_ENABLED", "Runtime Policies", "backend_runtime", "bool", "Policy gate for grounding.", defaultValue: "true", consumers: Values("src/Helper.Api/Backend/Configuration/BackendOptions.cs")));
        definitions.Add(Def("HELPER_POLICY_SYNC_CRITIC_ENABLED", "Runtime Policies", "backend_runtime", "bool", "Policy gate for synchronous critic execution.", defaultValue: "true", consumers: Values("src/Helper.Api/Backend/Configuration/BackendOptions.cs")));
        definitions.Add(Def("HELPER_POLICY_ASYNC_AUDIT_ENABLED", "Runtime Policies", "backend_runtime", "bool", "Policy gate for asynchronous post-turn audit.", defaultValue: "true", consumers: Values("src/Helper.Api/Backend/Configuration/BackendOptions.cs")));
        definitions.Add(Def("HELPER_SHADOW_MODE_ENABLED", "Runtime Policies", "backend_runtime", "bool", "Enable shadow-mode execution paths.", defaultValue: "false", consumers: Values("src/Helper.Api/Backend/Configuration/BackendOptions.cs")));
        definitions.Add(Def("HELPER_POLICY_SAFE_FALLBACK_ONLY", "Runtime Policies", "backend_runtime", "bool", "Restrict backend to safe fallback responses only.", defaultValue: "false", consumers: Values("src/Helper.Api/Backend/Configuration/BackendOptions.cs")));
    }

    private static void AddFrontendDefinitions(ICollection<BackendEnvironmentVariableDefinition> definitions)
    {
        definitions.Add(Def("VITE_HELPER_API_PROTOCOL", "Frontend Local", "frontend_local", "string", "Frontend API protocol when VITE_HELPER_API_BASE is not set.", defaultValue: "http", includeInLocalExample: true, exampleValue: "http", consumers: Values("services/apiConfig.ts")));
        definitions.Add(Def("VITE_HELPER_API_HOST", "Frontend Local", "frontend_local", "string", "Frontend API host when VITE_HELPER_API_BASE is not set.", defaultValue: "localhost", includeInLocalExample: true, exampleValue: "localhost", consumers: Values("services/apiConfig.ts")));
        definitions.Add(Def("VITE_HELPER_API_PORT", "Frontend Local", "frontend_local", "string", "Frontend API port when VITE_HELPER_API_BASE is not set.", defaultValue: "5000", includeInLocalExample: true, exampleValue: "5056", consumers: Values("services/apiConfig.ts")));
        definitions.Add(Def("VITE_HELPER_API_BASE", "Frontend Local", "frontend_local", "url", "Explicit frontend API base URL.", includeInLocalExample: true, exampleValue: "http://localhost:5056", consumers: Values("services/apiConfig.ts")));
        definitions.Add(Def("VITE_HELPER_SESSION_SCOPES_CONVERSATION", "Frontend Local", "frontend_local", "csv", "Optional conversation surface scope override.", exampleValue: "chat:read,chat:write,feedback:write", includeInLocalExample: true, commentOutInLocalExample: true, consumers: Values("services/apiConfig.ts")));
        definitions.Add(Def("VITE_HELPER_SESSION_SCOPES_RUNTIME_CONSOLE", "Frontend Local", "frontend_local", "csv", "Optional runtime-console surface scope override.", exampleValue: "metrics:read", includeInLocalExample: true, commentOutInLocalExample: true, consumers: Values("services/apiConfig.ts")));
        definitions.Add(Def("VITE_HELPER_SESSION_SCOPES_BUILDER", "Frontend Local", "frontend_local", "csv", "Optional builder surface scope override.", exampleValue: "chat:read,chat:write,tools:execute,build:run,fs:write", includeInLocalExample: true, commentOutInLocalExample: true, consumers: Values("services/apiConfig.ts")));
        definitions.Add(Def("VITE_HELPER_SESSION_SCOPES_EVOLUTION", "Frontend Local", "frontend_local", "csv", "Optional evolution surface scope override.", exampleValue: "evolution:control,metrics:read", includeInLocalExample: true, commentOutInLocalExample: true, consumers: Values("services/apiConfig.ts")));
        definitions.Add(Def("VITE_API_BASE", "Frontend Local", "compatibility", "url", "Legacy frontend API base variable.", deprecated: true, replacement: "VITE_HELPER_API_BASE", consumers: Values(".env.local.example")));
        definitions.Add(Def("VITE_HELPER_SESSION_SCOPES", "Frontend Local", "compatibility", "csv", "Legacy single-surface scope override key.", deprecated: true, replacement: "VITE_HELPER_SESSION_SCOPES_<SURFACE>", consumers: Values(".env.local.example")));
    }

    private static void AddKnowledgeAndIndexingDefinitions(ICollection<BackendEnvironmentVariableDefinition> definitions)
    {
        definitions.Add(Def("HELPER_MODEL_CODER", "Knowledge And Indexing", "backend_runtime", "string", "Optional coder-model route.", consumers: Values("src/Helper.Runtime/AILink.cs")));
        definitions.Add(Def("HELPER_MODEL_VISION", "Knowledge And Indexing", "backend_runtime", "string", "Optional vision-model route.", consumers: Values("src/Helper.Runtime/AILink.cs")));
        definitions.Add(Def("HELPER_PDF_VISION_GHOSTSCRIPT_PATH", "Knowledge And Indexing", "backend_runtime", "path", "Explicit Ghostscript executable for PDF-to-image vision extraction.", consumers: Values("src/Helper.Runtime.Knowledge/StructuredDocumentParsers.Textual.cs")));
        definitions.Add(Def("HELPER_INDEX_PIPELINE_VERSION", "Knowledge And Indexing", "script_runtime", "string", "Active library indexing pipeline version.", defaultValue: "v1", consumers: Values("src/Helper.Runtime.Knowledge/LibrarianAgent.cs", "scripts/cutover_to_v2.ps1", "scripts/monitor_library_indexing_supervisor.ps1", "scripts/write_chunking_post_cutover_validation.ps1", "scripts/run_ordered_library_indexing.ps1", "scripts/run_v2_pilot_reindex.ps1")));
        definitions.Add(Def("HELPER_RAG_ALLOW_V1_FALLBACK", "Knowledge And Indexing", "script_runtime", "bool", "Allow retrieval fallback to legacy v1 collections.", defaultValue: "true", consumers: Values("src/Helper.Runtime.Knowledge/Retrieval/RetrievalCollectionRouter.cs", "scripts/cutover_to_v2.ps1", "scripts/write_chunking_post_cutover_validation.ps1")));
        definitions.Add(Def("HELPER_INDEX_EXCLUDED_EXTENSIONS", "Knowledge And Indexing", "script_runtime", "csv", "Extensions excluded from ordered/reset indexing scripts and synthetic learning scans.", consumers: Values("src/Helper.Runtime/SyntheticLearningService.cs", "scripts/reset_library_index_safe.ps1", "scripts/run_ordered_library_indexing.ps1")));
        definitions.Add(Def("HELPER_INDEX_EXCLUDED_FILES", "Knowledge And Indexing", "script_runtime", "csv", "Explicit file paths excluded from ordered/reset indexing scripts and synthetic learning scans.", consumers: Values("src/Helper.Runtime/SyntheticLearningService.cs", "scripts/reset_library_index_safe.ps1", "scripts/run_ordered_library_indexing.ps1")));
        definitions.Add(Def("HELPER_INDEX_MAX_CHUNKS_PER_DOCUMENT", "Knowledge And Indexing", "backend_runtime", "int", "Default max chunks per document for v2 indexing.", defaultValue: "1600", minValue: "128", maxValue: "64000", consumers: Values("src/Helper.Runtime.Knowledge/StructuredLibrarianV2Pipeline.cs")));
        definitions.Add(Def("HELPER_INDEX_MAX_CHUNKS_PER_DOCUMENT_LARGE_REFERENCE", "Knowledge And Indexing", "backend_runtime", "int", "Max chunks for large reference documents.", defaultValue: "24000", minValue: "1600", maxValue: "120000", consumers: Values("src/Helper.Runtime.Knowledge/StructuredLibrarianV2Pipeline.cs", "src/Helper.Runtime.Knowledge/LibrarianAgent.cs")));
        definitions.Add(Def("HELPER_INDEX_LARGE_REFERENCE_MIN_PAGES", "Knowledge And Indexing", "backend_runtime", "int", "Minimum page count to treat a document as a large reference.", defaultValue: "400", minValue: "64", maxValue: "10000", consumers: Values("src/Helper.Runtime.Knowledge/StructuredLibrarianV2Pipeline.cs")));
        definitions.Add(Def("HELPER_INDEX_MAX_CHUNKS_PER_DOCUMENT_LARGE_DOCUMENT", "Knowledge And Indexing", "backend_runtime", "int", "Max chunks for large-document indexing mode.", defaultValue: "12000", minValue: "1600", maxValue: "120000", consumers: Values("src/Helper.Runtime.Knowledge/StructuredLibrarianV2Pipeline.cs")));
        definitions.Add(Def("HELPER_INDEX_LARGE_DOCUMENT_MIN_PAGES", "Knowledge And Indexing", "backend_runtime", "int", "Minimum pages for large-document mode.", defaultValue: "250", minValue: "64", maxValue: "10000", consumers: Values("src/Helper.Runtime.Knowledge/StructuredLibrarianV2Pipeline.cs")));
        definitions.Add(Def("HELPER_INDEX_LARGE_DOCUMENT_MIN_BLOCKS", "Knowledge And Indexing", "backend_runtime", "int", "Minimum block count for large-document mode.", defaultValue: "400", minValue: "64", maxValue: "100000", consumers: Values("src/Helper.Runtime.Knowledge/StructuredLibrarianV2Pipeline.cs")));
        definitions.Add(Def("HELPER_INDEX_LARGE_DOCUMENT_MIN_OBSERVED_CHUNKS", "Knowledge And Indexing", "backend_runtime", "int", "Minimum observed chunk count before forcing large-document mode.", defaultValue: "1700", minValue: "1601", maxValue: "120000", consumers: Values("src/Helper.Runtime.Knowledge/StructuredLibrarianV2Pipeline.cs")));
        definitions.Add(Def("HELPER_ENABLE_AUTONOMOUS_EVOLUTION_AUTOSTART", "Knowledge And Indexing", "backend_runtime", "bool", "Autostart autonomous evolution on maintenance startup.", defaultValue: "false", consumers: Values("src/Helper.Runtime/MaintenanceService.cs")));
    }

    private static void AddOperatorScriptDefinitions(ICollection<BackendEnvironmentVariableDefinition> definitions)
    {
        definitions.Add(Def("HELPER_RUNTIME_SMOKE_API_BASE", "Operator And CI Scripts", "script_runtime", "url", "API base URL used by runtime smoke and CI gates.", consumers: Values("scripts/ci_gate.ps1", "scripts/check_backend_control_plane.ps1", "scripts/check_latency_budget.ps1", "scripts/ui_perf_regression.ps1", "scripts/reset_library_index_safe.ps1")));
        definitions.Add(Def("HELPER_RUNTIME_SMOKE_UI_URL", "Operator And CI Scripts", "script_runtime", "url", "UI URL used by runtime UI perf smoke.", consumers: Values("scripts/ci_gate.ps1", "scripts/ui_perf_regression.ps1")));
        definitions.Add(Def("HELPER_REMEDIATION_LOCK", "Operator And CI Scripts", "script_runtime", "bool", "Explicit remediation freeze guard. CI expects `1` when freeze is active.", defaultValue: "unset", consumers: Values("scripts/check_remediation_freeze.ps1")));
    }
}

