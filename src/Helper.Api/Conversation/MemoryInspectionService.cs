namespace Helper.Api.Conversation;

public sealed record MemoryInspectionItem(
    string Id,
    string Type,
    string Content,
    MemoryScope Scope,
    string Retention,
    string WhyRemembered,
    int Priority,
    string? SourceTurnId,
    string? SourceProjectId,
    bool IsPersonal,
    bool UserEditable,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt);

public interface IMemoryInspectionService
{
    IReadOnlyList<MemoryInspectionItem> BuildSnapshot(ConversationState state, DateTimeOffset now);
}

public sealed class MemoryInspectionService : IMemoryInspectionService
{
    private readonly IProjectMemoryBoundaryPolicy _projectMemoryBoundaryPolicy;

    public MemoryInspectionService(IProjectMemoryBoundaryPolicy? projectMemoryBoundaryPolicy = null)
    {
        _projectMemoryBoundaryPolicy = projectMemoryBoundaryPolicy ?? new ProjectMemoryBoundaryPolicy();
    }

    public IReadOnlyList<MemoryInspectionItem> BuildSnapshot(ConversationState state, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(state);

        lock (state.SyncRoot)
        {
            return state.MemoryItems
                .Where(item => !item.ExpiresAt.HasValue || item.ExpiresAt.Value > now)
                .Where(item => _projectMemoryBoundaryPolicy.ShouldInclude(item, state.ProjectContext))
                .OrderByDescending(item => item.Priority)
                .ThenByDescending(item => item.CreatedAt)
                .Select(item => new MemoryInspectionItem(
                    item.Id,
                    item.Type,
                    item.Content,
                    item.Scope,
                    item.Retention,
                    item.WhyRemembered,
                    item.Priority,
                    item.SourceTurnId,
                    item.SourceProjectId,
                    item.IsPersonal,
                    item.UserEditable,
                    item.CreatedAt,
                    item.ExpiresAt))
                .ToArray();
        }
    }
}
