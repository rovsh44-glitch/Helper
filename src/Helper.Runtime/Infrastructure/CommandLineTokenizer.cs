using System.Text;

namespace Helper.Runtime.Infrastructure;

internal static class CommandLineTokenizer
{
    public static IReadOnlyList<string> Split(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return Array.Empty<string>();
        }

        var tokens = new List<string>();
        var current = new StringBuilder();
        bool inSingleQuotes = false;
        bool inDoubleQuotes = false;

        foreach (var character in commandLine)
        {
            if (character == '"' && !inSingleQuotes)
            {
                inDoubleQuotes = !inDoubleQuotes;
                continue;
            }

            if (character == '\'' && !inDoubleQuotes)
            {
                inSingleQuotes = !inSingleQuotes;
                continue;
            }

            if (char.IsWhiteSpace(character) && !inSingleQuotes && !inDoubleQuotes)
            {
                FlushCurrentToken(tokens, current);
                continue;
            }

            current.Append(character);
        }

        FlushCurrentToken(tokens, current);
        return tokens;
    }

    private static void FlushCurrentToken(List<string> tokens, StringBuilder current)
    {
        if (current.Length == 0)
        {
            return;
        }

        tokens.Add(current.ToString());
        current.Clear();
    }
}
