using System.Text;
using Helper.Api.Hosting;

namespace Helper.Api.Conversation;

public static class ChatPromptFormatter
{
    public static string BuildConversationPrompt(IReadOnlyList<ChatMessageDto> history)
        => BuildConversationPrompt(history, null);

    public static string BuildConversationPrompt(IReadOnlyList<ChatMessageDto> history, IReadOnlyList<string>? contextBlocks)
    {
        var prompt = new StringBuilder();
        if (contextBlocks is { Count: > 0 })
        {
            prompt.AppendLine("Additional context:");
            foreach (var block in contextBlocks.Where(block => !string.IsNullOrWhiteSpace(block)))
            {
                prompt.AppendLine(block);
                prompt.AppendLine();
            }
        }

        prompt.AppendLine("Conversation:");

        foreach (var message in history)
        {
            var role = message.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase) ? "Assistant" : "User";
            prompt.Append(role);
            prompt.Append(": ");
            prompt.AppendLine(message.Content);

            if (message.Attachments is { Count: > 0 })
            {
                prompt.AppendLine($"Attachments: {string.Join(", ", message.Attachments.Select(x => $"{x.Name}({x.Type},{x.SizeBytes}B)"))}");
            }
        }

        prompt.AppendLine();
        prompt.Append("Assistant:");
        return prompt.ToString();
    }
}

