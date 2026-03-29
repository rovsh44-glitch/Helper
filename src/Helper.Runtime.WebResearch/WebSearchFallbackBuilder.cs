using System.Text.RegularExpressions;

namespace Helper.Runtime.WebResearch;

public static partial class WebSearchFallbackBuilder
{
    [GeneratedRegex(@"https?://[^\s\)\]\}\""\>]+", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex UrlRegex();

    public static IReadOnlyList<WebSearchDocument> BuildFromQuery(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<WebSearchDocument>();
        }

        return UrlRegex()
            .Matches(query)
            .Select(match => match.Value.Trim().TrimEnd('.', ',', ';', ':'))
            .Where(IsHttpUrl)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(url => new WebSearchDocument(
                url,
                "User-provided source URL",
                "The runtime preserved this URL but could not fetch live page content during fallback.",
                IsFallback: true))
            .ToArray();
    }

    private static bool IsHttpUrl(string? candidate)
    {
        return Uri.TryCreate(candidate, UriKind.Absolute, out var uri) &&
               (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase));
    }
}

