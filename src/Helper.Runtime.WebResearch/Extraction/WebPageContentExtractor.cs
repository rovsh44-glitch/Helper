using System.Net;
using System.Text.RegularExpressions;

namespace Helper.Runtime.WebResearch.Extraction;

public interface IWebPageContentExtractor
{
    ExtractedWebPage? Extract(Uri requestedUri, Uri resolvedUri, string? contentType, string content);
}

public sealed class WebPageContentExtractor : IWebPageContentExtractor
{
    private static readonly Regex CanonicalHrefRegex = new(
        "<link[^>]*rel\\s*=\\s*[\"'][^\"']*canonical[^\"']*[\"'][^>]*href\\s*=\\s*[\"'](?<href>[^\"']+)[\"'][^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex OgTitleRegex = new(
        "<meta[^>]*(?:property|name)\\s*=\\s*[\"'](?:og:title|twitter:title)[\"'][^>]*content\\s*=\\s*[\"'](?<content>[^\"']+)[\"'][^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex TitleRegex = new(
        "<title[^>]*>(?<content>.*?)</title>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex DateRegex = new(
        "<meta[^>]*(?:property|name|itemprop)\\s*=\\s*[\"'](?:article:published_time|article:modified_time|og:updated_time|date|datePublished|dateModified|pubdate)[\"'][^>]*content\\s*=\\s*[\"'](?<content>[^\"']+)[\"'][^>]*>|<time[^>]*datetime\\s*=\\s*[\"'](?<datetime>[^\"']+)[\"'][^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex ScriptLikeRegex = new(
        "<(script|style|noscript|template|svg|iframe)[^>]*>.*?</\\1>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex BlockElementRegex = new(
        "</?(address|article|aside|blockquote|br|div|figcaption|figure|footer|form|h[1-6]|header|hr|li|main|nav|ol|p|pre|section|table|tbody|td|th|thead|tr|ul)[^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex TagRegex = new(
        "<[^>]+>",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex WhitespaceRegex = new(
        "\\s+",
        RegexOptions.Compiled);

    public ExtractedWebPage? Extract(Uri requestedUri, Uri resolvedUri, string? contentType, string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        var normalizedContentType = WebSourceTypeExtractionSupport.NormalizeMediaType(contentType);
        var sourceType = WebSourceTypeClassifier.Classify(requestedUri, resolvedUri, normalizedContentType);
        if (normalizedContentType.Equals("text/plain", StringComparison.OrdinalIgnoreCase))
        {
            return ExtractPlainText(requestedUri, resolvedUri, normalizedContentType, content, sourceType);
        }

        return ExtractHtml(requestedUri, resolvedUri, normalizedContentType, content, sourceType);
    }

    private static ExtractedWebPage? ExtractHtml(Uri requestedUri, Uri resolvedUri, string contentType, string content, WebSourceTypeProfile sourceType)
    {
        var canonicalUrl = ResolveCanonicalUrl(resolvedUri, content);
        var title = DecodeAndNormalize(FirstNonEmpty(
            MatchGroupValue(OgTitleRegex, content, "content"),
            MatchGroupValue(TitleRegex, content, "content")));
        var publishedAt = NormalizePublishedAt(FirstNonEmpty(
            MatchGroupValue(DateRegex, content, "content"),
            MatchGroupValue(DateRegex, content, "datetime")));
        var body = BuildBodyFromHtml(content, sourceType);
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        var passages = BuildPassages(body, sourceType);
        if (passages.Count == 0)
        {
            return null;
        }

        return new ExtractedWebPage(
            requestedUri.AbsoluteUri,
            resolvedUri.AbsoluteUri,
            canonicalUrl,
            string.IsNullOrWhiteSpace(title) ? resolvedUri.Host : title,
            publishedAt,
            body,
            passages,
            contentType);
    }

    private static ExtractedWebPage? ExtractPlainText(Uri requestedUri, Uri resolvedUri, string contentType, string content, WebSourceTypeProfile sourceType)
    {
        var rawFirstLine = content
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();
        var body = NormalizeBody(content, sourceType);
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        var passages = BuildPassages(body, sourceType);
        if (passages.Count == 0)
        {
            return null;
        }

        var title = string.IsNullOrWhiteSpace(rawFirstLine) ? resolvedUri.Host : DecodeAndNormalize(rawFirstLine);
        return new ExtractedWebPage(
            requestedUri.AbsoluteUri,
            resolvedUri.AbsoluteUri,
            resolvedUri.AbsoluteUri,
            title,
            PublishedAt: null,
            body,
            passages,
            contentType);
    }

    private static string BuildBodyFromHtml(string html, WebSourceTypeProfile sourceType)
    {
        var stripped = ScriptLikeRegex.Replace(html, " ");
        stripped = BlockElementRegex.Replace(stripped, "\n");
        stripped = TagRegex.Replace(stripped, " ");
        stripped = WebUtility.HtmlDecode(stripped);
        return NormalizeBody(stripped, sourceType);
    }

    private static string NormalizeBody(string content, WebSourceTypeProfile sourceType)
    {
        var lines = content
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.TrimEntries)
            .Select(static line => WhitespaceRegex.Replace(line, " ").Trim())
            .Where(line => IsUsefulLine(line, sourceType))
            .Where(line => !LooksLikeChromeLine(line, sourceType))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (lines.Length == 0)
        {
            return string.Empty;
        }

        return string.Join("\n\n", lines);
    }

