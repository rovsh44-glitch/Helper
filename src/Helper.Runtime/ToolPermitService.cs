using System.Text.Json;
using System.Text.RegularExpressions;
using Helper.Runtime.Core;

namespace Helper.Runtime.Infrastructure
{
    public interface IToolPermitService
    {
        Task<ToolPermitDecision> DecideAsync(string toolName, Dictionary<string, object> arguments, CancellationToken ct = default);
    }

    public sealed record ToolPermitDecision(bool Allowed, string Reason);

    public sealed class ToolPermitService : IToolPermitService
    {
        private readonly IExtensionRegistry? _extensionRegistry;
        private readonly HashSet<string> _allowedLocalTools;
        private readonly HashSet<string> _blockedArgumentTokens = new(StringComparer.OrdinalIgnoreCase)
        {
            "ignore previous instructions",
            "reveal system prompt",
            "exfiltrate",
            "access_token",
            "authorization: bearer",
            "private key"
        };

        private readonly Dictionary<string, HashSet<string>> _mcpAllowedToolsByProvider;
        private readonly HashSet<string> _manifestTrustedMcpProviders;
        private readonly HashSet<string> _configuredTrustedMcpProviders;
        private readonly bool _hasTrustedProviderOverride;
        private readonly bool _allowAnyTrustedMcpTool;
        private static readonly Regex SafeNamePattern = new("^[a-zA-Z0-9_.:-]+$", RegexOptions.Compiled);

        public ToolPermitService(IExtensionRegistry? extensionRegistry = null)
        {
            _extensionRegistry = extensionRegistry;
            _allowedLocalTools = LoadAllowedLocalTools(extensionRegistry);
            _mcpAllowedToolsByProvider = LoadMcpToolPolicy(extensionRegistry);
            _manifestTrustedMcpProviders = LoadManifestTrustedMcpProviders(extensionRegistry);
            _configuredTrustedMcpProviders = LoadConfiguredTrustedMcpProviders();
            _hasTrustedProviderOverride = _configuredTrustedMcpProviders.Count > 0;
            _allowAnyTrustedMcpTool = ReadFlag("HELPER_MCP_ALLOW_ANY_TRUSTED_TOOL", fallback: false);
        }

        public Task<ToolPermitDecision> DecideAsync(string toolName, Dictionary<string, object> arguments, CancellationToken ct = default)
        {
            var normalizedToolName = (toolName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalizedToolName))
            {
                return Task.FromResult(new ToolPermitDecision(false, "Tool name is empty."));
            }

            if (!SafeNamePattern.IsMatch(normalizedToolName))
            {
                return Task.FromResult(new ToolPermitDecision(false, $"Tool '{normalizedToolName}' contains unsafe characters."));
            }

            if (!AreArgumentsSafe(arguments, out var argumentReason))
            {
                return Task.FromResult(new ToolPermitDecision(false, argumentReason));
            }

            if (normalizedToolName.Equals("shell_execute", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(new ToolPermitDecision(false, "Tool 'shell_execute' is retired from the production local tool surface."));
            }

            if (_allowedLocalTools.Contains(normalizedToolName))
            {
                return Task.FromResult(DecideForLocalTool(normalizedToolName));
            }

            if (TryParseMcpTool(normalizedToolName, out var provider, out var providerTool))
            {
                return Task.FromResult(DecideForMcpTool(normalizedToolName, provider, providerTool));
            }

            return Task.FromResult(new ToolPermitDecision(false, $"Tool '{normalizedToolName}' is not approved by strict deny-by-default policy."));
        }

        private ToolPermitDecision DecideForLocalTool(string toolName)
        {
            return new ToolPermitDecision(true, "Permitted local tool.");
        }

        private ToolPermitDecision DecideForMcpTool(string toolName, string provider, string providerTool)
        {
            if (_extensionRegistry?.TryGetManifest(provider, out var manifest) == true &&
                manifest.TrustLevel == ExtensionTrustLevel.Experimental &&
                !ReadFlag("HELPER_ENABLE_EXPERIMENTAL_EXTENSIONS", fallback: false))
            {
                return new ToolPermitDecision(false, $"MCP provider '{provider}' is experimental and not enabled.");
            }

            if (!IsMcpProviderTrusted(provider))
            {
                return new ToolPermitDecision(false, $"MCP provider '{provider}' is not trusted.");
            }

            if (_allowAnyTrustedMcpTool)
            {
                return new ToolPermitDecision(true, $"Permitted MCP tool '{toolName}' via trusted-provider wildcard.");
            }

            if (!_mcpAllowedToolsByProvider.TryGetValue(provider, out var allowedTools))
            {
                return new ToolPermitDecision(false, $"MCP tool '{toolName}' denied: provider '{provider}' has no permit policy.");
            }

            if (!allowedTools.Contains("*") && !allowedTools.Contains(providerTool))
            {
                return new ToolPermitDecision(false, $"MCP tool '{toolName}' denied by provider policy.");
            }

            return new ToolPermitDecision(true, $"Permitted MCP tool '{toolName}'.");
        }

        private bool AreArgumentsSafe(Dictionary<string, object> arguments, out string reason)
        {
            reason = string.Empty;
            var serializedArgs = JsonSerializer.Serialize(arguments ?? new Dictionary<string, object>());
            foreach (var token in _blockedArgumentTokens)
            {
                if (serializedArgs.Contains(token, StringComparison.OrdinalIgnoreCase))
                {
                    reason = $"Potential injection/exfiltration token detected: '{token}'.";
                    return false;
                }
            }

            return true;
        }

