using System;
using System.Collections.Generic;

namespace Helper.Runtime.Core
{
    public enum ExtensionCategory
    {
        BuiltIn,
        Internal,
        External,
        Experimental
    }

    public enum ExtensionTransport
    {
        None,
        Stdio
    }

    public enum ExtensionTrustLevel
    {
        BuiltIn,
        Internal,
        TrustedExternal,
        Experimental
    }

    public sealed record ExtensionToolPolicy(
        bool AllowAllTools,
        IReadOnlyList<string> AllowedTools);

    public sealed record ExtensionManifest(
        string SchemaVersion,
        string Id,
        string DisplayName,
        ExtensionCategory Category,
        string ProviderType,
        ExtensionTransport Transport,
        string Description,
        string? Command,
        IReadOnlyList<string> Args,
        IReadOnlyList<string> DeclaredTools,
        IReadOnlyList<string> RequiredEnv,
        IReadOnlyList<string> Capabilities,
        ExtensionTrustLevel TrustLevel,
        bool DefaultEnabled,
        bool DisabledInCertificationMode,
        bool QuietWhenUnavailable,
        ExtensionToolPolicy ToolPolicy,
        string SourcePath);

    public sealed record ExtensionRegistrySnapshot(
        IReadOnlyList<ExtensionManifest> Manifests,
        IReadOnlyList<string> Failures,
        IReadOnlyList<string> Warnings);

    public interface IExtensionRegistry
    {
        ExtensionRegistrySnapshot GetSnapshot();
        IReadOnlyList<ExtensionManifest> GetByCategory(ExtensionCategory category);
        bool TryGetManifest(string extensionId, out ExtensionManifest manifest);
    }
}