    private static IReadOnlyList<ExtractedWebPassage> BuildPassages(string body, WebSourceTypeProfile sourceType)
    {
        var paragraphs = body
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(paragraph => paragraph.Length >= sourceType.MinimumUsefulLineLength)
            .ToArray();
        if (paragraphs.Length == 0)
        {
            return Array.Empty<ExtractedWebPassage>();
        }

        var passages = new List<ExtractedWebPassage>();
        var buffer = new List<string>();
        var targetLength = sourceType.PassageTargetLength;

        foreach (var paragraph in paragraphs)
        {
            buffer.Add(paragraph);
            var current = string.Join(" ", buffer);
            if (current.Length >= targetLength || buffer.Count >= 2)
            {
                passages.Add(new ExtractedWebPassage(passages.Count + 1, current));
                buffer.Clear();
            }

            if (passages.Count >= sourceType.MaxPassages)
            {
                break;
            }
        }

        if (buffer.Count > 0 && passages.Count < sourceType.MaxPassages)
        {
            passages.Add(new ExtractedWebPassage(passages.Count + 1, string.Join(" ", buffer)));
        }

        return passages
            .Where(static passage => !string.IsNullOrWhiteSpace(passage.Text))
            .ToArray();
    }

    private static bool IsUsefulLine(string line, WebSourceTypeProfile sourceType)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        if (line.Length >= sourceType.MinimumUsefulLineLength)
        {
            return true;
        }

        var wordCount = line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        return wordCount >= sourceType.MinimumUsefulWordCount && line.Any(char.IsLetterOrDigit);
    }

    private static bool LooksLikeChromeLine(string line, WebSourceTypeProfile sourceType)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        if (line.Length > 140 && !sourceType.StrictChromeRejection)
        {
            return false;
        }

        return WebChromePatternCatalog.Matches(line);
    }

    private static string DecodeAndNormalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return WhitespaceRegex.Replace(WebUtility.HtmlDecode(value), " ").Trim();
    }

    private static string ResolveCanonicalUrl(Uri resolvedUri, string html)
    {
        var href = MatchGroupValue(CanonicalHrefRegex, html, "href");
        if (string.IsNullOrWhiteSpace(href))
        {
            return resolvedUri.AbsoluteUri;
        }

        return Uri.TryCreate(resolvedUri, WebUtility.HtmlDecode(href.Trim()), out var canonicalUri) &&
               (canonicalUri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                canonicalUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            ? canonicalUri.AbsoluteUri
            : resolvedUri.AbsoluteUri;
    }

    private static string? NormalizePublishedAt(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var trimmed = WebUtility.HtmlDecode(raw).Trim();
        if (DateTimeOffset.TryParse(trimmed, out var parsed))
        {
            return parsed.ToString("yyyy-MM-dd");
        }

        return trimmed.Length > 40 ? trimmed[..40].TrimEnd() : trimmed;
    }

    private static string? MatchGroupValue(Regex regex, string content, string groupName)
    {
        var match = regex.Match(content);
        if (!match.Success)
        {
            return null;
        }

        var value = match.Groups[groupName].Value;
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string? FirstNonEmpty(params string?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}

