using Helper.Runtime.WebResearch.Fetching;

namespace Helper.Runtime.WebResearch.Extraction;

public interface IContentTypeAdmissionPolicy
{
    ContentTypeAdmissionDecision Evaluate(Uri requestUri, string? contentType, long? contentLength);
}

public sealed class ContentTypeAdmissionPolicy : IContentTypeAdmissionPolicy
{
    private static readonly string[] AllowedMediaTypes =
    {
        "text/html",
        "application/xhtml+xml",
        "text/plain",
        "application/pdf"
    };

    private readonly int _maxResponseBytes;

    public ContentTypeAdmissionPolicy()
        : this(WebPageFetchSettings.ReadMaxResponseBytes())
    {
    }

    public ContentTypeAdmissionPolicy(int maxResponseBytes)
    {
        _maxResponseBytes = Math.Clamp(maxResponseBytes, 16_384, 2_000_000);
    }

    public ContentTypeAdmissionDecision Evaluate(Uri requestUri, string? contentType, long? contentLength)
    {
        var normalizedContentType = NormalizeMediaType(contentType);
        if (contentLength is > 0 && contentLength > _maxResponseBytes)
        {
            if (AllowsPartialRead(normalizedContentType))
            {
                return Allow(
                    "content_length_over_budget_partial_read",
                    requestUri,
                    normalizedContentType,
                    contentLength);
            }

            return Block(
                "content_length_exceeded",
                requestUri,
                normalizedContentType,
                $"web_page_fetch.content_length={contentLength}");
        }

        if (string.IsNullOrWhiteSpace(normalizedContentType))
        {
            return Allow(
                "content_type_missing_assumed_html",
                requestUri,
                normalizedContentType,
                contentLength);
        }

        if (AllowedMediaTypes.Contains(normalizedContentType, StringComparer.OrdinalIgnoreCase))
        {
            return Allow(
                "content_type_admitted",
                requestUri,
                normalizedContentType,
                contentLength);
        }

        return Block(
            "unsupported_content_type",
            requestUri,
            normalizedContentType,
            contentLength is > 0 ? $"web_page_fetch.content_length={contentLength}" : null);
    }

    private static bool AllowsPartialRead(string? normalizedContentType)
    {
        return normalizedContentType is null or "text/html" or "application/xhtml+xml" or "text/plain";
    }

    private ContentTypeAdmissionDecision Allow(string reason, Uri requestUri, string? contentType, long? contentLength)
    {
        var trace = new List<string>
        {
            $"web_page_fetch.content_type_allowed reason={reason} target={requestUri} content_type={contentType ?? "unknown"}",
            $"web_page_fetch.max_bytes={_maxResponseBytes}"
        };

        if (contentLength is > 0)
        {
            trace.Add($"web_page_fetch.content_length={contentLength}");
        }

        return new ContentTypeAdmissionDecision(true, reason, trace);
    }

    private static ContentTypeAdmissionDecision Block(
        string reason,
        Uri requestUri,
        string? contentType,
        string? detail)
    {
        var trace = new List<string>
        {
            $"web_page_fetch.content_type_blocked reason={reason} target={requestUri} content_type={contentType ?? "unknown"}"
        };

        if (!string.IsNullOrWhiteSpace(detail))
        {
            trace.Add(detail);
        }

        return new ContentTypeAdmissionDecision(false, reason, trace);
    }

    private static string? NormalizeMediaType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return null;
        }

        var semicolonIndex = contentType.IndexOf(';');
        var mediaType = semicolonIndex >= 0
            ? contentType[..semicolonIndex]
            : contentType;

        var normalized = mediaType.Trim().ToLowerInvariant();
        return normalized.Length == 0 ? null : normalized;
    }
}

