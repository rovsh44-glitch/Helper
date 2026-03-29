namespace Helper.Api.Backend.Configuration;

public sealed record BackendConfigValidationSnapshot(
    bool IsValid,
    IReadOnlyList<string> Alerts,
    IReadOnlyList<string>? Warnings = null,
    IReadOnlyList<string>? DeprecatedVariables = null,
    IReadOnlyList<string>? UnknownVariables = null);

public interface IBackendConfigValidator
{
    BackendConfigValidationSnapshot Validate();
}

public sealed class BackendConfigValidator : IBackendConfigValidator
{
    private readonly Helper.Api.Hosting.ApiRuntimeConfig _runtimeConfig;
    private readonly IBackendOptionsCatalog _options;

    public BackendConfigValidator(
        Helper.Api.Hosting.ApiRuntimeConfig runtimeConfig,
        IBackendOptionsCatalog options)
    {
        _runtimeConfig = runtimeConfig;
        _options = options;
    }

    public BackendConfigValidationSnapshot Validate()
    {
        var alerts = new List<string>();
        var warnings = new List<string>();
        var deprecatedVariables = new List<string>();
        var unknownVariables = new List<string>();

        if (string.Equals(_runtimeConfig.RootPath, _runtimeConfig.DataRoot, StringComparison.OrdinalIgnoreCase))
        {
            alerts.Add("Data root must be separated from code root.");
        }

        if (_options.Auth.SessionSigningKeySource == "api_key_derived")
        {
            alerts.Add("Session signing key falls back to API key derived secret. Configure HELPER_SESSION_SIGNING_KEY.");
        }

        if (_options.Auth.AllowLocalBootstrap && !BackendOptionsCatalog.IsDevelopmentLikeCurrentEnvironment())
        {
            alerts.Add("Local bootstrap is enabled outside local development. Set HELPER_AUTH_ALLOW_LOCAL_BOOTSTRAP=false.");
        }

        if (_options.ModelGateway.InteractiveConcurrency > _options.ModelGateway.BackgroundConcurrency + 4)
        {
            alerts.Add("Interactive model concurrency is significantly higher than background pool; validate VRAM budget.");
        }

        if (_options.Persistence.QueueCapacity < _options.Persistence.MaxBatchSize)
        {
            alerts.Add("Persistence queue capacity must be greater than or equal to its batch size.");
        }

        if (_options.Transport.StreamDeadlineSeconds <= _options.Transport.StreamHeartbeatSeconds)
        {
            alerts.Add("Streaming deadline must exceed heartbeat interval.");
        }

        if (!Path.IsPathRooted(_options.Persistence.StorePath))
        {
            alerts.Add("Conversation store path must be absolute.");
        }

        if (_options.Warmup.Mode == "full" && _options.Warmup.StartupBudgetMs < 5000)
        {
            alerts.Add("Warmup startup budget is too low for full warmup mode.");
        }

        if (_options.Performance.PersistenceLagMs < _options.Persistence.FlushDelayMs)
        {
            alerts.Add("Persistence lag alert budget is below write-behind flush delay.");
        }

        EvaluateEnvFileGovernance(
            Path.Combine(_runtimeConfig.RootPath, ".env.local.example"),
            failOnUnknown: true,
            failOnDeprecated: true,
            alerts,
            warnings,
            deprecatedVariables,
            unknownVariables);

        EvaluateEnvFileGovernance(
            Path.Combine(_runtimeConfig.RootPath, ".env.local"),
            failOnUnknown: false,
            failOnDeprecated: false,
            alerts,
            warnings,
            deprecatedVariables,
            unknownVariables);

        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(BackendOptionsCatalog.LegacyLocalBootstrapEnvVar)))
        {
            deprecatedVariables.Add(BackendOptionsCatalog.LegacyLocalBootstrapEnvVar);
            warnings.Add($"Process environment still uses deprecated {BackendOptionsCatalog.LegacyLocalBootstrapEnvVar}. Use {BackendOptionsCatalog.LocalBootstrapEnvVar} instead.");
        }

        return new BackendConfigValidationSnapshot(
            alerts.Count == 0,
            alerts,
            warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            deprecatedVariables.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray(),
            unknownVariables.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static void EvaluateEnvFileGovernance(
        string path,
        bool failOnUnknown,
        bool failOnDeprecated,
        ICollection<string> alerts,
        ICollection<string> warnings,
        ICollection<string> deprecatedVariables,
        ICollection<string> unknownVariables)
    {
        if (!File.Exists(path))
        {
            return;
        }

        var configuredNames = ReadEnvFileVariableNames(path);
        if (configuredNames.Count == 0)
        {
            return;
        }

        var snapshot = BackendEnvironmentInventory.EvaluateNames(configuredNames);
        foreach (var deprecated in snapshot.DeprecatedVariables)
        {
            deprecatedVariables.Add(deprecated);
            var message = $"{Path.GetFileName(path)} uses deprecated variable {deprecated}.";
            if (BackendEnvironmentInventory.TryGet(deprecated, out var definition) && !string.IsNullOrWhiteSpace(definition?.Replacement))
            {
                message += $" Replace it with {definition.Replacement}.";
            }

            if (failOnDeprecated)
            {
                alerts.Add(message);
            }
            else
            {
                warnings.Add(message);
            }
        }

        foreach (var unknown in snapshot.UnknownVariables)
        {
            unknownVariables.Add(unknown);
            var message = $"{Path.GetFileName(path)} defines unknown variable {unknown}. Add it to BackendEnvironmentInventory or remove it.";
            if (failOnUnknown)
            {
                alerts.Add(message);
            }
            else
            {
                warnings.Add(message);
            }
        }
    }

    private static IReadOnlyList<string> ReadEnvFileVariableNames(string path)
    {
        var names = new List<string>();
        foreach (var line in File.ReadAllLines(path))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                continue;
            }

            var equalsIndex = trimmed.IndexOf('=');
            if (equalsIndex <= 0)
            {
                continue;
            }

            var name = trimmed[..equalsIndex].Trim();
            if (!string.IsNullOrWhiteSpace(name))
            {
                names.Add(name);
            }
        }

        return names;
    }
}

