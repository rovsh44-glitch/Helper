using System.Text;
using Microsoft.CodeAnalysis.CSharp;

namespace Helper.GeneratedArtifactValidation.Core;

public sealed class IdentifierSanitizer
{
    public string SanitizeProjectName(string value) => SanitizeSingle(value, "GeneratedProject");

    public string SanitizeNamespace(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "GeneratedNamespace";
        }

        var parts = value
            .Split('.', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => SanitizeSingle(x, "Ns"))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray();

        return parts.Length == 0 ? "GeneratedNamespace" : string.Join('.', parts);
    }

    public string SanitizeTypeName(string value) => SanitizeSingle(value, "GeneratedType");

    public string SanitizeMethodName(string value) => SanitizeSingle(value, "Execute");

    private static string SanitizeSingle(string value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var trimmed = value.Trim();
        var builder = new StringBuilder(trimmed.Length + 2);
        var previousUnderscore = false;

        foreach (var ch in trimmed)
        {
            if (char.IsLetterOrDigit(ch) || ch == '_')
            {
                builder.Append(ch);
                previousUnderscore = false;
                continue;
            }

            if (!previousUnderscore)
            {
                builder.Append('_');
                previousUnderscore = true;
            }
        }

        var normalized = builder.ToString().Trim('_');
        if (string.IsNullOrWhiteSpace(normalized))
        {
            normalized = fallback;
        }

        if (!char.IsLetter(normalized[0]) && normalized[0] != '_')
        {
            normalized = "_" + normalized;
        }

        if (!SyntaxFacts.IsValidIdentifier(normalized))
        {
            normalized = "_" + normalized;
        }

        if (SyntaxFacts.GetKeywordKind(normalized) != Microsoft.CodeAnalysis.CSharp.SyntaxKind.None)
        {
            normalized = "_" + normalized;
        }

        return normalized;
    }
}

