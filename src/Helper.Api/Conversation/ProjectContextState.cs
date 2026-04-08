namespace Helper.Api.Conversation;

public sealed record ProjectContextState(
    string ProjectId,
    string? Label,
    string? Instructions,
    bool MemoryEnabled,
    IReadOnlyList<string> ReferenceArtifacts,
    DateTimeOffset UpdatedAtUtc)
{
    public static ProjectContextState Empty(string projectId)
        => new(
            projectId,
            null,
            null,
            MemoryEnabled: true,
            Array.Empty<string>(),
            DateTimeOffset.UtcNow);
}
