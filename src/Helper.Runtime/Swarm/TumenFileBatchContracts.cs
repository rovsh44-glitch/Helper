using Helper.Runtime.Core;
using Helper.Runtime.Generation;

namespace Helper.Runtime.Swarm;

internal sealed record TumenFileBatchRequest(
    string RawProjectRoot,
    string RootNamespace,
    string PlatformOs,
    IReadOnlyList<SwarmFileDefinition> FileDefinitions,
    Action<string>? OnProgress);

internal sealed record TumenFileBatchResult(
    IReadOnlyList<GeneratedFile> Files,
    IReadOnlyList<GeneratedArtifactPlaceholderFinding> PlaceholderFindings,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings,
    int MethodCount,
    int RetryCount);

