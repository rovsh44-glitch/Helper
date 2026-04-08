using System.Text;

namespace Helper.Api.Conversation;

public interface IProjectInstructionPolicy
{
    string? BuildContextBlock(ProjectContextState? projectContext, ChatTurnContext context);
}

public sealed class ProjectInstructionPolicy : IProjectInstructionPolicy
{
    public string? BuildContextBlock(ProjectContextState? projectContext, ChatTurnContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (projectContext is null || string.IsNullOrWhiteSpace(projectContext.ProjectId))
        {
            return null;
        }

        var builder = new StringBuilder();
        builder.AppendLine("Project context:");
        builder.Append("- Project: ").AppendLine(projectContext.Label ?? projectContext.ProjectId);

        if (!string.IsNullOrWhiteSpace(projectContext.Instructions))
        {
            builder.Append("- Instructions: ").AppendLine(projectContext.Instructions.Trim());
        }

        if (projectContext.ReferenceArtifacts.Count > 0)
        {
            builder.Append("- Reference artifacts: ").AppendLine(string.Join(", ", projectContext.ReferenceArtifacts.Take(6)));
        }

        builder.Append("- Memory boundary: ").AppendLine(projectContext.MemoryEnabled ? "project_scoped" : "conversation_only");

        var block = builder.ToString().TrimEnd();
        return block.Equals("Project context:", StringComparison.Ordinal)
            ? null
            : block;
    }
}
