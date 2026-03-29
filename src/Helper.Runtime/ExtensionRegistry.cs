using System.Text.Json;
using System.Text.RegularExpressions;
using Helper.Runtime.Core;

namespace Helper.Runtime.Infrastructure
{
    public sealed class ExtensionRegistry : IExtensionRegistry
    {
        public const string ManifestSchemaVersion = "helper.extension.manifest/v1";

        private static readonly Regex SafeIdPattern = new("^[a-z0-9][a-z0-9_.-]*$", RegexOptions.Compiled);
        private static readonly Regex SafeEnvPattern = new("^[A-Z][A-Z0-9_]*$", RegexOptions.Compiled);
        private static readonly Regex SafeToolPattern = new("^[a-zA-Z0-9_.:-]+$", RegexOptions.Compiled);
        private readonly Lazy<ExtensionRegistrySnapshot> _snapshot;

        public ExtensionRegistry()
        {
            _snapshot = new Lazy<ExtensionRegistrySnapshot>(LoadSnapshot);
        }

        public ExtensionRegistrySnapshot GetSnapshot() => _snapshot.Value;

        public IReadOnlyList<ExtensionManifest> GetByCategory(ExtensionCategory category)
            => GetSnapshot().Manifests.Where(x => x.Category == category).ToArray();

        public bool TryGetManifest(string extensionId, out ExtensionManifest manifest)
        {
            var resolved = GetSnapshot().Manifests.FirstOrDefault(x =>
                string.Equals(x.Id, NormalizeName(extensionId), StringComparison.OrdinalIgnoreCase));
            if (resolved is null)
            {
                manifest = null!;
                return false;
            }

            manifest = resolved;
            return true;
        }

