namespace Helper.Api.Conversation;

public interface IProjectMemoryBoundaryPolicy
{
    bool ShouldInclude(ConversationMemoryItem item, ProjectContextState? projectContext);
}

public sealed class ProjectMemoryBoundaryPolicy : IProjectMemoryBoundaryPolicy
{
    public bool ShouldInclude(ConversationMemoryItem item, ProjectContextState? projectContext)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (projectContext is null || !projectContext.MemoryEnabled || string.IsNullOrWhiteSpace(projectContext.ProjectId))
        {
            return true;
        }

        return string.Equals(item.SourceProjectId, projectContext.ProjectId, StringComparison.OrdinalIgnoreCase) ||
               string.IsNullOrWhiteSpace(item.SourceProjectId);
    }
}
