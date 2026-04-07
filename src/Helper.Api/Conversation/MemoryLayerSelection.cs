using Helper.Runtime.Core;
using Helper.Api.Hosting;

namespace Helper.Api.Conversation;

public sealed record MemoryLayerSelection(
    IReadOnlyList<string> Layers,
    int HistoryMessageBudget,
    int ProceduralLessonBudget,
    int RetrievalChunkBudget)
{
    public static MemoryLayerSelection Resolve(ChatTurnContext context)
    {
        var layers = new List<string> { "recent_history" };
        var historyBudget = context.ExecutionMode switch
        {
            TurnExecutionMode.Fast => 8,
            TurnExecutionMode.Deep => 16,
            _ => 12
        };
        var lessonBudget = context.ExecutionMode == TurnExecutionMode.Deep ? 3 : 2;
        var retrievalBudget = context.ExecutionMode switch
        {
            TurnExecutionMode.Fast => 2,
            TurnExecutionMode.Deep => 5,
            _ => 3
        };

        if (context.History.Any(IsBranchSummaryMessage))
        {
            layers.Add("branch_summary");
        }

        if (context.History.Any(IsRollingSummaryMessage))
        {
            layers.Add("rolling_summary");
        }

        if (context.History.Any(IsConversationProfileMessage))
        {
            layers.Add("conversation_profile");
        }

        if (context.Intent.Intent is IntentType.Research or IntentType.Generate || context.ExecutionMode == TurnExecutionMode.Deep)
        {
            layers.Add("procedural_lessons");
        }

        if (context.IsFactualPrompt || context.Intent.Intent == IntentType.Research || context.ExecutionMode != TurnExecutionMode.Fast)
        {
            layers.Add("structured_retrieval");
        }

        return new MemoryLayerSelection(
            layers.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            HistoryMessageBudget: historyBudget,
            ProceduralLessonBudget: lessonBudget,
            RetrievalChunkBudget: retrievalBudget);
    }

    internal static bool IsBranchSummaryMessage(ChatMessageDto message)
        => string.Equals(message.Role, "system", StringComparison.OrdinalIgnoreCase) &&
           message.Content.StartsWith("Conversation summary (", StringComparison.OrdinalIgnoreCase);

    internal static bool IsRollingSummaryMessage(ChatMessageDto message)
        => string.Equals(message.Role, "system", StringComparison.OrdinalIgnoreCase) &&
           message.Content.StartsWith("Conversation summary:", StringComparison.OrdinalIgnoreCase);

    internal static bool IsConversationProfileMessage(ChatMessageDto message)
        => string.Equals(message.Role, "system", StringComparison.OrdinalIgnoreCase) &&
           (message.Content.StartsWith("User preferences:", StringComparison.OrdinalIgnoreCase) ||
            message.Content.StartsWith("Conversation profile:", StringComparison.OrdinalIgnoreCase) ||
            message.Content.StartsWith("Open tasks:", StringComparison.OrdinalIgnoreCase));
}

