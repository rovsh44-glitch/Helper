using System.Text.RegularExpressions;

namespace Helper.Api.Conversation;

internal static partial class ConversationEvidenceIdentitySupport
{
    public static string BuildEvidenceKey(
        string? url,
        string? evidenceKind,
        string? passageId,
        int? passageOrdinal,
        string? title,
        string? text)
    {
        var normalizedUrl = NormalizeUrl(url);
        var normalizedKind = string.IsNullOrWhiteSpace(evidenceKind) ? "unknown" : evidenceKind.Trim().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(passageId))
        {
            return $"{normalizedUrl}|{normalizedKind}|passage:{passageId.Trim().ToLowerInvariant()}";
        }

        if (passageOrdinal.HasValue)
        {
            return $"{normalizedUrl}|{normalizedKind}|passage_ordinal:{passageOrdinal.Value}";
        }

        var normalizedTitle = NormalizeText(title);
        var normalizedText = NormalizeText(text);
        var tail = normalizedText[..Math.Min(80, normalizedText.Length)];
        return $"{normalizedUrl}|{normalizedKind}|{normalizedTitle}|{tail}";
    }

    public static string NormalizeUrl(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return (url ?? string.Empty).Trim().ToLowerInvariant();
        }

        var host = uri.Host.ToLowerInvariant();
        if (host.StartsWith("www.", StringComparison.Ordinal))
        {
            host = host[4..];
        }

        var path = uri.AbsolutePath.TrimEnd('/');
        return $"{host}{path.ToLowerInvariant()}";
    }

    public static string NormalizeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return SpaceRegex()
            .Replace(NonWordRegex().Replace(value.ToLowerInvariant(), " "), " ")
            .Trim();
    }

    public static string Summarize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "unknown";
        }

        return value.Length <= 72 ? value : value[..72];
    }

    [GeneratedRegex("[^\\p{L}\\p{Nd}]+", RegexOptions.Compiled)]
    private static partial Regex NonWordRegex();

    [GeneratedRegex("\\s+", RegexOptions.Compiled)]
    private static partial Regex SpaceRegex();
}

