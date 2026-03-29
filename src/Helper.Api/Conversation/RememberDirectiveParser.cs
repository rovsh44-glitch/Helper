namespace Helper.Api.Conversation;

internal static class RememberDirectiveParser
{
    private const string RememberPrefix = "remember:";

    public static bool TryExtractFact(string? content, out string fact)
    {
        fact = string.Empty;
        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        var normalized = StripOptionalTagPrefix(content);
        if (!normalized.StartsWith(RememberPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        fact = normalized[RememberPrefix.Length..].Trim();
        return true;
    }

    private static string StripOptionalTagPrefix(string content)
    {
        var normalized = content.Trim();
        if (!normalized.StartsWith("[", StringComparison.Ordinal))
        {
            return normalized;
        }

        var closeIndex = normalized.IndexOf(']');
        if (closeIndex < 1 || closeIndex > 24)
        {
            return normalized;
        }

        return normalized[(closeIndex + 1)..].TrimStart();
    }
}