        private bool IsMcpProviderTrusted(string provider)
        {
            if (_hasTrustedProviderOverride)
            {
                return _configuredTrustedMcpProviders.Contains("*") || _configuredTrustedMcpProviders.Contains(provider);
            }

            return _manifestTrustedMcpProviders.Contains("*") || _manifestTrustedMcpProviders.Contains(provider);
        }

        private static bool TryParseMcpTool(string toolName, out string provider, out string providerTool)
        {
            provider = string.Empty;
            providerTool = string.Empty;

            if (toolName.StartsWith("mcp:", StringComparison.OrdinalIgnoreCase))
            {
                var parts = toolName.Split(':', 3, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts.Length == 3)
                {
                    provider = NormalizeName(parts[1]);
                    providerTool = NormalizeName(parts[2]);
                    return provider.Length > 0 && providerTool.Length > 0;
                }
            }

            if (toolName.StartsWith("mcp.", StringComparison.OrdinalIgnoreCase))
            {
                var parts = toolName.Split('.', 3, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts.Length == 3)
                {
                    provider = NormalizeName(parts[1]);
                    providerTool = NormalizeName(parts[2]);
                    return provider.Length > 0 && providerTool.Length > 0;
                }
            }

            var separatorIndex = toolName.IndexOf('_');
            if (separatorIndex > 0 && separatorIndex < toolName.Length - 1)
            {
                provider = NormalizeName(toolName[..separatorIndex]);
                providerTool = NormalizeName(toolName[(separatorIndex + 1)..]);
                return provider.Length > 0 && providerTool.Length > 0;
            }

            return false;
        }

        private static HashSet<string> LoadAllowedLocalTools(IExtensionRegistry? extensionRegistry)
        {
            var raw = Environment.GetEnvironmentVariable("HELPER_TOOL_PERMIT_LOCAL_ALLOWLIST");
            IEnumerable<string> tools;
            if (!string.IsNullOrWhiteSpace(raw))
            {
                tools = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            }
            else
            {
                var manifestTools = extensionRegistry?
                    .GetByCategory(ExtensionCategory.BuiltIn)
                    .SelectMany(x => x.DeclaredTools)
                    .ToArray();
                tools = manifestTools is { Length: > 0 }
                    ? manifestTools
                    : new[] { "dotnet_test", "read_file", "write_file" };
            }

            return tools
                .Select(NormalizeName)
                .Where(x => x.Length > 0)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        private static Dictionary<string, HashSet<string>> LoadMcpToolPolicy(IExtensionRegistry? extensionRegistry)
        {
            var map = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

            if (extensionRegistry is not null)
            {
                foreach (var manifest in extensionRegistry.GetSnapshot().Manifests.Where(IsMcpManifest))
                {
                    if (manifest.ToolPolicy.AllowAllTools)
                    {
                        map[manifest.Id] = new HashSet<string>(new[] { "*" }, StringComparer.OrdinalIgnoreCase);
                        continue;
                    }

                    if (manifest.ToolPolicy.AllowedTools.Count > 0)
                    {
                        map[manifest.Id] = manifest.ToolPolicy.AllowedTools.ToHashSet(StringComparer.OrdinalIgnoreCase);
                    }
                }
            }

            var raw = Environment.GetEnvironmentVariable("HELPER_MCP_PERMITTED_TOOLS");
            if (string.IsNullOrWhiteSpace(raw))
            {
                return map;
            }

            var items = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var item in items)
            {
                var pair = item.Split(':', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (pair.Length != 2)
                {
                    continue;
                }

                var provider = NormalizeName(pair[0]);
                if (provider.Length == 0)
                {
                    continue;
                }

                var tools = pair[1]
                    .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(x => x == "*" ? "*" : NormalizeName(x))
                    .Where(x => x.Length > 0)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (tools.Count == 0)
                {
                    continue;
                }

                if (!map.TryGetValue(provider, out var existing))
                {
                    map[provider] = tools;
                }
                else
                {
                    foreach (var tool in tools)
                    {
                        existing.Add(tool);
                    }
                }
            }

            return map;
        }

        private static HashSet<string> LoadManifestTrustedMcpProviders(IExtensionRegistry? extensionRegistry)
        {
            if (extensionRegistry is null)
            {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            return extensionRegistry
                .GetSnapshot()
                .Manifests
                .Where(IsMcpManifest)
                .Where(manifest => manifest.TrustLevel is ExtensionTrustLevel.TrustedExternal or ExtensionTrustLevel.Experimental)
                .Select(manifest => manifest.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        private static HashSet<string> LoadConfiguredTrustedMcpProviders()
        {
            var raw = Environment.GetEnvironmentVariable("HELPER_MCP_TRUSTED_SERVERS");
            if (string.IsNullOrWhiteSpace(raw))
            {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            return raw
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(x => x == "*" ? "*" : NormalizeName(x))
                .Where(x => x.Length > 0)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        private static bool IsMcpManifest(ExtensionManifest manifest)
        {
            return manifest.Transport == ExtensionTransport.Stdio &&
                string.Equals(manifest.ProviderType, "mcp", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var normalized = value.Trim().ToLowerInvariant();
            return Regex.Replace(normalized, @"[^a-z0-9_.-]", string.Empty);
        }

        private static bool ReadFlag(string envName, bool fallback)
        {
            var raw = Environment.GetEnvironmentVariable(envName);
            return bool.TryParse(raw, out var parsed) ? parsed : fallback;
        }
    }
}