        private static ExtensionRegistrySnapshot LoadSnapshot()
        {
            var helperRoot = HelperWorkspacePathResolver.ResolveHelperRoot(Directory.GetCurrentDirectory());
            var manifestDirectory = HelperWorkspacePathResolver.ResolveWorkspaceFile(Path.Combine("mcp_config", "extensions"), helperRoot);
            var manifests = new List<ExtensionManifest>();
            var failures = new List<string>();
            var warnings = new List<string>();

            if (Directory.Exists(manifestDirectory))
            {
                foreach (var manifestPath in Directory.GetFiles(manifestDirectory, "*.json", SearchOption.TopDirectoryOnly).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                {
                    if (TryLoadManifest(manifestPath, out var manifest, out var failure))
                    {
                        manifests.Add(manifest!);
                    }
                    else if (!string.IsNullOrWhiteSpace(failure))
                    {
                        failures.Add(failure);
                    }
                }
            }

            if (manifests.Count == 0)
            {
                LoadLegacyServersFallback(helperRoot, manifests, failures, warnings);
            }

            return new ExtensionRegistrySnapshot(
                manifests.OrderBy(x => x.Category).ThenBy(x => x.Id, StringComparer.OrdinalIgnoreCase).ToArray(),
                failures.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
        }

        private static bool TryLoadManifest(string manifestPath, out ExtensionManifest? manifest, out string failure)
        {
            manifest = null;
            failure = string.Empty;

            try
            {
                var json = File.ReadAllText(manifestPath);
                var document = JsonSerializer.Deserialize<ExtensionManifestDocument>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (document is null)
                {
                    failure = $"Extension manifest '{manifestPath}' is empty or unreadable.";
                    return false;
                }

                if (!string.Equals(document.SchemaVersion?.Trim(), ManifestSchemaVersion, StringComparison.Ordinal))
                {
                    failure = $"Extension manifest '{manifestPath}' declares unsupported schemaVersion '{document.SchemaVersion ?? "<null>"}'.";
                    return false;
                }

                var normalizedId = NormalizeName(document.Id);
                if (!SafeIdPattern.IsMatch(normalizedId))
                {
                    failure = $"Extension manifest '{manifestPath}' has invalid id '{document.Id ?? "<null>"}'.";
                    return false;
                }

                if (!TryParseCategory(document.Category, out var category))
                {
                    failure = $"Extension manifest '{manifestPath}' has unsupported category '{document.Category ?? "<null>"}'.";
                    return false;
                }

                if (!TryParseTransport(document.Transport, out var transport))
                {
                    failure = $"Extension manifest '{manifestPath}' has unsupported transport '{document.Transport ?? "<null>"}'.";
                    return false;
                }

                if (!TryParseTrustLevel(document.TrustLevel, out var trustLevel))
                {
                    failure = $"Extension manifest '{manifestPath}' has unsupported trustLevel '{document.TrustLevel ?? "<null>"}'.";
                    return false;
                }

                var providerType = NormalizeName(document.ProviderType);
                if (!SafeIdPattern.IsMatch(providerType))
                {
                    failure = $"Extension manifest '{manifestPath}' has invalid providerType '{document.ProviderType ?? "<null>"}'.";
                    return false;
                }

                var declaredTools = NormalizeToolNames(document.DeclaredTools);
                var requiredEnv = NormalizeEnvironmentVariables(document.RequiredEnv, manifestPath, out var envFailure);
                if (!string.IsNullOrWhiteSpace(envFailure))
                {
                    failure = envFailure;
                    return false;
                }

                var capabilities = NormalizeNameList(document.Capabilities);
                var args = NormalizeArgs(document.Args);
                var command = string.IsNullOrWhiteSpace(document.Command) ? null : document.Command.Trim();

                if (IsCheckedInManifestPathPortableViolation(command, args))
                {
                    failure = $"Extension manifest '{manifestPath}' is not portable: absolute command or args paths are not allowed in checked-in manifests.";
                    return false;
                }

                if (category is ExtensionCategory.BuiltIn or ExtensionCategory.Internal)
                {
                    if (transport != ExtensionTransport.None)
                    {
                        failure = $"Extension manifest '{manifestPath}' must use transport 'none' for category '{category}'.";
                        return false;
                    }

                    if (declaredTools.Count == 0)
                    {
                        failure = $"Extension manifest '{manifestPath}' must declare at least one tool for category '{category}'.";
                        return false;
                    }
                }

                if (category is ExtensionCategory.External or ExtensionCategory.Experimental)
                {
                    if (transport != ExtensionTransport.Stdio)
                    {
                        failure = $"Extension manifest '{manifestPath}' must use transport 'stdio' for category '{category}'.";
                        return false;
                    }

                    if (!string.Equals(providerType, "mcp", StringComparison.OrdinalIgnoreCase))
                    {
                        failure = $"Extension manifest '{manifestPath}' must use providerType 'mcp' for category '{category}'.";
                        return false;
                    }

                    if (string.IsNullOrWhiteSpace(command))
                    {
                        failure = $"Extension manifest '{manifestPath}' requires a command for MCP provider activation.";
                        return false;
                    }
                }

                var toolPolicy = NormalizeToolPolicy(document.ToolPolicy, manifestPath, out var toolPolicyFailure);
                if (!string.IsNullOrWhiteSpace(toolPolicyFailure))
                {
                    failure = toolPolicyFailure;
                    return false;
                }

                manifest = new ExtensionManifest(
                    SchemaVersion: ManifestSchemaVersion,
                    Id: normalizedId,
                    DisplayName: string.IsNullOrWhiteSpace(document.DisplayName) ? normalizedId : document.DisplayName.Trim(),
                    Category: category,
                    ProviderType: providerType,
                    Transport: transport,
                    Description: string.IsNullOrWhiteSpace(document.Description) ? normalizedId : document.Description.Trim(),
                    Command: command,
                    Args: args,
                    DeclaredTools: declaredTools,
                    RequiredEnv: requiredEnv,
                    Capabilities: capabilities,
                    TrustLevel: trustLevel,
                    DefaultEnabled: document.DefaultEnabled ?? false,
                    DisabledInCertificationMode: document.DisabledInCertificationMode ?? false,
                    QuietWhenUnavailable: document.QuietWhenUnavailable ?? false,
                    ToolPolicy: toolPolicy,
                    SourcePath: manifestPath);

                return true;
            }
            catch (JsonException ex)
            {
                failure = $"Extension manifest '{manifestPath}' contains invalid JSON: {ex.Message}";
                return false;
            }
            catch (Exception ex)
            {
                failure = $"Extension manifest '{manifestPath}' failed to load: {ex.Message}";
                return false;
            }
        }

        private static void LoadLegacyServersFallback(string helperRoot, List<ExtensionManifest> manifests, List<string> failures, List<string> warnings)
        {
            var legacyPath = HelperWorkspacePathResolver.ResolveWorkspaceFile(Path.Combine("mcp_config", "servers.json"), helperRoot);
            if (!File.Exists(legacyPath))
            {
                return;
            }

            try
            {
                var json = File.ReadAllText(legacyPath);
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("mcpServers", out var servers))
                {
                    return;
                }

                warnings.Add("Using deprecated mcp_config/servers.json fallback. Migrate to mcp_config/extensions/*.json manifests.");
                foreach (var serverProperty in servers.EnumerateObject())
                {
                    var id = NormalizeName(serverProperty.Name);
                    if (!SafeIdPattern.IsMatch(id))
                    {
                        failures.Add($"Legacy MCP provider '{serverProperty.Name}' in '{legacyPath}' has an invalid id.");
                        continue;
                    }

                    var command = serverProperty.Value.TryGetProperty("command", out var commandProp)
                        ? commandProp.GetString()
                        : null;
                    var args = serverProperty.Value.TryGetProperty("args", out var argsProp)
                        ? argsProp.EnumerateArray().Select(x => x.GetString() ?? string.Empty).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray()
                        : Array.Empty<string>();
                    var requiredEnv = serverProperty.Value.TryGetProperty("env", out var envProp)
                        ? envProp.EnumerateObject().Select(x => x.Name).ToArray()
                        : Array.Empty<string>();

                    manifests.Add(new ExtensionManifest(
                        SchemaVersion: "legacy.servers.json",
                        Id: id,
                        DisplayName: serverProperty.Name,
                        Category: ExtensionCategory.External,
                        ProviderType: "mcp",
                        Transport: ExtensionTransport.Stdio,
                        Description: $"Legacy MCP provider '{serverProperty.Name}' loaded from deprecated servers.json.",
                        Command: string.IsNullOrWhiteSpace(command) ? null : command.Trim(),
                        Args: NormalizeArgs(args),
                        DeclaredTools: Array.Empty<string>(),
                        RequiredEnv: NormalizeNameList(requiredEnv),
                        Capabilities: Array.Empty<string>(),
                        TrustLevel: ExtensionTrustLevel.TrustedExternal,
                        DefaultEnabled: true,
                        DisabledInCertificationMode: false,
                        QuietWhenUnavailable: false,
                        ToolPolicy: new ExtensionToolPolicy(false, Array.Empty<string>()),
                        SourcePath: legacyPath));
                }
            }
            catch (Exception ex)
            {
                failures.Add($"Failed to load deprecated MCP registry '{legacyPath}': {ex.Message}");
            }
        }

        private static ExtensionToolPolicy NormalizeToolPolicy(ExtensionToolPolicyDocument? document, string manifestPath, out string failure)
        {
            failure = string.Empty;
            if (document is null)
            {
                return new ExtensionToolPolicy(false, Array.Empty<string>());
            }

            var allowedTools = NormalizeToolNames(document.AllowedTools);
            if (allowedTools.Any(tool => !SafeToolPattern.IsMatch(tool)))
            {
                failure = $"Extension manifest '{manifestPath}' contains invalid tool names in toolPolicy.allowedTools.";
                return new ExtensionToolPolicy(false, Array.Empty<string>());
            }

            return new ExtensionToolPolicy(document.AllowAllTools ?? false, allowedTools);
        }

        private static IReadOnlyList<string> NormalizeEnvironmentVariables(IEnumerable<string>? values, string manifestPath, out string failure)
        {
            failure = string.Empty;
            var normalized = new List<string>();
            foreach (var value in values ?? Array.Empty<string>())
            {
                var trimmed = value?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(trimmed))
                {
                    continue;
                }

                if (!SafeEnvPattern.IsMatch(trimmed))
                {
                    failure = $"Extension manifest '{manifestPath}' contains invalid env name '{trimmed}'.";
                    return Array.Empty<string>();
                }

                normalized.Add(trimmed);
            }

            return normalized.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        private static IReadOnlyList<string> NormalizeToolNames(IEnumerable<string>? values)
        {
            return (values ?? Array.Empty<string>())
                .Select(x => x?.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(NormalizeName)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static IReadOnlyList<string> NormalizeArgs(IEnumerable<string>? values)
        {
            return (values ?? Array.Empty<string>())
                .Select(x => x?.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Cast<string>()
                .ToArray();
        }

        private static IReadOnlyList<string> NormalizeNameList(IEnumerable<string>? values)
        {
            return (values ?? Array.Empty<string>())
                .Select(NormalizeName)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static bool IsCheckedInManifestPathPortableViolation(string? command, IReadOnlyList<string> args)
        {
            if (!string.IsNullOrWhiteSpace(command) && Path.IsPathRooted(command))
            {
                return true;
            }

            foreach (var arg in args)
            {
                if (Path.IsPathRooted(arg))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryParseCategory(string? value, out ExtensionCategory category)
        {
            category = ExtensionCategory.External;
            return NormalizeName(value) switch
            {
                "built_in" => TryAssign(ExtensionCategory.BuiltIn, out category),
                "internal" => TryAssign(ExtensionCategory.Internal, out category),
                "external" => TryAssign(ExtensionCategory.External, out category),
                "experimental" => TryAssign(ExtensionCategory.Experimental, out category),
                _ => false
            };
        }

        private static bool TryParseTransport(string? value, out ExtensionTransport transport)
        {
            transport = ExtensionTransport.None;
            return NormalizeName(value) switch
            {
                "none" => TryAssign(ExtensionTransport.None, out transport),
                "stdio" => TryAssign(ExtensionTransport.Stdio, out transport),
                _ => false
            };
        }

        private static bool TryParseTrustLevel(string? value, out ExtensionTrustLevel trustLevel)
        {
            trustLevel = ExtensionTrustLevel.TrustedExternal;
            return NormalizeName(value) switch
            {
                "built_in" => TryAssign(ExtensionTrustLevel.BuiltIn, out trustLevel),
                "internal" => TryAssign(ExtensionTrustLevel.Internal, out trustLevel),
                "trusted_external" => TryAssign(ExtensionTrustLevel.TrustedExternal, out trustLevel),
                "experimental" => TryAssign(ExtensionTrustLevel.Experimental, out trustLevel),
                _ => false
            };
        }

        private static bool TryAssign<T>(T value, out T destination)
        {
            destination = value;
            return true;
        }

        private static string NormalizeName(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            return value.Trim().ToLowerInvariant();
        }

        private sealed class ExtensionManifestDocument
        {
            public string? SchemaVersion { get; set; }
            public string? Id { get; set; }
            public string? DisplayName { get; set; }
            public string? Category { get; set; }
            public string? ProviderType { get; set; }
            public string? Transport { get; set; }
            public string? Description { get; set; }
            public string? Command { get; set; }
            public List<string>? Args { get; set; }
            public List<string>? DeclaredTools { get; set; }
            public List<string>? RequiredEnv { get; set; }
            public List<string>? Capabilities { get; set; }
            public string? TrustLevel { get; set; }
            public bool? DefaultEnabled { get; set; }
            public bool? DisabledInCertificationMode { get; set; }
            public bool? QuietWhenUnavailable { get; set; }
            public ExtensionToolPolicyDocument? ToolPolicy { get; set; }
        }

        private sealed class ExtensionToolPolicyDocument
        {
            public bool? AllowAllTools { get; set; }
            public List<string>? AllowedTools { get; set; }
        }
    }
}

