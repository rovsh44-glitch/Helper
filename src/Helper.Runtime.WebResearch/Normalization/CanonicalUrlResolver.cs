using System.Text;

namespace Helper.Runtime.WebResearch.Normalization;

internal readonly record struct CanonicalUrlResolution(
    string CanonicalUrl,
    bool UsedExtractedCanonical,
    IReadOnlyList<string> Reasons);

internal interface ICanonicalUrlResolver
{
    CanonicalUrlResolution Resolve(WebSearchDocument document);
}

internal sealed class CanonicalUrlResolver : ICanonicalUrlResolver
{
    private static readonly string[] TrackingQueryPrefixes =
    {
        "utm_",
        "fbclid",
        "gclid",
        "mc_",
        "ref",
        "ref_",
        "source",
        "src",
        "cmp",
        "cmpid",
        "campaign",
        "aff",
        "affiliate"
    };

    public CanonicalUrlResolution Resolve(WebSearchDocument document)
    {
        var reasons = new List<string>();
        var rawUrl = ResolveInputUrl(document, reasons, out var usedExtractedCanonical);
        if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri) || !IsHttpUri(uri))
        {
            return new CanonicalUrlResolution(document.Url, usedExtractedCanonical, reasons.Count == 0 ? Array.Empty<string>() : reasons);
        }

        var scheme = uri.Scheme.ToLowerInvariant();
        var host = NormalizeHost(uri.Host, reasons);
        var path = NormalizePath(uri.AbsolutePath, reasons);
        var query = NormalizeQuery(uri.Query, reasons);

        var builder = new StringBuilder();
        builder.Append(scheme).Append("://").Append(host).Append(path);
        if (!string.IsNullOrWhiteSpace(query))
        {
            builder.Append('?').Append(query);
        }

        return new CanonicalUrlResolution(
            builder.ToString(),
            usedExtractedCanonical,
            reasons.Count == 0 ? Array.Empty<string>() : reasons);
    }

    private static string ResolveInputUrl(WebSearchDocument document, List<string> reasons, out bool usedExtractedCanonical)
    {
        usedExtractedCanonical = false;
        if (!string.IsNullOrWhiteSpace(document.ExtractedPage?.CanonicalUrl) &&
            Uri.TryCreate(document.ExtractedPage.CanonicalUrl, UriKind.Absolute, out var canonicalUri) &&
            IsHttpUri(canonicalUri))
        {
            usedExtractedCanonical = true;
            reasons.Add("page_canonical");
            return document.ExtractedPage.CanonicalUrl.Trim();
        }

        return (document.Url ?? string.Empty).Trim();
    }

    private static string NormalizeHost(string host, List<string> reasons)
    {
        var normalized = host.Trim().ToLowerInvariant();
        if (normalized.StartsWith("www.", StringComparison.Ordinal))
        {
            normalized = normalized[4..];
            reasons.Add("host_www_removed");
        }

        return normalized;
    }

    private static string NormalizePath(string path, List<string> reasons)
    {
        var normalized = string.IsNullOrWhiteSpace(path) ? "/" : path.Trim();
        while (normalized.Contains("//", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("//", "/", StringComparison.Ordinal);
            reasons.Add("path_slashes_collapsed");
        }

        if (normalized.EndsWith("/index.html", StringComparison.OrdinalIgnoreCase) ||
            normalized.EndsWith("/index.htm", StringComparison.OrdinalIgnoreCase) ||
            normalized.EndsWith("/index.php", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..normalized.LastIndexOf("/index", StringComparison.OrdinalIgnoreCase)];
            normalized = string.IsNullOrEmpty(normalized) ? "/" : normalized;
            reasons.Add("index_suffix_removed");
        }

        if (normalized.EndsWith("/amp", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^4];
            normalized = string.IsNullOrEmpty(normalized) ? "/" : normalized;
            reasons.Add("amp_suffix_removed");
        }

        if (normalized.Length > 1 && normalized.EndsWith("/", StringComparison.Ordinal))
        {
            normalized = normalized.TrimEnd('/');
            reasons.Add("trailing_slash_removed");
        }

        return normalized;
    }

    private static string NormalizeQuery(string query, List<string> reasons)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return string.Empty;
        }

        var cleaned = new List<string>();
        var parts = query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            var separator = part.IndexOf('=', StringComparison.Ordinal);
            var key = separator >= 0 ? part[..separator] : part;
            var value = separator >= 0 ? part[(separator + 1)..] : string.Empty;
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            if (ShouldRemoveQueryKey(key))
            {
                reasons.Add($"query_removed:{key.ToLowerInvariant()}");
                continue;
            }

            cleaned.Add(separator >= 0
                ? $"{key.ToLowerInvariant()}={value}"
                : key.ToLowerInvariant());
        }

        cleaned.Sort(StringComparer.OrdinalIgnoreCase);
        return string.Join("&", cleaned);
    }

    private static bool ShouldRemoveQueryKey(string key)
    {
        foreach (var prefix in TrackingQueryPrefixes)
        {
            if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                key.Equals(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsHttpUri(Uri uri)
    {
        return uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
               uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
    }
}

