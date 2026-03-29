using System.Text.RegularExpressions;
using Helper.Runtime.Core;

namespace Helper.Api.Conversation;

internal static class ResponseComposerSupport
{
    private static readonly Regex SourcesSectionRegex = new(@"(^|\n)\s*(Sources|Источники):", RegexOptions.Compiled);

    public static bool ContainsSourcesSection(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        return SourcesSectionRegex.IsMatch(content);
    }

    public static bool IsOperationalTurn(ChatTurnContext context)
    {
        if (context.Intent.Intent == IntentType.Generate)
        {
            return true;
        }

        if (context.ToolCalls.Any(x => string.Equals(x, "helper.generate", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return context.ExecutionOutput.StartsWith("Failed to generate project.", StringComparison.OrdinalIgnoreCase);
    }
}

